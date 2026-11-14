using DBADash.DailyReport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace DBADash.Test
{
    [TestClass]
    public class DailyReportTests
    {
        // ---- Performance analysis ----

        /// <summary>24 identical hours for one period.  Default: 20% CPU, 100 read IOPs at 2ms, no waits</summary>
        private static IEnumerable<PerformanceHour> Day(int period, double cpu = 20, long reads = 360000, long readStallMs = 360000 * 2,
            long waitMs = 0, long criticalWaitMs = 0, int deadlocks = 0) =>
            Enumerable.Range(0, 24).Select(h => new PerformanceHour
            {
                Period = period,
                HourOffset = h,
                AvgCPU = cpu,
                MaxCPU = cpu,
                HighCPUSamples = cpu >= 80 ? 60 : 0,
                CPUSamples = 60,
                Reads = reads,
                Writes = 0,
                ReadStallMs = readStallMs,
                WriteStallMs = 0,
                IOSampleMs = 3600000,
                WaitMs = waitMs,
                CriticalWaitMs = criticalWaitMs,
                LockWaitMs = 0,
                WaitSampleMs = 3600000,
                Deadlocks = h == 0 ? deadlocks : 0
            });

        private static PerformanceData Data(int baselineWeeks, params InstancePerformanceData[] instances)
        {
            var data = new PerformanceData { WindowEndUtc = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc), BaselineWeeks = baselineWeeks };
            data.Instances.AddRange(instances);
            return data;
        }

        private static int nextID;

        private static InstancePerformanceData Instance(string name, params IEnumerable<PerformanceHour>[] days)
        {
            var i = new InstancePerformanceData { InstanceID = ++nextID, Instance = name };
            foreach (var d in days) i.Hours.AddRange(d);
            return i;
        }

        [TestMethod]
        public void Performance_NormalInstancesAreCountedNotListed()
        {
            var data = Data(4, Instance("SQL1", Day(0), Day(1), Day(2), Day(3), Day(4)));
            var a = PerformanceAnalyzer.Analyze(data, true, true, 50);
            Assert.AreEqual(0, a.Flagged.Count);
            Assert.AreEqual(1, a.OkCount);
        }

        [TestMethod]
        public void Performance_BusiestHourCPUIsComparedWithThresholds()
        {
            // Quiet all day apart from one hour at 95% - the day's average is about 23%, under every threshold
            var today = Day(0, cpu: 20).Select(h => h.HourOffset == 14
                ? new PerformanceHour { Period = 0, HourOffset = 14, AvgCPU = 95, MaxCPU = 100, HighCPUSamples = 60, CPUSamples = 60 }
                : h);
            var a = PerformanceAnalyzer.Analyze(Data(0, Instance("SQL1", today)), true, false, 50);

            var f = a.Flagged.Single().Findings.Single();
            Assert.AreEqual("CPU", f.Metric);
            Assert.AreEqual(PerformanceSeverity.Critical, f.Severity);
            StringAssert.Contains(f.ValueText, "95% in the busiest hour (22:00)", "Hour 14 of a window starting 08:00");
            StringAssert.Contains(f.Context, "1h at 80%+");
        }

        [TestMethod]
        public void Performance_BaselineIsTheMedianSoOneOddWeekDoesNotCount()
        {
            // Last week had an incident (95% CPU); the usual is 20%.  Today at 22% is normal - not a big improvement
            var data = Data(4, Instance("SQL1", Day(0, cpu: 22), Day(1, cpu: 95), Day(2, cpu: 20), Day(3, cpu: 20), Day(4, cpu: 21)));
            var a = PerformanceAnalyzer.Analyze(data, true, true, 50);
            Assert.AreEqual(0, a.Flagged.Count);
            Assert.AreEqual(0, a.BetterThanUsualCount);
        }

        [TestMethod]
        public void Performance_WorseThanUsualWithinThresholds()
        {
            // 60% busiest hour is under the 75% warning threshold but triple the usual 20%
            var data = Data(2, Instance("SQL1", Day(0, cpu: 60), Day(1, cpu: 20), Day(2, cpu: 20)));
            var a = PerformanceAnalyzer.Analyze(data, true, true, 50);

            var f = a.Flagged.Single().Findings.Single(x => x.Metric == "CPU");
            Assert.AreEqual(PerformanceSeverity.WorseThanUsual, f.Severity);
            Assert.AreEqual(20, f.Baseline!.Value, 0.001);
            Assert.AreEqual("+200%", f.ChangeText);
            Assert.AreEqual(1, a.WorseThanUsualInstances);
        }

        [TestMethod]
        public void Performance_BetterThanUsualIsCountedNotListed()
        {
            var data = Data(2, Instance("SQL1", Day(0, cpu: 20), Day(1, cpu: 60), Day(2, cpu: 60)));
            var a = PerformanceAnalyzer.Analyze(data, true, true, 50);
            Assert.AreEqual(0, a.Flagged.Count);
            Assert.AreEqual(1, a.BetterThanUsualCount);
        }

        [TestMethod]
        public void Performance_SmallAbsoluteChangesAreIgnored()
        {
            // 2% -> 6% CPU is +200% but only 4 points
            var data = Data(1, Instance("SQL1", Day(0, cpu: 6), Day(1, cpu: 2)));
            Assert.AreEqual(0, PerformanceAnalyzer.Analyze(data, true, true, 50).Flagged.Count);
        }

        [TestMethod]
        public void Performance_BelowChangeThresholdIsNotReported()
        {
            // 30% -> 42% is +40%, under a 50% threshold
            var data = Data(1, Instance("SQL1", Day(0, cpu: 42), Day(1, cpu: 30)));
            Assert.AreEqual(0, PerformanceAnalyzer.Analyze(data, true, true, 50).Flagged.Count);
        }

        [TestMethod]
        public void Performance_LatencyIgnoredOnIdleInstance()
        {
            // 60ms reads is over the critical threshold, but at 10 IOPs (under the 100 minimum)
            var data = Data(1, Instance("SQL1", Day(0, reads: 36000, readStallMs: 36000 * 60), Day(1, reads: 36000, readStallMs: 36000 * 2)));
            Assert.AreEqual(0, PerformanceAnalyzer.Analyze(data, true, true, 50).Flagged.Count);
        }

        [TestMethod]
        public void Performance_LatencyOverThresholdShowsIOPsAndUsual()
        {
            var data = Data(1, Instance("SQL1", Day(0, readStallMs: 360000 * 60), Day(1)));
            var f = PerformanceAnalyzer.Analyze(data, true, true, 50).Flagged.Single().Findings.Single();
            Assert.AreEqual("ReadLatency", f.Metric);
            Assert.AreEqual(PerformanceSeverity.Critical, f.Severity);
            Assert.AreEqual("60 ms", f.ValueText);
            Assert.AreEqual("2 ms", f.BaselineText);
            Assert.AreEqual("at 100 IOPs (usual 100)", f.Context);
        }

        [TestMethod]
        public void Performance_WaitIncreaseNamesTheWaitTypesBehindIt()
        {
            var sql1 = Instance("SQL1", Day(0, waitMs: 1800000, criticalWaitMs: 1800000), Day(1, waitMs: 36000));
            sql1.WaitTypes.Add(new PerformanceWaitType { Period = 0, WaitType = "RESOURCE_SEMAPHORE", IsCriticalWait = true, WaitMs = 24 * 1800000L });
            sql1.WaitTypes.Add(new PerformanceWaitType { Period = 1, WaitType = "CXPACKET", IsCriticalWait = false, WaitMs = 24 * 36000L });
            var findings = PerformanceAnalyzer.Analyze(Data(1, sql1), true, true, 50).Flagged.Single().Findings;

            var critical = findings.Single(f => f.Metric == "CriticalWaits");
            Assert.AreEqual(PerformanceSeverity.Warning, critical.Severity, "500 ms/s is over the 10 ms/s warning, under the 1000 critical");
            Assert.AreEqual("RESOURCE_SEMAPHORE +500 ms/s", critical.Context);
            Assert.AreEqual("new", critical.ChangeText, "From 0 there's no percentage");
            Assert.AreEqual("RESOURCE_SEMAPHORE +500 ms/s", findings.Single(f => f.Metric == "Waits").Context, "CXPACKET went down so isn't named");
        }

        [TestMethod]
        public void Performance_DeadlocksOnlyReportedAsAChange()
        {
            // 5 deadlocks every day is the usual - counted in the fleet line, not listed
            var steady = Data(2, Instance("SQL1", Day(0, deadlocks: 5), Day(1, deadlocks: 5), Day(2, deadlocks: 5)));
            var a = PerformanceAnalyzer.Analyze(steady, true, true, 50);
            Assert.AreEqual(0, a.Flagged.Count);
            Assert.AreEqual(5, a.Deadlocks);
            Assert.AreEqual(1, a.DeadlockInstances);

            var spike = Data(2, Instance("SQL1", Day(0, deadlocks: 40), Day(1, deadlocks: 5), Day(2, deadlocks: 5)));
            var f = PerformanceAnalyzer.Analyze(spike, true, true, 50).Flagged.Single().Findings.Single();
            Assert.AreEqual("Deadlocks", f.Metric);
            Assert.AreEqual(PerformanceSeverity.WorseThanUsual, f.Severity);
        }

        [TestMethod]
        public void Performance_NoDataAndNoBaselineAreNamed()
        {
            var data = Data(4, Instance("Stopped", Day(1)), Instance("New", Day(0)));
            var a = PerformanceAnalyzer.Analyze(data, true, true, 50);
            CollectionAssert.AreEqual(new[] { "Stopped" }, a.NoData);
            CollectionAssert.AreEqual(new[] { "New" }, a.NoBaseline);
            Assert.AreEqual(1, a.OkCount);
        }

        [TestMethod]
        public void Performance_ThresholdsOnlyWhenComparisonOff()
        {
            // Without the comparison a tripling within thresholds isn't reported
            var data = Data(0, Instance("SQL1", Day(0, cpu: 60)));
            Assert.AreEqual(0, PerformanceAnalyzer.Analyze(data, true, false, 50).Flagged.Count);
        }

        [TestMethod]
        public void Performance_ComparisonOnlyStillLabelsSeverity()
        {
            var data = Data(1, Instance("SQL1", Day(0, cpu: 95), Day(1, cpu: 20)));
            var f = PerformanceAnalyzer.Analyze(data, false, true, 50).Flagged.Single().Findings.Single();
            Assert.AreEqual(PerformanceSeverity.Critical, f.Severity);
            Assert.IsTrue(f.IsWorseThanUsual);
        }

        [TestMethod]
        public void Performance_ComparisonOnlyDoesNotListSteadyHighCPU()
        {
            var data = Data(1, Instance("SQL1", Day(0, cpu: 95), Day(1, cpu: 95)));
            Assert.AreEqual(0, PerformanceAnalyzer.Analyze(data, false, true, 50).Flagged.Count);
        }

        [TestMethod]
        public void Performance_WorstInstancesFirst()
        {
            var data = Data(1,
                Instance("B-worse", Day(0, cpu: 60), Day(1, cpu: 20)),
                Instance("A-critical", Day(0, cpu: 95), Day(1, cpu: 95)));
            var a = PerformanceAnalyzer.Analyze(data, true, true, 50);
            CollectionAssert.AreEqual(new[] { "A-critical", "B-worse" }, a.Flagged.Select(f => f.Instance).ToArray());
        }

        [TestMethod]
        public void Median_OddEvenAndNulls()
        {
            Assert.AreEqual(2, PerformanceAnalyzer.Median(new double?[] { 3, 1, 2 }));
            Assert.AreEqual(2.5, PerformanceAnalyzer.Median(new double?[] { 1, 2, 3, 4 }));
            Assert.AreEqual(5, PerformanceAnalyzer.Median(new double?[] { null, 5 }));
            Assert.IsNull(PerformanceAnalyzer.Median(new double?[] { null }));
        }

        [TestMethod]
        public void Duration_Formats()
        {
            Assert.AreEqual("45m", PerformanceAnalyzer.Duration(45));
            Assert.AreEqual("3h 10m", PerformanceAnalyzer.Duration(190));
            Assert.AreEqual("2h", PerformanceAnalyzer.Duration(120));
        }

        // ---- Checks and rendering ----

        [TestMethod]
        public void Summary_IssuesAreCriticalAndWarningStatusColumnsOnly()
        {
            var dt = new DataTable();
            dt.Columns.Add("InstanceID", typeof(int));
            dt.Columns.Add("Instance", typeof(string));
            dt.Columns.Add("DriveStatus", typeof(int));
            dt.Columns.Add("FullBackupStatus", typeof(int));
            dt.Columns.Add("JobStatus", typeof(int));
            dt.Columns.Add("DBMailStatus", typeof(int));
            dt.Columns.Add("DBMailStatusDescription", typeof(string));
            dt.Columns.Add("LastFail", typeof(DateTime));
            dt.Rows.Add(1, "SQL1", 1, 4, 2, 3, "Database Mail not configured", DateTime.UtcNow);
            dt.Rows.Add(2, "SQL2", 4, 6, 5, 4, DBNull.Value, DateTime.UtcNow);

            var issues = DailyReportRenderer.GetIssues(dt);

            Assert.AreEqual(2, issues.Count, "Critical drive and warning job on SQL1 only; OK, NA, acknowledged and warning-low are not issues");
            var drive = issues.Single(i => i.Check == "Drive");
            Assert.IsTrue(drive.IsCritical);
            Assert.AreEqual("SQL1", drive.Instance);
            Assert.IsFalse(issues.Single(i => i.Check == "Job").IsCritical);
        }

        [TestMethod]
        public void FriendlyName_SplitsCamelCaseAndDropsStatus()
        {
            Assert.AreEqual("Log Free Space", DailyReportRenderer.FriendlyName("LogFreeSpaceStatus"));
            Assert.AreEqual("AG", DailyReportRenderer.FriendlyName("AGStatus"));
            Assert.AreEqual("Last Good Check DB", DailyReportRenderer.FriendlyName("LastGoodCheckDBStatus"));
        }

        [TestMethod]
        public void Subject_SummarisesIssuesAndChanges()
        {
            Assert.AreEqual("[DBA Dash] Morning: all OK", DailyReportRenderer.BuildSubject("Morning", 0, 0, 0));
            Assert.AreEqual("[DBA Dash] Morning: 2 critical, 1 warning, performance issues on 3 instances", DailyReportRenderer.BuildSubject("Morning", 2, 1, 3));
            Assert.AreEqual("[DBA Dash] Morning: checks OK, performance issues on 1 instance", DailyReportRenderer.BuildSubject("Morning", 0, 0, 1));
        }

        private static DailyReportIssue Obj(string group, int status, string instance, string name, string detail = "x") =>
            new() { CheckGroup = group, Status = status, Instance = instance, ObjectName = name, Detail = detail };

        [TestMethod]
        public void ObjectSection_NamesCountAndServers()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var issues = new[]
            {
                Obj("Backups - Full", 1, "LAB2022", "DB1", "no full backup recorded"),
                Obj("Backups - Full", 1, "LAB2022", "DB2"),
                Obj("Backups - Full", 1, "LAB2019Node1", "DB3")
            };
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), issues, null, "UTC");

            Assert.IsTrue(content.TextBody.Contains("3 databases across 2 servers reporting critical:"), content.TextBody);
            Assert.IsTrue(content.TextBody.Contains("Servers: LAB2022 (2), LAB2019Node1 (1)"), "Servers listed with their database counts, most first");
            Assert.IsTrue(content.TextBody.Contains("DB1 on LAB2022 - no full backup recorded"));
            Assert.IsTrue(content.HtmlBody.Contains("Backups - Full"));
            Assert.IsFalse(content.TextBody.Contains("Backups - Log"), "Empty sections are left out");
        }

        [TestMethod]
        public void ObjectSection_CapsItemsAndSaysHowManyMore()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var issues = Enumerable.Range(1, DailyReportRenderer.MaxItemsPerSection + 5)
                .Select(i => Obj("Backups - Full", 2, "LAB2022", $"DB{i:000}"))
                .ToList();
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), issues, null, "UTC");

            Assert.AreEqual(DailyReportRenderer.MaxItemsPerSection, content.TextBody.Split('\n').Count(l => l.Contains(" on LAB2022")));
            Assert.IsTrue(content.TextBody.Contains("And 5 others."));
            Assert.AreEqual(0, content.CriticalCount);
            Assert.AreEqual(DailyReportRenderer.MaxItemsPerSection + 5, content.WarningCount);
        }

        [TestMethod]
        public void OtherChecks_GroupedByCheckWithInstancesCommaSeparated()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var summary = new DataTable();
            summary.Columns.Add("InstanceDisplayName", typeof(string));
            summary.Columns.Add("AGStatus", typeof(int));
            summary.Rows.Add("LAB2022", 1);
            summary.Rows.Add("LAB2005", 1);
            summary.Rows.Add("LAB2019", 2);
            var text = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", summary, Array.Empty<DailyReportIssue>(), null, "UTC").TextBody;

            Assert.IsTrue(text.Contains("AG - critical on LAB2005, LAB2022; warning on LAB2019"), text);
        }

        [TestMethod]
        public void ObjectSection_ServerFlaggedWithoutDatabaseDetailIsNamed()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var summary = new DataTable();
            summary.Columns.Add("InstanceDisplayName", typeof(string));
            summary.Columns.Add("FullBackupStatus", typeof(int));
            summary.Rows.Add("LOCALDB#86CB7214", 1);
            var text = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", summary, Array.Empty<DailyReportIssue>(), null, "UTC").TextBody;

            Assert.IsTrue(text.Contains("1 server flagged with no database detail: LOCALDB#86CB7214"), text);
            Assert.IsFalse(text.Contains("instance level"), "No placeholder wording");
        }

        [TestMethod]
        public void SummaryRow_WithNoNameUsesInstanceIdNotBlank()
        {
            var summary = new DataTable();
            summary.Columns.Add("InstanceID", typeof(int));
            summary.Columns.Add("Instance", typeof(string));
            summary.Columns.Add("AGStatus", typeof(int));
            summary.Rows.Add(7, DBNull.Value, 1);

            var issue = DailyReportRenderer.GetIssues(summary).Single();
            Assert.AreEqual("InstanceID 7", issue.Instance);
        }

        [TestMethod]
        public void ObjectSection_CriticalItemsListedBeforeWarnings()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var issues = new[] { Obj("Drive space", 2, "A", "C:"), Obj("Drive space", 1, "B", "D:") };
            var text = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), issues, null, "UTC").TextBody;

            Assert.IsTrue(text.IndexOf("[Critical] D: on B", StringComparison.Ordinal) < text.IndexOf("[Warning] C: on A", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Render_EncodesNamesAndIncludesPerformance()
        {
            var config = new DailyReportConfig
            {
                ReportName = "Ops <Daily>",
                IncludePerformance = true,
                IncludeWeekOverWeek = true,
                ChangeThresholdPct = 50,
                TimeZone = "UTC"
            };
            var summary = new DataTable();
            summary.Columns.Add("Instance", typeof(string));
            summary.Columns.Add("DriveStatus", typeof(int));
            summary.Rows.Add("SQL<1>", 1);

            var perf = PerformanceAnalyzer.Analyze(Data(1, Instance("SQL<1>", Day(0, cpu: 95), Day(1, cpu: 30)), Instance("Quiet", Day(0), Day(1))), true, true, 50);
            var content = DailyReportRenderer.Render(config, new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc), "{All}", summary,
                Array.Empty<DailyReportIssue>(), perf, "UTC");

            Assert.IsTrue(content.HtmlBody.Contains("Ops &lt;Daily&gt;"), "Report name must be HTML encoded");
            Assert.IsTrue(content.HtmlBody.Contains("SQL&lt;1&gt;"));
            Assert.IsFalse(content.HtmlBody.Contains("SQL<1>"));
            Assert.IsTrue(content.HtmlBody.Contains("Performance (last 24 hours)"));
            Assert.IsTrue(content.TextBody.Contains("Compared with the median of the same 24 hours in the previous 1 week."), content.TextBody);
            Assert.IsTrue(content.TextBody.Contains("1 critical. 1 other instance within thresholds and usual levels."), content.TextBody);
            Assert.IsTrue(content.TextBody.Contains("SQL<1> [Critical]"), content.TextBody);
            Assert.IsTrue(content.TextBody.Contains("  - CPU: 95% in the busiest hour (08:00); 24h at 80%+, day average 95% - usual 30% (+217%)"), content.TextBody);
            Assert.IsFalse(content.TextBody.Contains("Quiet"), "Instances with nothing to report aren't listed");
            Assert.IsTrue(content.TextBody.Contains("DRIVE SPACE"), "A drive flagged on the summary with no object rows is still reported");
            Assert.AreEqual(1, content.CriticalCount, "Performance isn't counted in the checks");
            Assert.IsTrue(content.Subject.EndsWith("performance issues on 1 instance"), content.Subject);
        }

        [TestMethod]
        public void Render_OmitsPerformanceWhenNotBuilt()
        {
            var config = new DailyReportConfig { ReportName = "Basic", TimeZone = "UTC" };
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), Array.Empty<DailyReportIssue>(), null, "UTC");

            Assert.IsFalse(content.HtmlBody.Contains("Performance (last 24 hours)"));
            Assert.AreEqual(0, content.InstanceCount);
        }

        [TestMethod]
        public void Render_PerformanceNamesInstancesWithNoData()
        {
            var config = new DailyReportConfig { ReportName = "Ops", IncludePerformance = true, TimeZone = "UTC" };
            var perf = PerformanceAnalyzer.Analyze(Data(0, Instance("Gone")), true, false, 50);
            var text = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), Array.Empty<DailyReportIssue>(), perf, "UTC").TextBody;

            Assert.IsTrue(text.Contains("No performance data in the last 24 hours: Gone"), text);
            Assert.IsFalse(text.Contains("Compared with the median"), "No comparison note when the comparison is off");
        }

        // ---- Per-report settings ----

        [TestMethod]
        public void Settings_RoundTripAndDefaults()
        {
            var s = new DailyReportSettings { BaselineWeeks = 6, IncludeAISummary = true, IncludeCharts = false };
            s.MetricSettings["Deadlocks"] = new PerformanceMetricSetting { Enabled = false, MinChange = 7 };

            var back = DailyReportSettings.FromJson(s.ToJson());
            Assert.AreEqual(6, back.BaselineWeeks);
            Assert.IsTrue(back.IncludeAISummary);
            Assert.IsFalse(back.IncludeCharts);
            Assert.IsFalse(back.IsEnabled("deadlocks"), "Keys ignore case");
            Assert.AreEqual(7, back.MinChange("Deadlocks"));
            Assert.AreEqual(10, back.MinChange("CPU"), "A measure not in the JSON takes its default");
        }

        [TestMethod]
        public void Settings_BadOrMissingJsonGivesDefaults()
        {
            Assert.AreEqual(4, DailyReportSettings.FromJson(null).BaselineWeeks);
            Assert.AreEqual(4, DailyReportSettings.FromJson("not json").BaselineWeeks);
            Assert.IsTrue(DailyReportSettings.FromJson("{}").IncludeCharts);
            Assert.AreEqual(DailyReportSettings.MaxBaselineWeeks, DailyReportSettings.FromJson("{\"BaselineWeeks\":99}").BaselineWeeks, "Clamped");
            Assert.AreEqual(0, DailyReportSettings.FromJson("{\"MetricSettings\":{\"Nope\":{\"Enabled\":false}}}").MetricSettings.Count, "Unknown measures are dropped");
        }

        [TestMethod]
        public void Settings_ToJsonDoesNotChangeTheInstance()
        {
            var s = new DailyReportSettings { BaselineWeeks = 99 };
            var dictionary = s.MetricSettings;
            s.ToJson();
            Assert.AreEqual(99, s.BaselineWeeks, "Clamped on read, not on write");
            Assert.AreSame(dictionary, s.MetricSettings);
            Assert.AreEqual(DailyReportSettings.MaxBaselineWeeks, s.Clone().BaselineWeeks);
        }

        [TestMethod]
        public void Settings_DisabledMeasureIsNotReported()
        {
            var settings = new DailyReportSettings();
            settings.MetricSettings["CPU"] = new PerformanceMetricSetting { Enabled = false, MinChange = 10 };
            var data = Data(1, Instance("SQL1", Day(0, cpu: 95), Day(1, cpu: 20)));
            var findings = PerformanceAnalyzer.Analyze(data, true, true, 50, settings).Flagged.SingleOrDefault()?.Findings;
            Assert.IsFalse(findings?.Any(f => f.Metric == "CPU") ?? false);
        }

        [TestMethod]
        public void Settings_MinChangeOverridesDefault()
        {
            // 20% -> 28% CPU is +40% of usual and 8 points: under the default 10 point minimum, over a minimum of 5
            var data = Data(1, Instance("SQL1", Day(0, cpu: 28), Day(1, cpu: 20)));
            Assert.AreEqual(0, PerformanceAnalyzer.Analyze(data, true, true, 30).Flagged.Count);
            var settings = new DailyReportSettings();
            settings.MetricSettings["CPU"] = new PerformanceMetricSetting { Enabled = true, MinChange = 5 };
            Assert.AreEqual(1, PerformanceAnalyzer.Analyze(data, true, true, 30, settings).Flagged.Count);
        }

        // ---- New, ongoing and resolved ----

        private static IssueHistory History(DateTime runUtc, Dictionary<string, DateTime> firstSeen, params ResolvedIssue[] resolved) =>
            new() { RunUtc = runUtc, IsFirstRun = false, PreviousRunUtc = runUtc.AddDays(-1), FirstSeen = new Dictionary<string, DateTime>(firstSeen, StringComparer.OrdinalIgnoreCase), Resolved = resolved.ToList() };

        [TestMethod]
        public void History_NewAndOngoing()
        {
            var run = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
            var key = IssueHistory.ObjectKey("Drive space", "SQL1", "C:");
            var h = History(run, new Dictionary<string, DateTime> { [key] = run.AddDays(-3) });

            Assert.IsFalse(h.IsNew(key));
            Assert.AreEqual(3, h.DaysOngoing(key));
            Assert.AreEqual("for 3 days", h.AgeText(key));
            Assert.IsTrue(h.IsNew(IssueHistory.ObjectKey("Drive space", "SQL1", "D:")));
            Assert.AreEqual("new", h.AgeText(IssueHistory.ObjectKey("Drive space", "SQL1", "D:")));

            var first = IssueHistory.None(run);
            Assert.IsFalse(first.IsNew(key), "Nothing is new on the first run");
            Assert.IsNull(first.AgeText(key));
        }

        [TestMethod]
        public void History_LongKeysAreTrimmedConsistently()
        {
            var key = IssueHistory.ObjectKey("Agent jobs", "SQL1", new string('x', 600));
            Assert.AreEqual(IssueHistory.MaxKeyLength, key.Length);
        }

        [TestMethod]
        public void Render_MarksNewFirstAndListsResolved()
        {
            var run = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var issues = new[] { Obj("Drive space", 1, "SQL1", "C:"), Obj("Drive space", 1, "SQL1", "D:") };
            var history = History(run,
                new Dictionary<string, DateTime> { [IssueHistory.ObjectKey("Drive space", "SQL1", "C:")] = run.AddDays(-5) },
                new ResolvedIssue("x", "Backups - Full: DB9 on SQL2", run.AddDays(-2)));

            var content = DailyReportRenderer.Render(config, run, "{All}", new DataTable(), issues, null, "UTC", new DailyReportExtras { History = history });
            var text = content.TextBody;

            Assert.IsTrue(text.Contains("1 new since the last report · 1 resolved"), text);
            Assert.IsTrue(text.Contains("2 drives across 1 server reporting critical (1 new):"), text);
            Assert.IsTrue(text.IndexOf("[Critical] [New] D: on SQL1", StringComparison.Ordinal) < text.IndexOf("[Critical] C: on SQL1", StringComparison.Ordinal), "New first");
            Assert.IsTrue(text.Contains("C: on SQL1 - x (for 5 days)"), text);
            Assert.IsTrue(text.Contains("RESOLVED SINCE THE LAST REPORT"));
            Assert.IsTrue(text.Contains("Backups - Full: DB9 on SQL2 (had been reported for 2 days)"), text);
            Assert.AreEqual(1, content.NewCount);
            Assert.IsTrue(content.Subject.EndsWith(", 1 new"), content.Subject);
        }

        [TestMethod]
        public void Render_FirstRunSaysWhenNewStarts()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var text = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), new[] { Obj("Drive space", 1, "SQL1", "C:") }, null, "UTC",
                new DailyReportExtras { History = IssueHistory.None(DateTime.UtcNow) }).TextBody;
            Assert.IsTrue(text.Contains("Issues are marked as new or ongoing from the next scheduled report."), text);
            Assert.IsFalse(text.Contains("[New]"));
        }

        [TestMethod]
        public void IssueKeys_CoverEverySection()
        {
            var summary = new DataTable();
            summary.Columns.Add("InstanceDisplayName", typeof(string));
            summary.Columns.Add("AGStatus", typeof(int));
            summary.Columns.Add("FullBackupStatus", typeof(int));
            summary.Rows.Add("SQL1", 1, 1);
            var perf = PerformanceAnalyzer.Analyze(Data(0, Instance("SQL1", Day(0, cpu: 95))), true, false, 50);
            var keys = DailyReportRenderer.IssueKeys(DailyReportRenderer.Classify(summary, new[] { Obj("Drive space", 1, "SQL1", "C:") }), perf).Select(k => k.Key).ToList();

            CollectionAssert.Contains(keys, IssueHistory.ObjectKey("Drive space", "SQL1", "C:"));
            CollectionAssert.Contains(keys, IssueHistory.ObjectKey("Backups - Full", "SQL1", ""), "Flagged on the summary with no database rows");
            CollectionAssert.Contains(keys, IssueHistory.CheckKey("AGStatus", "SQL1"));
            CollectionAssert.Contains(keys, IssueHistory.PerformanceKey("CPU", "SQL1"));
        }

        // ---- Queries behind a regression ----

        private static TopQueryRow Q(string source, long id, int period, double cpu, long reads = 0, long execs = 100, string name = "dbo.p") =>
            new() { Source = source, InstanceID = 1, ID = id, Period = period, DatabaseName = "Sales", Name = name, QueryHash = "0x01", CPUMs = cpu, LogicalReads = reads, Executions = execs };

        [TestMethod]
        public void TopQueries_WeekWithNoCandidatesCountsAsZero()
        {
            // 100 in week 1; weeks 2-4 had data for the instance but none of today's candidates ran
            var rows = new List<TopQueryRow> { Q("Object", 1, 0, 500), Q("Object", 1, 1, 100) };
            var top = TopQueryAnalyzer.Rank(rows, TopQueryAnalyzer.MeasureCPU, byIncrease: true, count: 1, baselineCoverage: new[] { 1, 2, 3, 4 });
            Assert.AreEqual(0, top[0].Usual!.Value, 0.001, "Median of 100, 0, 0, 0");

            var withoutCoverage = TopQueryAnalyzer.Rank(rows, TopQueryAnalyzer.MeasureCPU, byIncrease: true, count: 1);
            Assert.AreEqual(100, withoutCoverage[0].Usual!.Value, 0.001);
        }

        [TestMethod]
        public void Channel_DisableWindow()
        {
            var now = new DateTime(2026, 11, 13, 10, 0, 0);
            Assert.IsFalse(DailyReportRunner.IsInDisableWindow(null, null, now));
            Assert.IsTrue(DailyReportRunner.IsInDisableWindow(now.AddHours(-1), null, now), "Disabled indefinitely");
            Assert.IsTrue(DailyReportRunner.IsInDisableWindow(now.AddHours(-1), now.AddHours(1), now));
            Assert.IsFalse(DailyReportRunner.IsInDisableWindow(now.AddHours(1), null, now), "Not started");
            Assert.IsFalse(DailyReportRunner.IsInDisableWindow(now.AddHours(-2), now.AddHours(-1), now), "Ended");
        }

        [TestMethod]
        public void Settings_NonFiniteMinChangeTakesTheDefault()
        {
            var settings = new DailyReportSettings();
            settings.MetricSettings["CPU"] = new PerformanceMetricSetting { Enabled = true, MinChange = double.NaN };
            settings.MetricSettings["Deadlocks"] = new PerformanceMetricSetting { Enabled = true, MinChange = double.PositiveInfinity };
            var copy = settings.Clone(); // serializes - would throw on NaN
            Assert.AreEqual(10, copy.MetricSettings["CPU"].MinChange);
            Assert.AreEqual(3, copy.MetricSettings["Deadlocks"].MinChange);
            Assert.IsTrue(double.IsNaN(settings.MetricSettings["CPU"].MinChange), "ToJson doesn't change the instance");
        }

        [TestMethod]
        public void Performance_NoChangeIsNeverSignificant()
        {
            Assert.IsFalse(PerformanceAnalyzer.IsSignificant(0, 0, 0, 50));
            Assert.IsTrue(PerformanceAnalyzer.IsSignificant(1, 0, 0, 50));
        }

        [TestMethod]
        public void TopQueries_RankedByIncreaseOnUsual()
        {
            var rows = new List<TopQueryRow>
            {
                // Big but steady
                Q("Object", 1, 0, 900_000, name: "dbo.Steady"), Q("Object", 1, 1, 880_000, name: "dbo.Steady"), Q("Object", 1, 2, 910_000, name: "dbo.Steady"),
                // Smaller, but up a lot
                Q("Object", 2, 0, 600_000, name: "dbo.Regressed"), Q("Object", 2, 1, 50_000, name: "dbo.Regressed"), Q("Object", 2, 2, 60_000, name: "dbo.Regressed"),
                // New this week, ad hoc
                Q("Query", 3, 0, 300_000, name: "SELECT * FROM Orders WHERE Id = ?")
            };
            var top = TopQueryAnalyzer.Rank(rows, TopQueryAnalyzer.MeasureCPU, byIncrease: true, count: 2);

            CollectionAssert.AreEqual(new[] { "dbo.Regressed", "SELECT * FROM Orders WHERE Id = ?" }, top.Select(q => q.Name).ToArray());
            Assert.AreEqual(55_000, top[0].Usual!.Value, 0.001);
            Assert.AreEqual(0, top[1].Usual!.Value, "Missing from the baseline weeks = used nothing then");
            Assert.AreEqual("ad hoc query 0x01 in Sales", top[1].MetadataName, "No statement text for the AI");
            Assert.AreEqual("Sales.dbo.Regressed", top[0].DisplayName);
            Assert.AreEqual("CPU 10m (usual 55s), 100 executions (usual 100)", TopQueryAnalyzer.Describe(top[0], TopQueryAnalyzer.MeasureCPU));
        }

        [TestMethod]
        public void TopQueries_MeasureFollowsTheFindings()
        {
            var io = new InstancePerformanceResult();
            io.Findings.Add(new PerformanceFinding { Metric = "ReadLatency" });
            Assert.AreEqual(TopQueryAnalyzer.MeasureReads, TopQueryAnalyzer.MeasureFor(io));
            io.Findings.Add(new PerformanceFinding { Metric = "CPU" });
            Assert.AreEqual(TopQueryAnalyzer.MeasureCPU, TopQueryAnalyzer.MeasureFor(io));
        }

        [TestMethod]
        public void TopQueries_ListedUnderTheInstanceWithAiSafeNames()
        {
            var config = new DailyReportConfig { ReportName = "Ops", IncludePerformance = true, IncludeWeekOverWeek = true, TimeZone = "UTC" };
            config.Settings.IncludeCharts = false;
            var perf = PerformanceAnalyzer.Analyze(Data(1, Instance("SQL1", Day(0, cpu: 95), Day(1, cpu: 20))), true, true, 50);
            var sqlID = perf.Flagged.Single().InstanceID;
            TopQueryAnalyzer.Apply(perf, new[] { new TopQueryRow { Source = "Query", InstanceID = sqlID, ID = 9, Period = 0, DatabaseName = "Sales", Name = "SELECT secret = 'x'", QueryHash = "0xAB", CPUMs = 120_000, Executions = 5 } }, 3);

            var email = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC").TextBody;
            var ai = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC", new DailyReportExtras { ForAi = true }).TextBody;

            Assert.IsTrue(email.Contains("Behind it (CPU, most above usual):"), email);
            Assert.IsTrue(email.Contains("SELECT secret = 'x' in Sales - CPU 2m"), email);
            Assert.IsFalse(ai.Contains("secret"), "Statement text never goes to the AI");
            Assert.IsTrue(ai.Contains("ad hoc query 0xAB in Sales"), ai);
        }

        // ---- Charts ----

        [TestMethod]
        public void Chart_RendersPngWithBaselineAndThreshold()
        {
            var perf = PerformanceAnalyzer.Analyze(Data(2, Instance("SQL1", Day(0, cpu: 95), Day(1, cpu: 20), Day(2, cpu: 30))), true, true, 50);
            var instance = perf.Flagged.Single();
            var series = PerformanceChartSeries.From(instance, instance.Findings.First(), new PerformanceThresholdValues(), 2, h => $"{h:00}:00");

            Assert.IsTrue(series.HasBaseline);
            Assert.AreEqual(20, series.UsualMin[5]!.Value, 0.001);
            Assert.AreEqual(30, series.UsualMax[5]!.Value, 0.001);
            Assert.AreEqual(75, series.Threshold);

            var png = PerformanceChart.RenderPng(series);
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray(), "PNG signature");
        }

        [TestMethod]
        public void Chart_EmbeddedAsCidAndInlinedForPreview()
        {
            var config = new DailyReportConfig { ReportName = "Ops", IncludePerformance = true, TimeZone = "UTC" };
            var perf = PerformanceAnalyzer.Analyze(Data(0, Instance("SQL1", Day(0, cpu: 95))), true, false, 50);
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC");

            var image = content.Images.Single();
            Assert.IsTrue(content.HtmlBody.Contains($"src=\"cid:{image.ContentId}\""));
            Assert.IsTrue(content.HtmlWithInlineImages().Contains("src=\"data:image/png;base64,"));
            Assert.AreEqual(0, DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC", new DailyReportExtras { ForAi = true }).Images.Count);
        }

        [TestMethod]
        public void Chart_NiceMax()
        {
            Assert.AreEqual(100, PerformanceChart.NiceMax(95));
            Assert.AreEqual(20, PerformanceChart.NiceMax(13));
            Assert.AreEqual(0.5, PerformanceChart.NiceMax(0.42), 1e-9);
            Assert.AreEqual(1, PerformanceChart.NiceMax(0));
        }

        [TestMethod]
        public void TopQueries_NewThisWeekSaysNew()
        {
            var q = new TopQuery { Source = "Object", Name = "dbo.p", DatabaseName = "Sales", Current = 120_000, Usual = 0, Executions = 10, UsualExecutions = 0 };
            Assert.AreEqual("CPU 2m (new), 10 executions", TopQueryAnalyzer.Describe(q, TopQueryAnalyzer.MeasureCPU));
        }

        // ---- Layout and plain text ----

        [TestMethod]
        public void Html_HasTilesHeaderAndMobileStyle()
        {
            var run = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", IncludePerformance = true };
            var perf = PerformanceAnalyzer.Analyze(Data(0, Instance("SQL1", Day(0, cpu: 95))), true, false, 50);
            var history = History(run, new Dictionary<string, DateTime>());
            var html = DailyReportRenderer.Render(config, run, "{All}", new DataTable(), new[] { Obj("Drive space", 1, "SQL1", "C:") }, perf, "UTC",
                new DailyReportExtras { History = history }).HtmlBody;

            StringAssert.Contains(html, "@media only screen and (max-width:620px)");
            StringAssert.Contains(html, ">Critical</div>", "Tile label");
            StringAssert.Contains(html, ">Resolved<");
            StringAssert.Contains(html, ">Performance<");
            StringAssert.Contains(html, "class=\"dr-row\"");
            StringAssert.Contains(html, "1 drive &#183; 1 new", "Count pill on the section heading (the dot is encoded)");
        }

        [TestMethod]
        public void Html_IsAWholeDocumentWithTheStyleInTheHead()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, null, "UTC");
            var html = content.HtmlBody;

            StringAssert.StartsWith(html, "<!DOCTYPE html>");
            Assert.IsTrue(html.IndexOf("<style", StringComparison.Ordinal) < html.IndexOf("</head>", StringComparison.Ordinal), "Gmail drops a style block that isn't in the head");
            StringAssert.Contains(html, "body,table,td,div,p,span{font-family:", "Outlook doesn't inherit the font into nested tables");
            StringAssert.Contains(html, "<meta name=\"color-scheme\" content=\"light\">");
            StringAssert.EndsWith(html, "</body></html>");

            var preview = content.PreviewHtml("<div>BANNER</div>");
            Assert.IsTrue(preview.IndexOf("<body", StringComparison.Ordinal) < preview.IndexOf("BANNER", StringComparison.Ordinal), "Banner inside the body, at the top");
            Assert.AreEqual(1, preview.Split("<html").Length - 1, "One document, not one inside another");
        }

        [TestMethod]
        public void Html_CardAndTileBordersAreOnCells()
        {
            // Outlook drops borders and margins on a table, so the coloured edges must be on cells
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", IncludePerformance = true };
            var perf = PerformanceAnalyzer.Analyze(Data(0, Instance("SQL1", Day(0, cpu: 95))), true, false, 50);
            var html = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC").HtmlBody;
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(html, "<table[^>]*style=\"[^\"]*border(-(left|right|top|bottom))?:"), "No table has a border style");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(html, "<table[^>]*style=\"[^\"]*margin"), "No table has a margin");
        }

        [TestMethod]
        public void PlainText_NoChartsAndPreviewShowsText()
        {
            var config = new DailyReportConfig { ReportName = "Ops <Plain>", TimeZone = "UTC", IncludePerformance = true };
            config.Settings.PlainTextOnly = true;
            var perf = PerformanceAnalyzer.Analyze(Data(0, Instance("SQL1", Day(0, cpu: 95))), true, false, 50);
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC");

            Assert.IsTrue(content.PlainTextOnly);
            Assert.AreEqual(0, content.Images.Count, "No charts are drawn for a plain text report");
            var preview = content.PreviewHtml();
            StringAssert.Contains(preview, "<pre");
            StringAssert.StartsWith(preview, "<!DOCTYPE html>", "A page of its own");
            StringAssert.Contains(preview, "Ops &lt;Plain&gt;", "The text is encoded in the preview page");
            StringAssert.Contains(content.TextBody, "PERFORMANCE (LAST 24 HOURS)");

            config.Settings.PlainTextOnly = false;
            var html = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, perf, "UTC");
            Assert.AreEqual(1, html.Images.Count);
            StringAssert.Contains(html.PreviewHtml(), "data:image/png;base64,");
        }

        [TestMethod]
        public void DisplayNames_AzureServerRowsHaveNoInstanceID()
        {
            // Summary_Get returns Azure SQL DB as one row per logical server with a NULL InstanceID - this threw "Object cannot be cast from DBNull"
            var summary = new DataTable();
            summary.Columns.Add("InstanceID", typeof(int));
            summary.Columns.Add("Instance", typeof(string));
            summary.Columns.Add("InstanceGroupName", typeof(string));
            summary.Columns.Add("AGStatus", typeof(int));
            summary.Rows.Add(1, "SQL1", "SQL1", 4);
            summary.Rows.Add(DBNull.Value, "azsrv.database.windows.net", "azsrv", 1);
            summary.Rows.Add(2, "SQL2", "SQL2", 4);

            DailyReportRunner.ApplyDisplayNames(summary, new Dictionary<int, string> { [1] = "Prod 1" });

            Assert.AreEqual("Prod 1", summary.Rows[0]["InstanceDisplayName"]);
            Assert.AreEqual("azsrv", summary.Rows[1]["InstanceDisplayName"]);
            Assert.AreEqual(DBNull.Value, summary.Rows[2]["InstanceDisplayName"], "No display name: the report falls back to Instance");
            Assert.AreEqual("azsrv", DailyReportRenderer.GetIssues(summary).Single().Instance, "The Azure server's issue is named by the server");
        }

        [TestMethod]
        public void SendResult_SaysWhatEachChannelDid()
        {
            var result = new DailyReportRunner.SendResult();
            Assert.IsFalse(result.SentToAny);
            Assert.AreEqual("The report has no notification channels.", result.ToString());

            result.Channels.Add(new DailyReportRunner.ChannelSendResult { ChannelName = "Ops", Recipient = "ops@example.com", Sent = true, Detail = "2.0.0 OK queued as 1A2B " });
            result.Channels.Add(new DailyReportRunner.ChannelSendResult { ChannelName = "DBAs", Detail = "the channel is in its disable window (Disable From / Disable To)" });

            Assert.IsTrue(result.SentToAny);
            Assert.IsFalse(result.SentToAll);
            Assert.AreEqual("DBAs", result.Failures.Single().ChannelName);
            Assert.AreEqual(
                "Ops: accepted by the mail server for ops@example.com (2.0.0 OK queued as 1A2B)" + Environment.NewLine +
                "DBAs: not sent - the channel is in its disable window (Disable From / Disable To)",
                result.ToString());
        }

        // ---- AI summary ----

        [TestMethod]
        public void AiSummary_ShownWhenOnAndFailureNoted()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            config.Settings.IncludeAISummary = true;
            var ok = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, null, "UTC",
                new DailyReportExtras { AiSummary = new DailyReportAiSummary { Summary = "Drive C: on SQL1 needs <attention>.", Model = "m1" } });
            Assert.IsTrue(ok.TextBody.Contains("AI SUMMARY (M1)"), ok.TextBody);
            Assert.IsTrue(ok.HtmlBody.Contains("needs &lt;attention&gt;."), "Encoded");

            var failed = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, null, "UTC",
                new DailyReportExtras { AiSummary = new DailyReportAiSummary { Error = "service down" } }).TextBody;
            Assert.IsTrue(failed.Contains("AI summary not available: service down"), failed);

            config.Settings.IncludeAISummary = false;
            var off = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, null, "UTC",
                new DailyReportExtras { AiSummary = new DailyReportAiSummary { Summary = "x" } }).TextBody;
            Assert.IsFalse(off.Contains("AI SUMMARY"));
        }

        // ---- Schedule ----

        [TestMethod]
        public void Schedule_DayBitsStartOnMonday()
        {
            Assert.AreEqual(1, DailyReportConfig.DayBit(DayOfWeek.Monday));
            Assert.AreEqual(16, DailyReportConfig.DayBit(DayOfWeek.Friday));
            Assert.AreEqual(64, DailyReportConfig.DayBit(DayOfWeek.Sunday));
        }

        [TestMethod]
        public void Schedule_Described()
        {
            Assert.AreEqual("08:00, every day", new DailyReportConfig().ScheduleDescription);
            Assert.AreEqual("07:30, weekdays", new DailyReportConfig { ScheduleTime = new TimeSpan(7, 30, 0), DaysOfWeek = DailyReportConfig.Weekdays }.ScheduleDescription);
            Assert.AreEqual("Every hour 08:00-18:00, Mon, Wed, Fri", new DailyReportConfig { IntervalMinutes = 60, WindowEndTime = new TimeSpan(18, 0, 0), DaysOfWeek = 1 | 4 | 16 }.ScheduleDescription);
            Assert.AreEqual("Every 30 minutes from 08:00, weekends", new DailyReportConfig { IntervalMinutes = 30, DaysOfWeek = DailyReportConfig.Weekends }.ScheduleDescription);
            Assert.AreEqual("Every 2 hours", DailyReportConfig.IntervalText(120));
        }

        // ---- Changes report ----

        private static DailyReportIssue Backup(string db, int status) =>
            new() { CheckGroup = "Backups - Full", Status = status, Instance = "SQL1", ObjectName = db, Detail = "no full backup" };

        private static IssueHistory ChangesHistory(params (string Db, int Status)[] previous) => new()
        {
            RunUtc = new DateTime(2026, 11, 13, 10, 0, 0),
            IsFirstRun = false,
            PreviousRunUtc = new DateTime(2026, 11, 13, 9, 0, 0),
            FirstSeen = previous.ToDictionary(p => IssueHistory.ObjectKey("Backups - Full", "SQL1", p.Db), _ => new DateTime(2026, 11, 12), StringComparer.OrdinalIgnoreCase),
            PreviousStatus = previous.ToDictionary(p => IssueHistory.ObjectKey("Backups - Full", "SQL1", p.Db), p => p.Status, StringComparer.OrdinalIgnoreCase),
            Resolved = new List<ResolvedIssue> { new("obj|Drive space|SQL2|C:", "Drive space: C: on SQL2", new DateTime(2026, 11, 11)), new("perf|CPU|SQL1", "Performance - CPU on SQL1", new DateTime(2026, 11, 11)) }
        };

        [TestMethod]
        public void Changes_ListsNewWorseBetterAndResolved()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", ReportType = DailyReportType.Changes };
            var issues = new[] { Backup("New", 2), Backup("Worse", 1), Backup("Better", 2), Backup("Same", 1) };
            var history = ChangesHistory(("Worse", 2), ("Better", 1), ("Same", 1));

            var content = DailyReportRenderer.RenderChanges(config, new DateTime(2026, 11, 13, 10, 0, 0), "{All}", new DataTable(), issues, history, "UTC");

            Assert.AreEqual("[DBA Dash] Ops: 1 new, 1 worse, 1 better, 1 resolved", content.Subject, "Performance findings aren't the changes report's to resolve");
            Assert.IsFalse(content.NothingToSend);
            StringAssert.Contains(content.TextBody, "Changes since 09:00");
            StringAssert.Contains(content.TextBody, "Open now: 2 critical, 2 warnings.");
            StringAssert.Contains(content.TextBody, "New on SQL1");
            StringAssert.Contains(content.TextBody, "Worse on SQL1 - Backups - Full: no full backup (was warning)");
            StringAssert.Contains(content.TextBody, "Better on SQL1 - Backups - Full: no full backup (was critical)");
            StringAssert.Contains(content.TextBody, "Drive space: C: on SQL2");
            Assert.IsFalse(content.TextBody.Contains("Same on SQL1"), "Unchanged issues aren't listed");
        }

        [TestMethod]
        public void History_InstanceOnlyAndObjectDetailAreTheSameIssue()
        {
            var drive = IssueHistory.ObjectKey("Drive space", "LAB2008R2", "C:\\");
            var instanceOnly = IssueHistory.ObjectKey("Drive space", "LAB2008R2", string.Empty);
            var otherDrive = IssueHistory.ObjectKey("Drive space", "LAB2008R2", "D:\\");
            var job = IssueHistory.ObjectKey("Agent jobs", "SQL1", "ETL | nightly");
            var firstSeen = new DateTime(2026, 11, 10);

            // Object detail, then only the instance flagged: ongoing, not resolved + new
            var history = new IssueHistory { RunUtc = firstSeen.AddDays(3), IsFirstRun = false, Resolved = new List<ResolvedIssue> { new(drive, "Drive space: C: on LAB2008R2", firstSeen) } };
            history.ReconcileObjectDetail(new[] { instanceOnly });
            Assert.IsFalse(history.IsNew(instanceOnly));
            Assert.AreEqual(3, history.DaysOngoing(instanceOnly));
            Assert.AreEqual(0, history.Resolved.Count);

            // The other way round
            history = new IssueHistory { RunUtc = firstSeen.AddDays(1), IsFirstRun = false, Resolved = new List<ResolvedIssue> { new(instanceOnly, "Drive space: LAB2008R2", firstSeen) } };
            history.ReconcileObjectDetail(new[] { drive });
            Assert.IsFalse(history.IsNew(drive));
            Assert.AreEqual(0, history.Resolved.Count);

            // A second drive next to one already reported is new; an object name with | in it is still parsed
            history = new IssueHistory { RunUtc = firstSeen.AddDays(1), IsFirstRun = false };
            history.FirstSeen[drive] = firstSeen;
            history.FirstSeen[job] = firstSeen;
            history.ReconcileObjectDetail(new[] { drive, otherDrive, job });
            Assert.IsTrue(history.IsNew(otherDrive));
        }

        [TestMethod]
        public void Changes_StillOpenSummaryCountsEverything()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", ReportType = DailyReportType.Changes };
            var history = ChangesHistory(("Same", 1), ("Other", 2));
            history.Resolved.Clear();
            var issues = new[] { Backup("Same", 1), Backup("Other", 2), new DailyReportIssue { CheckGroup = "Drive space", Status = 2, Instance = "SQL2", ObjectName = "C:", Detail = "10% free" } };
            var alerts = new[] { Alert("Blocking", 5), Alert("CPU", 0) };

            var text = DailyReportRenderer.RenderChanges(config, new DateTime(2026, 11, 13, 10, 0, 0), "{All}", new DataTable(), issues, history, "UTC", alerts).TextBody;
            StringAssert.Contains(text, "STILL OPEN");
            StringAssert.Contains(text, "  - Backups - Full: 1 critical, 1 warning on 1 server" + Environment.NewLine + "  - Drive space: 1 warning on 1 server" + Environment.NewLine + "  - Alerts: 1 Critical, 1 High on 1 server");

            config.Settings.IncludeOpenSummary = false;
            Assert.IsFalse(DailyReportRenderer.RenderChanges(config, DateTime.UtcNow, "{All}", new DataTable(), issues, history, "UTC").TextBody.Contains("STILL OPEN"));
        }

        [TestMethod]
        public void Changes_SendNowRecordsHistoryOnlyForChangesReports()
        {
            Assert.IsTrue(DailyReportRunner.RecordsHistoryOnSendNow(new DailyReportConfig { DailyReportID = 3, ReportType = DailyReportType.Changes }));
            Assert.IsFalse(DailyReportRunner.RecordsHistoryOnSendNow(new DailyReportConfig { DailyReportID = 3, ReportType = DailyReportType.Full }), "Tomorrow's full report still marks today's issues new");
            Assert.IsFalse(DailyReportRunner.RecordsHistoryOnSendNow(new DailyReportConfig { ReportType = DailyReportType.Changes }), "An unsaved report has nowhere to record it");
        }

        [TestMethod]
        public void Changes_NothingToSendWhenUnchangedOrFirstRun()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", ReportType = DailyReportType.Changes };
            var history = ChangesHistory(("Same", 1));
            history.Resolved.Clear();
            var unchanged = DailyReportRenderer.RenderChanges(config, DateTime.UtcNow, "{All}", new DataTable(), new[] { Backup("Same", 1) }, history, "UTC");
            Assert.IsTrue(unchanged.NothingToSend);
            Assert.AreEqual("[DBA Dash] Ops: no changes", unchanged.Subject);

            var first = DailyReportRenderer.RenderChanges(config, DateTime.UtcNow, "{All}", new DataTable(), new[] { Backup("A", 1) }, IssueHistory.None(DateTime.UtcNow), "UTC");
            Assert.IsTrue(first.NothingToSend, "The first run records a starting point");
            Assert.AreEqual(0, first.NewCount, "Nothing is new without a previous run");
        }

        // ---- Alerts ----

        private static DailyReportAlert Alert(string key, int priority, bool acknowledged = false, bool blackout = false) => new()
        {
            Instance = "SQL1", AlertType = "Counter", AlertKey = key, Priority = priority, Message = $"{key}  is\n over its threshold",
            TriggerUtc = new DateTime(2026, 11, 13, 7, 0, 0), IsAcknowledged = acknowledged, IsBlackout = blackout
        };

        [TestMethod]
        public void Alerts_FullReportListsThoseNeedingAttention()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            var alerts = new List<DailyReportAlert> { Alert("Blocking", 5), Alert("Waits", 15, acknowledged: true), Alert("Logins", 25, blackout: true) };
            var content = DailyReportRenderer.Render(config, new DateTime(2026, 11, 13, 10, 0, 0), "{All}", new DataTable(), null, null, "UTC",
                new DailyReportExtras { Alerts = alerts });

            Assert.AreEqual("[DBA Dash] Ops: checks OK, 1 alert", content.Subject);
            StringAssert.Contains(content.TextBody, "OPEN ALERTS");
            StringAssert.Contains(content.TextBody, "[High] Blocking on SQL1 - Blocking is over its threshold (for 3 hours)");
            StringAssert.Contains(content.TextBody, "Not listed: 1 acknowledged and 1 in a blackout.");
            Assert.IsFalse(content.TextBody.Contains("Waits on SQL1"));

            var none = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, null, "UTC").TextBody;
            Assert.IsFalse(none.Contains("OPEN ALERTS"), "No section when alerts aren't included");
        }

        [TestMethod]
        public void Alerts_InChangesReport()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", ReportType = DailyReportType.Changes };
            var history = ChangesHistory();
            history.Resolved.Clear();
            history.FirstSeen[IssueHistory.AlertKey("SQL1", "Blocking")] = new DateTime(2026, 11, 13, 7, 0, 0);
            history.PreviousStatus[IssueHistory.AlertKey("SQL1", "Blocking")] = Alert("Blocking", 15).HistoryStatus; // was Medium
            var alerts = new[] { Alert("Blocking", 5), Alert("Waits", 15, acknowledged: true), Alert("CPU", 0) };

            var content = DailyReportRenderer.RenderChanges(config, new DateTime(2026, 11, 13, 10, 0, 0), "{All}", new DataTable(), Array.Empty<DailyReportIssue>(), history, "UTC", alerts);

            Assert.AreEqual("[DBA Dash] Ops: 1 new, 1 worse", content.Subject, "The acknowledged alert isn't new - it's being dealt with");
            StringAssert.Contains(content.TextBody, "[Critical] CPU on SQL1");
            StringAssert.Contains(content.TextBody, "[High] Blocking on SQL1 - Alert: Blocking is over its threshold (escalated)");
            StringAssert.Contains(content.TextBody, "Open now: 2 alerts.");
        }

        [TestMethod]
        public void Alerts_HighToCriticalIsEscalated()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC", ReportType = DailyReportType.Changes };
            var history = ChangesHistory();
            history.Resolved.Clear();
            var key = IssueHistory.AlertKey("SQL1", "CPU");
            history.FirstSeen[key] = new DateTime(2026, 11, 13, 7, 0, 0);
            history.PreviousStatus[key] = Alert("CPU", 5).HistoryStatus; // High - the same display band as Critical

            var content = DailyReportRenderer.RenderChanges(config, new DateTime(2026, 11, 13, 10, 0, 0), "{All}", new DataTable(), Array.Empty<DailyReportIssue>(), history, "UTC", new[] { Alert("CPU", 0) });

            Assert.AreEqual(1, content.WorseCount);
            StringAssert.Contains(content.TextBody, "[Critical] CPU on SQL1 - Alert: CPU is over its threshold (escalated)");
        }

        [TestMethod]
        public void History_ChecksAndAlertsAreOnDifferentScales()
        {
            var history = new IssueHistory { RunUtc = DateTime.UtcNow, IsFirstRun = false };
            history.PreviousStatus["k"] = 2;
            Assert.IsFalse(history.IsBetter("k", 11), "A status recorded on the other scale isn't compared");
            Assert.IsTrue(history.IsWorse("k", 1));
        }

        [TestMethod]
        public void History_ReconciledKeyKeepsItsStatus()
        {
            var instanceOnly = IssueHistory.ObjectKey("Drive space", "SQL1", string.Empty);
            var drive = IssueHistory.ObjectKey("Drive space", "SQL1", "C:");
            var history = new IssueHistory
            {
                RunUtc = new DateTime(2026, 11, 13), IsFirstRun = false,
                Resolved = new List<ResolvedIssue> { new(instanceOnly, "Drive space: SQL1", new DateTime(2026, 11, 10), Status: 2) }
            };
            history.ReconcileObjectDetail(new[] { drive });
            Assert.IsFalse(history.IsNew(drive));
            Assert.IsTrue(history.IsWorse(drive, 1), "A warning on the instance that's critical on the drive got worse");
        }

        [TestMethod]
        public void Alerts_PriorityNamesAndAge()
        {
            Assert.AreEqual("Critical", Alert("a", 0).PriorityName);
            Assert.AreEqual("High", Alert("a", 10).PriorityName);
            Assert.AreEqual("Medium", Alert("a", 11).PriorityName);
            Assert.AreEqual("Low", Alert("a", 30).PriorityName);
            var t = new DateTime(2026, 11, 13, 7, 0, 0);
            Assert.AreEqual("for 25 min", DailyReportRenderer.AlertAge(t, t.AddMinutes(25)));
            Assert.AreEqual("for 1 hour", DailyReportRenderer.AlertAge(t, t.AddMinutes(61)));
            Assert.AreEqual("for 3 days", DailyReportRenderer.AlertAge(t, t.AddDays(3)));
        }

        [TestMethod]
        public void AiSummary_OverviewAndBulletsParsed()
        {
            var blocks = AiSummaryFormat.Parse("Today needs attention on backups.\n\n- **Missing backups:** 63 databases on 9 servers\n* Drive space: C: on\n  LAB2008R2 at 0 GB\n• third\n2) fourth\n\n## Next\nplain");
            CollectionAssert.AreEqual(new[]
            {
                new AiSummaryBlock(false, "Today needs attention on backups."),
                new AiSummaryBlock(true, "**Missing backups:** 63 databases on 9 servers"),
                new AiSummaryBlock(true, "Drive space: C: on LAB2008R2 at 0 GB"),
                new AiSummaryBlock(true, "third"),
                new AiSummaryBlock(true, "fourth"),
                new AiSummaryBlock(false, "**Next**"),
                new AiSummaryBlock(false, "plain")
            }, blocks);
        }

        [TestMethod]
        public void AiSummary_BoldKeptOtherMarkdownRemovedAndEncoded()
        {
            Assert.AreEqual("<strong>Missing backups:</strong> on &lt;b&gt;SQL1&lt;/b&gt; and SQL2 now", AiSummaryFormat.ToHtml("**Missing backups:** on <b>SQL1</b> and *SQL2* `now`"));
            Assert.AreEqual("Missing backups: on SQL1 and SQL2", AiSummaryFormat.ToText("**Missing backups:** on SQL1 and *SQL2*"));
            Assert.AreEqual("<strong>&lt;script&gt;</strong>", AiSummaryFormat.ToHtml("**<script>**"));
            Assert.AreEqual("C:\\ 0.0 GB free", AiSummaryFormat.ToText("C:\\ 0.0 GB free"));
        }

        [TestMethod]
        public void AiSummary_BulletsRenderedAsRows()
        {
            var config = new DailyReportConfig { ReportName = "Ops", TimeZone = "UTC" };
            config.Settings.IncludeAISummary = true;
            var content = DailyReportRenderer.Render(config, DateTime.UtcNow, "{All}", new DataTable(), null, null, "UTC",
                new DailyReportExtras { AiSummary = new DailyReportAiSummary { Summary = "Overall poor.\n- **Backups:** missing on SQL1\n- **Drives:** C: full", Model = "m1" } });
            Assert.IsTrue(content.HtmlBody.Contains("<strong>Backups:</strong> missing on SQL1"), "Bold topic");
            Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(content.HtmlBody, "&#8226;").Count, "One bullet per point");
            Assert.IsFalse(content.HtmlBody.Contains("**"), "No raw markdown");
            StringAssert.Contains(content.TextBody, "Overall poor." + Environment.NewLine + "  - Backups: missing on SQL1" + Environment.NewLine + "  - Drives: C: full");
        }
    }
}
