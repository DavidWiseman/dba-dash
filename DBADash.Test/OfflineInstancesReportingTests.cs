using System;
using DBADashService;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DBADash.Test
{
    /// <summary>
    /// The repository opens and closes offline incidents from these reports, so a change to the set of
    /// offline instances has to be reported as soon as it happens.  An unchanged state is repeated on an
    /// interval to recover from a lost report rather than on every check - see #2042, where the every 10
    /// seconds repeat filled a folder destination with files.
    /// </summary>
    [TestClass]
    public class OfflineInstancesReportingTests
    {
        private const string NoneOffline = "";
        private const string OneOffline = "SERVER1@1000";
        private static readonly DateTime Now = new(2026, 09, 11, 12, 00, 00, DateTimeKind.Utc);

        [TestMethod]
        public void FirstReport_IsAlwaysRequired()
        {
            // Nothing reported yet.  Reporting on startup is what closes incidents left open by a restart.
            Assert.IsTrue(OfflineInstances.IsReportRequired(NoneOffline, null, DateTime.MinValue, Now, 60));
        }

        [TestMethod]
        public void InstanceGoingOffline_IsReportedImmediately()
        {
            Assert.IsTrue(OfflineInstances.IsReportRequired(OneOffline, NoneOffline, Now, Now, 60));
        }

        [TestMethod]
        public void InstanceComingBackOnline_IsReportedImmediately()
        {
            Assert.IsTrue(OfflineInstances.IsReportRequired(NoneOffline, OneOffline, Now, Now, 60));
        }

        [TestMethod]
        public void UnchangedState_IsNotReportedBeforeTheInterval()
        {
            Assert.IsFalse(OfflineInstances.IsReportRequired(NoneOffline, NoneOffline, Now, Now.AddSeconds(59), 60));
            Assert.IsFalse(OfflineInstances.IsReportRequired(OneOffline, OneOffline, Now, Now.AddSeconds(59), 60));
        }

        [TestMethod]
        public void UnchangedState_IsReportedOnTheInterval()
        {
            Assert.IsTrue(OfflineInstances.IsReportRequired(NoneOffline, NoneOffline, Now, Now.AddSeconds(60), 60));
            Assert.IsTrue(OfflineInstances.IsReportRequired(OneOffline, OneOffline, Now, Now.AddSeconds(60), 60));
        }

        [TestMethod]
        public void ZeroInterval_ReportsChangesOnly()
        {
            Assert.IsFalse(OfflineInstances.IsReportRequired(NoneOffline, NoneOffline, Now, Now.AddDays(1), 0));
            Assert.IsTrue(OfflineInstances.IsReportRequired(OneOffline, NoneOffline, Now, Now, 0));
        }
    }
}
