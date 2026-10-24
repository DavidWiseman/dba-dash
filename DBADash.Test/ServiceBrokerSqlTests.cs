using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;

namespace DBADash.Test
{
    /// <summary>
    /// The Service Broker collection run against a database set up with a problem for each check, imported with dbo.ServiceBroker_Upd
    /// as DBImporter does, then the status views and report read back.  The collection is off by default so CI's end-to-end run
    /// wouldn't otherwise exercise these checks - on any of the SQL versions or the binary server collation CI tests.
    ///
    /// <para>The snapshot is imported 20 mins in the future so the age checks fire (15 min queue & transmission queue warning
    /// thresholds, 10 mins for activation that's notified without anything receiving) without the test waiting for them.</para>
    ///
    /// <para>Same server as <see cref="MessageAuthorizationSqlTests"/>: the <see cref="MessageAuthorizationSqlTests.ServerEnvironmentVariable"/>
    /// connection string, or LocalDB.  Both databases are dropped afterwards.</para>
    /// </summary>
    [TestClass]
    [TestCategory("SqlServer")]
    public class ServiceBrokerSqlTests
    {
        private static readonly string? ServerConnectionString = Environment.GetEnvironmentVariable(MessageAuthorizationSqlTests.ServerEnvironmentVariable) is { Length: > 0 } cs ? cs : null;
        private static readonly string Suffix = Guid.NewGuid().ToString("N");
        private static readonly string Repository = "DBADashSBRepoTest_" + Suffix;
        private static readonly string BrokerDB = "DBADashSBTest_" + Suffix;
        private static string MasterConnectionString => ConnectionStringFor("master");
        private static string RepositoryConnectionString => ConnectionStringFor(Repository);
        private static string BrokerConnectionString => ConnectionStringFor(BrokerDB);

        private static DataSet collected = new();
        private static int instanceID;
        private static int databaseID;

        private static string ConnectionStringFor(string database)
        {
            var builder = ServerConnectionString == null
                ? new SqlConnectionStringBuilder { DataSource = @"(localdb)\MSSQLLocalDB", IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 60 }
                : new SqlConnectionStringBuilder(ServerConnectionString);
            builder.InitialCatalog = database;
            return builder.ConnectionString;
        }

        [ClassInitialize]
        public static void ClassInitialize(TestContext _)
        {
            try
            {
                using var master = new SqlConnection(MasterConnectionString);
                master.Open();
            }
            catch (SqlException ex) when (ServerConnectionString == null)
            {
                Assert.Inconclusive($"LocalDB isn't available: {ex.Message}");
                return;
            }
            new DacpacUtility.DacpacService().ProcessDacPac(MasterConnectionString, Repository, MessageAuthorizationSqlTests.FindDacpac(), DBValidations.DBVersionStatusEnum.CreateDB);
            CreateBrokerDatabase();
            collected = Collect();
            Import(collected);
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            try
            {
                SqlConnection.ClearAllPools();
                using var master = new SqlConnection(MasterConnectionString);
                master.Open();
                foreach (var db in new[] { Repository, BrokerDB })
                {
                    Execute(master, $"IF DB_ID('{db}') IS NOT NULL BEGIN ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{db}]; END");
                }
            }
            catch (SqlException)
            {
                // The server wasn't available - nothing to clean up
            }
        }

        /// <summary>
        /// A queue for each queue check, a message to a service that doesn't exist (transmission queue) and a conversation ended with
        /// an error (ER on the initiator).  Waits for the queue whose activation procedure doesn't RECEIVE to be NOTIFIED.
        /// </summary>
        private static void CreateBrokerDatabase()
        {
            using (var master = new SqlConnection(MasterConnectionString))
            {
                master.Open();
                Execute(master, $"CREATE DATABASE [{BrokerDB}]");
                Execute(master, $"ALTER DATABASE [{BrokerDB}] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE");
            }
            using var cn = new SqlConnection(BrokerConnectionString);
            cn.Open();
            Execute(cn, "CREATE PROC dbo.NoReceive AS RETURN");
            Execute(cn, @"
CREATE QUEUE dbo.InitiatorQueue
CREATE SERVICE InitiatorService ON QUEUE dbo.InitiatorQueue
/* Messages that sit waiting - no activation */
CREATE QUEUE dbo.WaitingQueue
CREATE SERVICE WaitingService ON QUEUE dbo.WaitingQueue([DEFAULT])
/* Activation that fires but nothing receives */
CREATE QUEUE dbo.NotReceivingQueue WITH ACTIVATION(STATUS = ON, PROCEDURE_NAME = dbo.NoReceive, MAX_QUEUE_READERS = 1, EXECUTE AS OWNER)
CREATE SERVICE NotReceivingService ON QUEUE dbo.NotReceivingQueue([DEFAULT])
/* Received messages are retained, so they're in the queue's internal table */
CREATE QUEUE dbo.RetentionQueue WITH RETENTION = ON
CREATE SERVICE RetentionService ON QUEUE dbo.RetentionQueue([DEFAULT])
/* SqlDependency creates a queue per listener */
CREATE QUEUE dbo.[SqlQueryNotificationService-11111111-1111-1111-1111-111111111111]
CREATE QUEUE dbo.[SqlQueryNotificationService-22222222-2222-2222-2222-222222222222]");
            Execute(cn, @"
DECLARE @h UNIQUEIDENTIFIER, @i INT = 0
WHILE @i < 3
BEGIN
	BEGIN DIALOG @h FROM SERVICE InitiatorService TO SERVICE 'WaitingService' WITH ENCRYPTION = OFF;
	SEND ON CONVERSATION @h (0x01);
	BEGIN DIALOG @h FROM SERVICE InitiatorService TO SERVICE 'RetentionService' WITH ENCRYPTION = OFF;
	SEND ON CONVERSATION @h (0x01);
	SET @i += 1
END
BEGIN DIALOG @h FROM SERVICE InitiatorService TO SERVICE 'NotReceivingService' WITH ENCRYPTION = OFF;
SEND ON CONVERSATION @h (0x01);
BEGIN DIALOG @h FROM SERVICE InitiatorService TO SERVICE 'MissingService' WITH ENCRYPTION = OFF;
SEND ON CONVERSATION @h (0x01);
/* One conversation group each, so this receives one of the three */
RECEIVE TOP(1) * FROM dbo.RetentionQueue;
/* The target ends with an error - the initiator's endpoint goes to ER */
BEGIN DIALOG @h FROM SERVICE InitiatorService TO SERVICE 'WaitingService' WITH ENCRYPTION = OFF;
SEND ON CONVERSATION @h (0x02);
SELECT TOP(1) @h = conversation_handle FROM dbo.WaitingQueue WHERE message_body = 0x02;
END CONVERSATION @h WITH ERROR = 50001 DESCRIPTION = N'Test';");

            for (var i = 0; i < 60; i++)
            {
                if (Scalar(cn, "SELECT COUNT(*) FROM sys.dm_broker_queue_monitors WHERE database_id = DB_ID() AND queue_id = OBJECT_ID('dbo.NotReceivingQueue') AND state = 'NOTIFIED'") is 1) return;
                Thread.Sleep(1000);
            }
            Assert.Fail("NotReceivingQueue wasn't NOTIFIED within 60 seconds.");
        }

        /// <summary>The collection query as DBCollector runs it.  It visits every broker enabled database on the server - only the test database is imported.</summary>
        private static DataSet Collect()
        {
            var ds = new DataSet();
            using var cn = new SqlConnection(MasterConnectionString);
            using var cmd = new SqlCommand(SqlStrings.ServiceBroker, cn) { CommandTimeout = 120 };
            new SqlDataAdapter(cmd).Fill(ds);
            return ds;
        }

        /// <summary>As DBImporter: each result set to its table-valued parameter, only where it has rows.</summary>
        private static void Import(DataSet ds)
        {
            using var cn = new SqlConnection(RepositoryConnectionString);
            cn.Open();
            Execute(cn, $@"
INSERT INTO dbo.Instances(Instance,ConnectionID,IsActive,ShowInSummary,EngineEdition) VALUES(N'SQL1',N'SQL1',1,1,3)
INSERT INTO dbo.Databases(InstanceID,name,database_id,create_date,compatibility_level,is_auto_close_on,is_master_key_encrypted_by_server,is_published,is_subscribed,is_merge_published,is_distributor,is_sync_with_backup,is_broker_enabled,is_date_correlation_on,IsActive,state)
VALUES(SCOPE_IDENTITY(),N'{BrokerDB}',DB_ID(N'{BrokerDB}'),SYSUTCDATETIME(),160,0,0,0,0,0,0,0,1,0,1,0)");
            instanceID = (int)Scalar(cn, "SELECT InstanceID FROM dbo.Instances WHERE Instance = N'SQL1'");
            databaseID = (int)Scalar(cn, $"SELECT DatabaseID FROM dbo.Databases WHERE name = N'{BrokerDB}'");

            using var cmd = new SqlCommand("dbo.ServiceBroker_Upd", cn) { CommandType = CommandType.StoredProcedure };
            for (var i = 0; i < ServiceBrokerTables.TableNames.Length; i++)
            {
                if (ds.Tables[i].Rows.Count > 0) cmd.Parameters.AddWithValue(ServiceBrokerTables.TableNames[i], ds.Tables[i]);
            }
            cmd.Parameters.AddWithValue("InstanceID", instanceID);
            cmd.Parameters.AddWithValue("SnapshotDate", DateTime.UtcNow.AddMinutes(20));
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Table-valued parameters bind by ordinal, so a column out of place in the collection query or its user defined type would be
        /// imported into the wrong column - possibly without an error.  The names have to line up as well as the count.
        /// </summary>
        [TestMethod]
        public void CollectionColumnsMatchTableTypes()
        {
            Assert.AreEqual(ServiceBrokerTables.TableNames.Length, collected.Tables.Count, "Result sets returned by the collection");
            using var cn = new SqlConnection(RepositoryConnectionString);
            cn.Open();
            for (var i = 0; i < ServiceBrokerTables.TableNames.Length; i++)
            {
                var typeColumns = new List<string>();
                using var cmd = new SqlCommand(@"
SELECT c.name
FROM sys.table_types tt
JOIN sys.columns c ON c.object_id = tt.type_table_object_id
WHERE tt.name = @name
AND tt.schema_id = SCHEMA_ID('dbo')
ORDER BY c.column_id", cn);
                cmd.Parameters.AddWithValue("name", ServiceBrokerTables.TableNames[i]);
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read()) typeColumns.Add(rdr.GetString(0));
                }
                var collectedColumns = collected.Tables[i].Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                CollectionAssert.AreEqual(typeColumns, collectedColumns, $"{ServiceBrokerTables.TableNames[i]}: collection [{string.Join(",", collectedColumns)}] vs type [{string.Join(",", typeColumns)}]");
            }
        }

        [TestMethod]
        public void OldMessagesWaitingIsAWarning()
        {
            var queue = Queue("WaitingQueue");
            Assert.AreEqual(3L, queue["message_count"]);
            Assert.AreEqual(2, queue["QueueAgeStatus"], "Oldest message ~20 mins old against the 15 min warning threshold");
            Assert.AreEqual(2, queue["QueueStatus"]);
            StringAssert.Contains((string)queue["Issues"], "Oldest message has been waiting");
        }

        [TestMethod]
        public void ActivationNotReceivingIsAWarning()
        {
            var queue = Queue("NotReceivingQueue");
            Assert.AreEqual("NOTIFIED", queue["monitor_state"]);
            Assert.AreEqual(2, queue["QueueStatus"]);
            StringAssert.Contains((string)queue["Issues"], "Activation was notified");
        }

        [TestMethod]
        public void RetentionQueueDepthExcludesReceivedMessages()
        {
            var queue = Queue("RetentionQueue");
            Assert.AreEqual(true, queue["is_retention_enabled"]);
            Assert.AreEqual(2L, queue["message_count"], "3 sent, 1 received and retained");
        }

        [TestMethod]
        public void SqlDependencyQueuesAreRolledUp()
        {
            var rows = Query($"SELECT queue_name FROM dbo.ServiceBrokerQueues WHERE DatabaseID = {databaseID} AND queue_name LIKE N'SqlQueryNotificationService-%'");
            Assert.AreEqual(1, rows.Rows.Count);
            Assert.AreEqual("SqlQueryNotificationService-*", rows.Rows[0]["queue_name"]);
        }

        [TestMethod]
        public void DatabaseChecks()
        {
            var db = Query($"SELECT * FROM dbo.ServiceBrokerDatabaseInfo WHERE DatabaseID = {databaseID}").Rows.Cast<DataRow>().Single();
            Assert.AreEqual(1L, db["transmission_queue_count"], "Message to a service that doesn't exist");
            Assert.AreEqual(2, db["TransmissionQueueStatus"]);
            Assert.AreEqual(1L, db["error_count"], "Initiator of the conversation the target ended with an error");
            Assert.AreEqual(2, db["ErrorConversationStatus"]);
            Assert.AreEqual(2, db["DatabaseStatus"]);
        }

        [TestMethod]
        public void InstanceStatusIncludesQueuesAndDatabases()
        {
            var instance = Query($"SELECT * FROM dbo.ServiceBrokerInstanceInfo WHERE InstanceID = {instanceID}").Rows.Cast<DataRow>().Single();
            Assert.AreEqual(2, instance["ServiceBrokerStatus"]);
        }

        /// <summary>Over the conversation scan limit the collection returns the total with state '??' - counted as open as the states are unknown.</summary>
        [TestMethod]
        public void UncountedConversationsAreOpen()
        {
            var before = (long)Query($"SELECT conversation_count FROM dbo.ServiceBrokerDatabaseInfo WHERE DatabaseID = {databaseID}").Rows[0][0];
            using var cn = new SqlConnection(RepositoryConnectionString);
            cn.Open();
            using var tran = cn.BeginTransaction();
            using var cmd = new SqlCommand($@"
INSERT INTO dbo.ServiceBrokerConversations(InstanceID,DatabaseID,state,conversation_count,SnapshotDate)
SELECT TOP(1) InstanceID,DatabaseID,'??',2000000,SnapshotDate FROM dbo.ServiceBrokerConversations WHERE DatabaseID = {databaseID}
SELECT conversation_count, ConversationStatus FROM dbo.ServiceBrokerDatabaseInfo WHERE DatabaseID = {databaseID}", cn, tran);
            using (var rdr = cmd.ExecuteReader())
            {
                Assert.IsTrue(rdr.Read());
                Assert.AreEqual(before + 2000000, rdr.GetInt64(0));
                Assert.AreEqual(2, rdr.GetInt32(1), "Over the 100,000 open conversation warning threshold");
            }
            tran.Rollback();
        }

        [TestMethod]
        public void ReportReturnsEveryResultSet()
        {
            foreach (var forDatabase in new[] { false, true })
            {
                using var cn = new SqlConnection(RepositoryConnectionString);
                using var cmd = new SqlCommand("dbo.ServiceBrokerReport_Get", cn) { CommandType = CommandType.StoredProcedure };
                var ids = new DataTable();
                ids.Columns.Add("ID", typeof(int));
                ids.Rows.Add(instanceID);
                cmd.Parameters.Add(new SqlParameter("InstanceIDs", SqlDbType.Structured) { TypeName = "dbo.IDs", Value = ids });
                if (forDatabase) cmd.Parameters.AddWithValue("DatabaseID", databaseID);
                var ds = new DataSet();
                new SqlDataAdapter(cmd).Fill(ds);
                Assert.AreEqual(7, ds.Tables.Count, $"Result sets (DatabaseID {(forDatabase ? "set" : "not set")})");
                Assert.AreEqual(5, ds.Tables[2].Rows.Count, "Queues: initiator, waiting, not receiving, retention and the SqlDependency roll up");
            }
        }

        private static DataRow Queue(string name) =>
            Query($"SELECT * FROM dbo.ServiceBrokerQueueInfo WHERE DatabaseID = {databaseID} AND queue_name = N'{name}'").Rows.Cast<DataRow>().Single();

        private static DataTable Query(string sql)
        {
            using var cn = new SqlConnection(RepositoryConnectionString);
            var dt = new DataTable();
            new SqlDataAdapter(new SqlCommand(sql, cn)).Fill(dt);
            return dt;
        }

        private static object Scalar(SqlConnection cn, string sql)
        {
            using var cmd = new SqlCommand(sql, cn);
            return cmd.ExecuteScalar()!;
        }

        private static void Execute(SqlConnection cn, string sql)
        {
            using var cmd = new SqlCommand(sql, cn);
            cmd.ExecuteNonQuery();
        }
    }
}
