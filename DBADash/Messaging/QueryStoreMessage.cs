using Octokit;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Serilog;
using SerilogTimings;
using static Microsoft.SqlServer.Management.SqlParser.Metadata.MetadataInfoProvider;
using System.Collections.Concurrent;

namespace DBADash.Messaging
{
    public class QueryStoreMessage : MessageBase
    {
        public string ConnectionID { get; set; }

        public string DatabaseName { get; set; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public int Top { get; set; } = 25;

        public int? ObjectID { get; set; }

        public string ObjectName { get; set; }

        public string SortColumn { get; set; } = "total_cpu_time_ms";

        public bool NearestInterval { get; set; } = true;

        private const int maxDegreeOfParallelism = 4;

        public static async Task<List<string>> GetDatabasesAsync(string connectionString)
        {
            var databases = new List<string>();
            var query = @"
        SELECT D.name
        FROM sys.databases D
        WHERE D.is_query_store_on=1
        AND D.database_id>4
        AND D.state=0
        AND HAS_DBACCESS(D.name)=1
        AND D.is_in_standby = 0
        AND DATABASEPROPERTYEX(D.name, 'Updateability') = 'READ_WRITE';
    ";
            await using var cn = new SqlConnection(connectionString);
            await using var command = new SqlCommand(query, cn);

            await cn.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                databases.Add(reader.GetString(0));
            }

            return databases;
        }

        public override async Task<DataSet> Process(CollectionConfig cfg, Guid handle)
        {
            if (IsExpired)
            {
                throw new Exception("Message expired");
            }
            using var op = Operation.Begin("Query store top queries for {database} on {instance} triggered from message {handle}",
                DatabaseName,
                ConnectionID,
                handle);
            try
            {
                var src = cfg.GetSourceConnection(ConnectionID);
                List<string> databases;
                if (string.IsNullOrEmpty(DatabaseName))
                {
                    databases = await GetDatabasesAsync(src.SourceConnection.ConnectionString);
                }
                else
                {
                    databases = new List<string> { DatabaseName };
                }
                if (databases.Count == 0)
                {
                    throw new Exception("No databases found with Query Store enabled");
                }
                var resultTable = new DataTable();
                var options = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };

                // Use a concurrent bag to collect DataTables from parallel tasks.
                var dataTables = new ConcurrentBag<DataTable>();
                var exceptions = new ConcurrentBag<Exception>();

                // Execute the query in parallel for each database.
                Parallel.ForEach(databases, options, async database =>
                {
                    try
                    {
                        var dt = await GetTopQueriesForDatabase(src.ConnectionString, database);
                        dataTables.Add(dt);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });

                // Wait for all parallel tasks to complete (important when using async/await within Parallel.ForEach).
                await Task.WhenAll(dataTables.Select(dt => Task.CompletedTask));

                if (exceptions.Count > 0)
                {
                    throw new AggregateException(exceptions);
                }

                // Merge all DataTables into a single DataTable.
                foreach (var dt in dataTables)
                {
                    if (resultTable.Columns.Count == 0)
                    {
                        resultTable = dt.Clone(); // Clone the structure of the first DataTable.
                    }

                    foreach (DataRow row in dt.Rows)
                    {
                        resultTable.ImportRow(row);
                    }
                }

                if (resultTable.Rows.Count > Top)
                {
                    var topXRows = resultTable.AsEnumerable()
                        .OrderByDescending(row => row[SortColumn]) // Or OrderByDescending for descending order.
                        .Take(Top);

                    resultTable = topXRows.CopyToDataTable();
                }

                var ds = new DataSet();
                ds.Tables.Add(resultTable);
                op.Complete();
                return ds;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing query store message");
                throw;
            }
        }

        private async Task<DataTable> GetTopQueriesForDatabase(string connectionString, string db)
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(SqlStrings.QueryStoreTopQueries, cn);
            cmd.Parameters.AddWithValue("@Database", db);
            cmd.Parameters.AddWithValue("@FromDate", From);
            cmd.Parameters.AddWithValue("@ToDate ", To);
            cmd.Parameters.AddWithValue("@Top", Top);
            cmd.Parameters.AddWithValue("@SortCol", SortColumn);
            cmd.Parameters.AddWithValue("@NearestInterval", NearestInterval);
            cmd.Parameters.AddWithValue("@ObjectID", ObjectID is > 0 ? ObjectID.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@ObjectName", string.IsNullOrEmpty(ObjectName) ? DBNull.Value : ObjectName);

            var da = new SqlDataAdapter(cmd);
            var dt = new DataTable();
            da.Fill(dt);
            return dt;
        }
    }
}