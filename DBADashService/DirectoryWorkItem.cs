using AsyncKeyedLock;
using DBADash;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DBADashService
{
    public sealed class DirectoryWorkItem : IWorkItem
    {
        private static readonly AsyncKeyedLocker<string> _folderLocker = new();
        private const uint ERROR_SHARING_VIOLATION = 0x80070020;
        private static readonly TimeSpan StaleTempFileAge = TimeSpan.FromHours(1);

        public DBADashSource Source { get; set; }

        public string DedupKey => Source.ConnectionString;

        public string Schedule { get; set; }

        public WorkItemPriority Priority { get; set; } = WorkItemPriority.Normal;

        public string Description => $"Import from {Source.ConnectionString}";

        public async Task ExecuteAsync(CollectionConfig config, CancellationToken cancellationToken)
        {
            var folder = Source.GetSource();
            Log.Logger.Information("Import from folder {folder}", folder);
            if (!Directory.Exists(folder))
            {
                Log.Error("Source directory doesn't exist {folder}", folder);
                return;
            }

            // One job per folder at a time
            using (await _folderLocker.LockAsync(Source.ConnectionString).ConfigureAwait(false))
            {
                List<string> allFiles;
                try
                {
                    allFiles = Directory.EnumerateFiles(folder, DestinationHandling.FileSearchPattern, SearchOption.TopDirectoryOnly)
                                        .ToList();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Enumerate files {folder}", folder);
                    return;
                }
                DeleteStaleTempFiles(allFiles);
                var files = allFiles.Where(f => f.EndsWith(DestinationHandling.FileExtension)).ToList();

                var filesByInstance = GetFilesToProcessByInstance(files);
                var tasks = filesByInstance.Select(kv => ProcessInstanceFilesAsync(kv.Value, Source, config, cancellationToken)).ToList();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Process a given list of files in order for a specific instance, writing collected data to the DBADash repository database
        /// </summary>
        private static async Task ProcessInstanceFilesAsync(List<string> files, DBADashSource source, CollectionConfig config, CancellationToken cancellationToken)
        {
            files.Sort(); // Ensure we process files in order
            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await ProcessFileAsync(f, source, config))
                {
                    TryDeleteFile(f);
                }
            }
        }

        /// <summary>
        /// Import a single file.  Returns true if the file has been dealt with and can be removed from the
        /// source folder, or false if it should be left in place and retried on the next iteration.
        /// A file that can't be imported is moved to the failed message folder rather than being left in
        /// place - files are processed in order, so leaving it would block every file behind it for this
        /// instance indefinitely.
        /// </summary>
        private static async Task<bool> ProcessFileAsync(string f, DBADashSource source, CollectionConfig config, int tryCount = 1)
        {
            const int MaxTryCount = 5;
            const int RetryDelay = 10;
            Log.Information("Processing file {0}", f);
            var fileName = Path.GetFileName(f);
            try
            {
                var ds = DataSetSerialization.DeserializeFromFile(f);
                var id = GetID(ds);
                using (await Locker.AsyncLocker.LockAsync(id))
                {
                    await DestinationHandling.WriteAllDestinationsAsync(ds, source, fileName, config);
                }
                return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) // Deleted after the folder was listed.  e.g. Another service importing from the same folder.
            {
                Log.Information("File {FileName} no longer exists.  Skipping", fileName);
                return false;
            }
            catch (IOException ex) when ((uint)ex.HResult == ERROR_SHARING_VIOLATION) // Another process has a lock on the file.  It might still be being written to.
            {
                if (tryCount > MaxTryCount)
                {
                    Log.Warning("File {FileName} is in use.  Exceeded max wait/retry.  File will be processed on the next iteration", fileName);
                    return false; // Leave the file in place.  It hasn't been imported.
                }
                Log.Information("File {FileName} is in use.  Waiting for lock to release. Attempt {TryCount}/{MaxRetryCount}", fileName, tryCount, MaxTryCount);
                await Task.Delay(RetryDelay);
                return await ProcessFileAsync(f, source, config, tryCount + 1);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error importing from {filename}.  File will be moved to {failedMessageFolder}", fileName, SchedulerServiceConfig.FailedMessageFolder);
                return TryMoveToFailedMessageFolder(f);
            }
        }

        internal static string GetID(DataSet ds)
        {
            try
            {
                return ds.Tables["DBADash"].Rows[0]["Instance"] + "_" + ds.Tables["DBADash"].Rows[0]["DBName"];
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting ID from DataSet");
                return "DEFAULT";
            }
        }

        /// <summary>
        /// Parse Instance from filename.  File format is DBADash_YYYYMMDD_HHMM_SS_{InstanceName}_{random}.xml
        /// </summary>
        public static string ParseInstance(string fileName)
        {
            return fileName[25..fileName.LastIndexOf('_')];
        }

        /// <summary>
        /// Split file list by Instance parsed from the filename.  Each instance will have 1 item in the dictionary containing a list of files to process for that instance
        /// </summary>
        internal static Dictionary<string, List<string>> GetFilesToProcessByInstance(List<string> files)
        {
            Dictionary<string, List<string>> filesToProcessByInstance = new();
            foreach (var path in files)
            {
                string instance;
                try
                {
                    instance = ParseInstance(Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    instance = "default";
                    Log.Warning("Unable to parse Instance from {0}: {1}", path, ex.Message);
                }
                if (filesToProcessByInstance.TryGetValue(instance, out var value))
                {
                    value.Add(path);
                }
                else
                {
                    filesToProcessByInstance.Add(instance, new List<string> { path });
                }
            }
            return filesToProcessByInstance;
        }

        /// <summary>
        /// Move a file that couldn't be imported to the failed message folder so it doesn't block the files
        /// queued behind it.  Files are removed from that folder after 7 days (SchedulerService.FolderCleanup).
        /// Returns true if the file has been dealt with, false if it has to be left where it is.
        /// </summary>
        private static bool TryMoveToFailedMessageFolder(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            try
            {
                if (string.IsNullOrEmpty(SchedulerServiceConfig.FailedMessageFolder))
                {
                    throw new Exception("Failed message folder is not available");
                }
                // Note: Path.GetFileName is required here.  filePath is rooted and Path.Combine returns a rooted
                // second argument unchanged, which would make this a move of the file over itself.
                File.Move(filePath, Path.Combine(SchedulerServiceConfig.FailedMessageFolder, fileName), true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error moving {FileName} to failed message folder {failedMessageFolder}.  The file will be left in place", fileName, SchedulerServiceConfig.FailedMessageFolder);
                return false;
            }
        }

        /// <summary>
        /// Temp files are renamed to .xml once the write completes (see DestinationHandling.WriteFolderAsync).
        /// A service killed mid-write leaves one behind that will never be renamed, so remove it once it's old
        /// enough that it can't still be in the process of being written.
        /// </summary>
        private static void DeleteStaleTempFiles(IEnumerable<string> files)
        {
            try
            {
                var cutOff = DateTime.UtcNow.Subtract(StaleTempFileAge);
                foreach (var f in files.Where(f => f.EndsWith(DestinationHandling.TempFileExtension)
                                                   && File.GetLastWriteTimeUtc(f) < cutOff))
                {
                    Log.Warning("Deleting incomplete file {FileName}", Path.GetFileName(f));
                    TryDeleteFile(f);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting incomplete files");
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting file");
            }
        }
    }
}