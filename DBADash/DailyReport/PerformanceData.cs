using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace DBADash.DailyReport
{
    /// <summary>One hour of one period for one instance, from dbo.DailyReport_Performance_Get.  Null when the source has no rows for the hour.</summary>
    public class PerformanceHour
    {
        /// <summary>0: the last 24 hours.  n: the same 24 hours n weeks earlier</summary>
        public int Period { get; init; }
        /// <summary>Hours from the start of the period's window (0-23)</summary>
        public int HourOffset { get; init; }

        public double? AvgCPU { get; init; }
        public double? MaxCPU { get; init; }
        /// <summary>CPU samples at 80% or more</summary>
        public long? HighCPUSamples { get; init; }
        public long? CPUSamples { get; init; }

        public long? Reads { get; init; }
        public long? Writes { get; init; }
        public long? ReadStallMs { get; init; }
        public long? WriteStallMs { get; init; }
        public long? IOSampleMs { get; init; }

        public long? WaitMs { get; init; }
        public long? CriticalWaitMs { get; init; }
        public long? LockWaitMs { get; init; }
        public long? WaitSampleMs { get; init; }

        public int? BlockingSnapshots { get; init; }
        public int? MaxBlockedSessions { get; init; }
        public long? MaxBlockedWaitMs { get; init; }
        public int? Deadlocks { get; init; }
        public int? SlowQueries { get; init; }
        public int? FailedSlowQueries { get; init; }
    }

    /// <summary>Wait time of one wait type in one period (the 24 hours)</summary>
    public class PerformanceWaitType
    {
        public int Period { get; init; }
        public string WaitType { get; init; }
        public bool IsCriticalWait { get; init; }
        public long WaitMs { get; init; }
    }

    public class InstancePerformanceData
    {
        public int InstanceID { get; init; }
        public string Instance { get; init; }
        public bool IsAzureDB { get; init; }
        public List<PerformanceHour> Hours { get; } = new();
        public List<PerformanceWaitType> WaitTypes { get; } = new();
    }

    /// <summary>dbo.PerformanceThresholds - the thresholds the Performance Summary colours by</summary>
    public class PerformanceThresholdValues
    {
        public double CPUCritical { get; init; } = 90;
        public double CPUWarning { get; init; } = 75;
        public double ReadLatencyCritical { get; init; } = 50;
        public double ReadLatencyWarning { get; init; } = 10;
        public double WriteLatencyCritical { get; init; } = 50;
        public double WriteLatencyWarning { get; init; } = 10;
        /// <summary>Latency is ignored below this many IOPs - a few slow IOs on an idle instance aren't a problem</summary>
        public double MinIOPs { get; init; } = 100;
        public double CriticalWaitCritical { get; init; } = 1000;
        public double CriticalWaitWarning { get; init; } = 10;
    }

    public class PerformanceData
    {
        /// <summary>End of the last 24 hours (UTC, on the hour).  Period n covers [WindowEndUtc - 24h - 7n days, WindowEndUtc - 7n days)</summary>
        public DateTime WindowEndUtc { get; init; }
        public DateTime WindowStartUtc => WindowEndUtc.AddHours(-24);
        public int BaselineWeeks { get; init; }
        public List<InstancePerformanceData> Instances { get; } = new();
        public PerformanceThresholdValues Thresholds { get; set; } = new();
    }

    public static class PerformanceDataRepository
    {
        public const int DefaultBaselineWeeks = 4;

        public static PerformanceData Get(string connectionString, string instanceList, DateTime nowUtc, int baselineWeeks = DefaultBaselineWeeks)
        {
            var windowEnd = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
            var data = new PerformanceData { WindowEndUtc = windowEnd, BaselineWeeks = baselineWeeks };

            using var cn = new SqlConnection(connectionString);
            using var cmd = new SqlCommand("dbo.DailyReport_Performance_Get", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 };
            cmd.Parameters.AddWithValue("@InstanceIDs", instanceList);
            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.DateTime2) { Value = windowEnd });
            cmd.Parameters.AddWithValue("@BaselineWeeks", (byte)baselineWeeks);
            cn.Open();
            using var r = cmd.ExecuteReader();

            var byID = new Dictionary<int, InstancePerformanceData>();
            while (r.Read())
            {
                var instance = new InstancePerformanceData
                {
                    InstanceID = r.GetInt32(0),
                    Instance = r.IsDBNull(1) ? $"InstanceID {r.GetInt32(0)}" : r.GetString(1),
                    IsAzureDB = r.GetBoolean(2)
                };
                byID[instance.InstanceID] = instance;
                data.Instances.Add(instance);
            }

            r.NextResult();
            while (r.Read())
            {
                if (!byID.TryGetValue(r.GetInt32(r.GetOrdinal("InstanceID")), out var instance)) continue;
                instance.Hours.Add(new PerformanceHour
                {
                    Period = Convert.ToInt32(r["Period"]),
                    HourOffset = Convert.ToInt32(r["HourOffset"]),
                    AvgCPU = D(r, "AvgCPU"),
                    MaxCPU = D(r, "MaxCPU"),
                    HighCPUSamples = L(r, "HighCPUSamples"),
                    CPUSamples = L(r, "CPUSamples"),
                    Reads = L(r, "Reads"),
                    Writes = L(r, "Writes"),
                    ReadStallMs = L(r, "ReadStallMs"),
                    WriteStallMs = L(r, "WriteStallMs"),
                    IOSampleMs = L(r, "IOSampleMs"),
                    WaitMs = L(r, "WaitMs"),
                    CriticalWaitMs = L(r, "CriticalWaitMs"),
                    LockWaitMs = L(r, "LockWaitMs"),
                    WaitSampleMs = L(r, "WaitSampleMs"),
                    BlockingSnapshots = I(r, "BlockingSnapshots"),
                    MaxBlockedSessions = I(r, "MaxBlockedSessions"),
                    MaxBlockedWaitMs = L(r, "MaxBlockedWaitMs"),
                    Deadlocks = I(r, "Deadlocks"),
                    SlowQueries = I(r, "SlowQueries"),
                    FailedSlowQueries = I(r, "FailedSlowQueries")
                });
            }

            r.NextResult();
            while (r.Read())
            {
                if (!byID.TryGetValue(r.GetInt32(r.GetOrdinal("InstanceID")), out var instance)) continue;
                instance.WaitTypes.Add(new PerformanceWaitType
                {
                    Period = Convert.ToInt32(r["Period"]),
                    WaitType = r.GetString(r.GetOrdinal("WaitType")),
                    IsCriticalWait = r.GetBoolean(r.GetOrdinal("IsCriticalWait")),
                    WaitMs = Convert.ToInt64(r["WaitMs"])
                });
            }

            r.NextResult();
            if (r.Read())
            {
                var defaults = new PerformanceThresholdValues();
                data.Thresholds = new PerformanceThresholdValues
                {
                    CPUCritical = D(r, "CPUCriticalThreshold") ?? defaults.CPUCritical,
                    CPUWarning = D(r, "CPUWarningThreshold") ?? defaults.CPUWarning,
                    ReadLatencyCritical = D(r, "ReadLatencyCriticalThreshold") ?? defaults.ReadLatencyCritical,
                    ReadLatencyWarning = D(r, "ReadLatencyWarningThreshold") ?? defaults.ReadLatencyWarning,
                    WriteLatencyCritical = D(r, "WriteLatencyCriticalThreshold") ?? defaults.WriteLatencyCritical,
                    WriteLatencyWarning = D(r, "WriteLatencyWarningThreshold") ?? defaults.WriteLatencyWarning,
                    MinIOPs = D(r, "MinIOPsThreshold") ?? defaults.MinIOPs,
                    CriticalWaitCritical = D(r, "CriticalWaitCriticalThreshold") ?? defaults.CriticalWaitCritical,
                    CriticalWaitWarning = D(r, "CriticalWaitWarningThreshold") ?? defaults.CriticalWaitWarning
                };
            }
            return data;
        }

        private static double? D(SqlDataReader r, string column)
        {
            var v = r[column];
            return v == DBNull.Value ? null : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static long? L(SqlDataReader r, string column)
        {
            var v = r[column];
            return v == DBNull.Value ? null : Convert.ToInt64(v);
        }

        private static int? I(SqlDataReader r, string column)
        {
            var v = r[column];
            return v == DBNull.Value ? null : Convert.ToInt32(v);
        }
    }
}
