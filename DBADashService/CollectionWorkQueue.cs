using AsyncKeyedLock;
using DBADash;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DBADashService
{
    /// <summary>
    /// Manages a work queue for parallel instance collection processing with configurable per-instance concurrency
    /// </summary>
    public class CollectionWorkQueue
    {
        private readonly Channel<WorkItem> _highChannel;
        private readonly Channel<WorkItem> _normalChannel;
        private readonly Channel<WorkItem> _lowChannel;
        private readonly ConcurrentDictionary<string, WorkItemState> _instanceStates;
        private readonly AsyncKeyedLocker<string> _instanceLocker;
        private readonly CollectionConfig _config;
        private readonly int _workerCount;
        private readonly int _maxConcurrentCollectionsPerInstance;
        private Task[] _workers;
        private CancellationTokenSource _cts;
        private const int DEFAULT_MAX_CONCURRENT_COLLECTIONS_PER_INSTANCE = 3;
        private int _lowInProgress;

        // Limit the number of concurrently running Low-priority items (to avoid starving higher priorities)
        private readonly int _lowMaxConcurrent;

        // De-duplication: track pending/running work keys
        private readonly ConcurrentDictionary<string, byte> _pendingKeys = new(StringComparer.Ordinal);

        public CollectionWorkQueue(CollectionConfig config, int? maxConcurrentCollectionsPerInstance = null)
        {
            _config = config;
            _workerCount = config.GetThreadCount();
            _maxConcurrentCollectionsPerInstance = maxConcurrentCollectionsPerInstance ?? DEFAULT_MAX_CONCURRENT_COLLECTIONS_PER_INSTANCE; // Default: allow up to 3 concurrent collections per instance
            _instanceStates = new ConcurrentDictionary<string, WorkItemState>();
            _instanceLocker = new AsyncKeyedLocker<string>(new AsyncKeyedLockOptions
            {
                MaxCount = _maxConcurrentCollectionsPerInstance // Semaphore-style: allow N concurrent operations per key
            });
            // Cap low-priority concurrency to 25% of workers (min 1)
            _lowMaxConcurrent = Math.Max(1, _workerCount / 4);
            // Bounded channel with backpressure - capacity based on worker count
            var capacity = Math.Max(_workerCount * 10, 100);
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };
            _highChannel = Channel.CreateBounded<WorkItem>(options);
            _normalChannel = Channel.CreateBounded<WorkItem>(options);
            _lowChannel = Channel.CreateBounded<WorkItem>(options);

            Log.Information("CollectionWorkQueue initialized with {workerCount} workers, channel capacity {capacity}, max {maxConcurrency} concurrent collections per instance",
                _workerCount, capacity, _maxConcurrentCollectionsPerInstance);
        }

        public void Start(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _workers = new Task[_workerCount];

            for (int i = 0; i < _workerCount; i++)
            {
                var workerId = i;
                _workers[i] = Task.Run(async () => await WorkerAsync(workerId, _cts.Token), _cts.Token);
            }

            Log.Information("Started {workerCount} collection workers", _workerCount);
        }

        public async Task StopAsync()
        {
            _highChannel.Writer.Complete();
            _normalChannel.Writer.Complete();
            _lowChannel.Writer.Complete();
            await Task.WhenAll(_workers);
            _cts?.Cancel();
            _cts?.Dispose();
            Log.Information("All collection workers stopped");
        }

        /// <summary>
        /// Enqueue work with priority. Returns false if channel is closed or duplicate detected.
        /// </summary>
        public async Task<bool> EnqueueAsync(WorkItem item, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!string.IsNullOrEmpty(item.DedupKey))
                {
                    if (!_pendingKeys.TryAdd(item.DedupKey, 0))
                    {
                        Log.Warning("Skipping enqueue of collection {types} on {instance} with schedule {schedule}.  The previous scheduled collection is still enqueued or in progress.",
                            item.Types, item.Source?.SourceConnection?.ConnectionForPrint, item.Schedule);
                        return false;
                    }
                }

                var writer = GetWriter(item.Priority);
                await writer.WriteAsync(item, cancellationToken);
                return true;
            }
            catch (ChannelClosedException)
            {
                Log.Warning("Attempted to enqueue work after channel was closed");
                if (!string.IsNullOrEmpty(item.DedupKey))
                {
                    _pendingKeys.TryRemove(item.DedupKey, out _);
                }
                return false;
            }
            catch
            {
                if (!string.IsNullOrEmpty(item.DedupKey))
                {
                    _pendingKeys.TryRemove(item.DedupKey, out _);
                }
                throw;
            }
        }

        public WorkItemState GetState(string connectionString)
        {
            return _instanceStates.GetOrAdd(connectionString, _ => new WorkItemState());
        }

        private ChannelWriter<WorkItem> GetWriter(WorkItemPriority priority)
        {
            return priority switch
            {
                WorkItemPriority.High => _highChannel.Writer,
                WorkItemPriority.Low => _lowChannel.Writer,
                _ => _normalChannel.Writer
            };
        }

        public int QueueDepth => _highChannel.Reader.Count + _normalChannel.Reader.Count + _lowChannel.Reader.Count;

        private async Task<WorkItem> ReadNextAsync(CancellationToken cancellationToken)
        {
            // Fast-path: drain in priority order, respecting low cap
            if (_highChannel.Reader.TryRead(out var highItem)) return highItem;
            if (_normalChannel.Reader.TryRead(out var normalItem)) return normalItem;
            if (Volatile.Read(ref _lowInProgress) < _lowMaxConcurrent && _lowChannel.Reader.TryRead(out var lowItem)) return lowItem;

            // Slow-path: wait for availability. Favor high/normal; allow low only under cap.
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await _highChannel.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_highChannel.Reader.TryRead(out highItem)) return highItem;
                }

                if (await _normalChannel.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_normalChannel.Reader.TryRead(out normalItem)) return normalItem;
                }

                if (Volatile.Read(ref _lowInProgress) < _lowMaxConcurrent)
                {
                    if (await _lowChannel.Reader.WaitToReadAsync(cancellationToken))
                    {
                        if (_lowChannel.Reader.TryRead(out lowItem)) return lowItem;
                    }
                }

                // Avoid busy-wait if low is throttled and no high/normal available
                await Task.Delay(25, cancellationToken);
            }

            throw new OperationCanceledException();
        }

        private async Task WorkerAsync(int workerId, CancellationToken cancellationToken)
        {
            Log.Debug("Worker {workerId} started", workerId);

            while (!cancellationToken.IsCancellationRequested)
            {
                WorkItem item;
                try
                {
                    item = await ReadNextAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Worker {workerId} canceled", workerId);
                    break;
                }

                var isLow = item.Priority == WorkItemPriority.Low;
                if (isLow)
                {
                    Interlocked.Increment(ref _lowInProgress);
                }

                try
                {
                    await ProcessWorkItemAsync(item, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Worker {workerId} error processing {instance}",
                        workerId, item.Source.SourceConnection.ConnectionForPrint);
                }
                finally
                {
                    if (!string.IsNullOrEmpty(item.DedupKey))
                    {
                        _pendingKeys.TryRemove(item.DedupKey, out _);
                    }

                    if (isLow)
                    {
                        Interlocked.Decrement(ref _lowInProgress);
                    }
                }
            }

            Log.Debug("Worker {workerId} stopped", workerId);
        }

        private async Task ProcessWorkItemAsync(WorkItem item, CancellationToken cancellationToken)
        {
            var connectionString = item.Source.SourceConnection.ConnectionString;

            // Allow up to N concurrent collections per instance (configured via MaxCount in AsyncKeyedLocker)
            // This maintains the current behavior where different schedules can run concurrently
            using (await _instanceLocker.LockAsync(connectionString, cancellationToken))
            {
                var state = GetState(item.DedupKey);
                item.State = state;

                await item.ExecuteAsync(_config, cancellationToken);
            }
        }
    }
}