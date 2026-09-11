using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DBADash.Test
{
    /// <summary>
    /// Tests for writing to and reading from a folder destination.  A service importing from the folder
    /// processes files for an instance in order and can't skip a file it fails to read, so a file must never
    /// be visible to it until it's complete.
    /// </summary>
    [TestClass]
    public class FolderDestinationTests
    {
        private string _folder = string.Empty;

        [TestInitialize]
        public void TestInitialize()
        {
            _folder = Path.Combine(Path.GetTempPath(), "DBADashTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // Leave it to the temp folder cleanup
            }
        }

        private static DataSet GetTestDataSet(int rowCount)
        {
            var ds = new DataSet("Test");
            var dt = new DataTable("TestTable");
            dt.Columns.Add("Value", typeof(string));
            for (var i = 0; i < rowCount; i++)
            {
                dt.Rows.Add("Row " + i);
            }
            ds.Tables.Add(dt);
            return ds;
        }

        [TestMethod]
        public async Task WriteFolderAsync_WritesFileThatCanBeReadBack()
        {
            var fileName = DestinationHandling.FileNamePrefix + "Test" + DestinationHandling.FileExtension;
            await DestinationHandling.WriteFolderAsync(GetTestDataSet(3), _folder, fileName, new CollectionConfig());

            var ds = DataSetSerialization.DeserializeFromFile(Path.Combine(_folder, fileName));
            Assert.AreEqual(3, ds.Tables["TestTable"]!.Rows.Count);
        }

        [TestMethod]
        public async Task WriteFolderAsync_DoesNotLeaveTempFileBehind()
        {
            var fileName = DestinationHandling.FileNamePrefix + "Test" + DestinationHandling.FileExtension;
            await DestinationHandling.WriteFolderAsync(GetTestDataSet(3), _folder, fileName, new CollectionConfig());

            CollectionAssert.AreEqual(new[] { fileName },
                Directory.GetFiles(_folder).Select(Path.GetFileName).ToArray());
        }

        [TestMethod]
        public async Task WriteFolderAsync_OverwritingExistingFileDoesNotLeaveTrailingContent()
        {
            // The file is written under a temp name and renamed, so an existing file is always replaced
            // rather than partially overwritten.  A file left with the tail of a longer document can never
            // be imported and would block the files behind it.
            var fileName = DestinationHandling.FileNamePrefix + "Test" + DestinationHandling.FileExtension;
            await DestinationHandling.WriteFolderAsync(GetTestDataSet(100), _folder, fileName, new CollectionConfig());
            await DestinationHandling.WriteFolderAsync(GetTestDataSet(1), _folder, fileName, new CollectionConfig());

            var ds = DataSetSerialization.DeserializeFromFile(Path.Combine(_folder, fileName));
            Assert.AreEqual(1, ds.Tables["TestTable"]!.Rows.Count);
        }

        [TestMethod]
        public void TempFileName_IsNotPickedUpAsACompletedFile()
        {
            // The importer lists DestinationHandling.FileSearchPattern and imports what ends with
            // FileExtension - the temp file has to be excluded by that filter.
            var tempFileName = DestinationHandling.FileNamePrefix + "Test" + DestinationHandling.FileExtension +
                               DestinationHandling.TempFileExtension;

            Assert.IsFalse(tempFileName.EndsWith(DestinationHandling.FileExtension));
        }

        [TestMethod]
        public void DeserializeFromFile_MissingFile_ThrowsAndDoesNotCreateTheFile()
        {
            // A file that was deleted between being listed and read (e.g. by another service importing from
            // the same folder) must throw rather than create an empty file that can never be imported.
            var path = Path.Combine(_folder, DestinationHandling.FileNamePrefix + "Missing" + DestinationHandling.FileExtension);

            Assert.ThrowsExactly<FileNotFoundException>(() => DataSetSerialization.DeserializeFromFile(path));
            Assert.IsFalse(File.Exists(path));
        }
    }
}
