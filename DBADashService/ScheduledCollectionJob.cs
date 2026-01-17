using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DBADash;
using Newtonsoft.Json;
using Quartz;
using Serilog;

namespace DBADashService
{
    /// <summary>
    /// Scheduled job that enqueues collection work for all instances with a specific schedule.
    /// One job per cron schedule instead of one job per instance per schedule.
    /// </summary>
    [DisallowConcurrentExecution]
    public class ScheduledCollectionJob : IJob
    {
        private static CollectionWorkQueue _workQueue;
        private static readonly CollectionConfig _config = SchedulerServiceConfig.Config;

        private const int HIGH_THRESHOLD_SECONDS = 300;    // <= 5 minutes
        private const int NORMAL_THRESHOLD_SECONDS = 7200; // <= 2 hours

        // Memoize schedule -> priority to avoid recalculation
        private static readonly ConcurrentDictionary<string, WorkItemPriority> _priorityCache = new(StringComparer.Ordinal);

        public static void Initialize(CollectionWorkQueue workQueue)
        {
            _workQueue = workQueue;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var dataMap = context.JobDetail.JobDataMap;
            var schedule = dataMap.GetString("Schedule");
            var collectionTypes = JsonConvert.DeserializeObject<CollectionType[]>(dataMap.GetString("Types"));
            var sources = JsonConvert.DeserializeObject<List<DBADashSource>>(dataMap.GetString("Sources"));

            // Read priority from JobDataMap or cache; compute once if missing
            var priority = GetOrComputePriority(schedule, dataMap);

            Log.Information("Enqueuing collection for {instanceCount} instances on schedule {schedule} with types {types}. Queue depth: {queueDepth}. Priority: {priority}",
                sources.Count, schedule, string.Join(", ", collectionTypes.Select(t => t.ToString())), _workQueue.QueueDepth, priority);

            var previousFireTime = context.PreviousFireTimeUtc?.UtcDateTime;
            var enqueueTasks = new List<Task<bool>>();

            foreach (var source in sources)
            {
                if (OfflineInstances.IsOffline(source))
                {
                    Log.Debug("Skipping {instance} - offline",
                        source.ConnectionID ?? source.SourceConnection.ConnectionForPrint);
                    continue;
                }

                var customCollections = source.CustomCollections
                    .CombineCollections(_config.CustomCollections)
                    .Where(c => c.Value.Schedule == schedule)
                    .ToDictionary(c => c.Key, c => c.Value);

                var workItem = new WorkItem
                {
                    Source = source,
                    Types = collectionTypes,
                    CustomCollections = customCollections,
                    PreviousFireTime = previousFireTime,
                    Schedule = schedule,
                    State = _workQueue.GetState(source.SourceConnection.ConnectionString),
                    Priority = priority
                };

                enqueueTasks.Add(_workQueue.EnqueueAsync(workItem, context.CancellationToken));
            }

            var results = await Task.WhenAll(enqueueTasks);
            var failedCount = results.Count(r => !r);

            if (failedCount > 0)
            {
                Log.Warning("Failed to enqueue {failedCount} work items for schedule {schedule}", failedCount, schedule);
            }
            else
            {
                Log.Debug("Successfully enqueued {count} work items for schedule {schedule}", sources.Count, schedule);
            }
        }

        private static WorkItemPriority GetOrComputePriority(string schedule, JobDataMap map)
        {
            // Prefer stored value in JobDataMap
            if (map.ContainsKey("Priority"))
            {
                return (WorkItemPriority)map.GetInt("Priority");
            }

            // Try cache
            if (_priorityCache.TryGetValue(schedule, out var cached))
            {
                map.Put("Priority", (int)cached);
                return cached;
            }

            // Compute once
            var computed = ComputePriorityFromSchedule(schedule);

            // Persist for subsequent runs
            _priorityCache[schedule] = computed;
            map.Put("Priority", (int)computed);

            return computed;
        }

        private static WorkItemPriority ComputePriorityFromSchedule(string schedule)
        {
            // Numeric seconds schedule
            if (int.TryParse(schedule, out var seconds))
            {
                if (seconds <= HIGH_THRESHOLD_SECONDS) return WorkItemPriority.High;
                if (seconds <= NORMAL_THRESHOLD_SECONDS) return WorkItemPriority.Normal;
                return WorkItemPriority.Low;
            }

            // Cron expression: compute interval between next two occurrences
            try
            {
                var cron = new CronExpression(schedule);
                var now = DateTimeOffset.UtcNow;

                var next1 = cron.GetNextValidTimeAfter(now);
                var next2 = next1.HasValue ? cron.GetNextValidTimeAfter(next1.Value) : null;

                if (next1.HasValue && next2.HasValue)
                {
                    var intervalSeconds = (int)Math.Round((next2.Value - next1.Value).TotalSeconds);

                    if (intervalSeconds <= HIGH_THRESHOLD_SECONDS) return WorkItemPriority.High;
                    if (intervalSeconds <= NORMAL_THRESHOLD_SECONDS) return WorkItemPriority.Normal;
                    return WorkItemPriority.Low;
                }

                Log.Warning("Unable to determine interval from cron schedule {schedule}. Defaulting priority to Normal.", schedule);
                return WorkItemPriority.Normal;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Invalid cron schedule {schedule}. Defaulting priority to Normal.", schedule);
                return WorkItemPriority.Normal;
            }
        }
    }
}