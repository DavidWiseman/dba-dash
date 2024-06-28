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

namespace DBADash.Messaging
{
    public class QueryStoreMessage : MessageBase
    {

        public string ConnectionID { get; set; }

        public string DatabaseName { get; set; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public int Top { get; set; } = 25;

        public string SortColumn { get; set; } = "total_cpu_time_ms";

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
                await using var cn = new SqlConnection(src.ConnectionString);
                await cn.OpenAsync();
                await using var cmd = new SqlCommand(SqlStrings.QueryStoreTopQueries, cn);
                cmd.Parameters.AddWithValue("@Database", DatabaseName);
                cmd.Parameters.AddWithValue("@interval_start_time", From);
                cmd.Parameters.AddWithValue("@interval_end_time ", To);
                cmd.Parameters.AddWithValue("@Top", Top);
                cmd.Parameters.AddWithValue("@sort_column", SortColumn);
                var da = new SqlDataAdapter(cmd);
                var ds = new DataSet();
                da.Fill(ds);
                return ds;

            }
            catch (Exception ex)
            {
                Log.Error(ex,"Error processing query store message");
                throw;
            }
            op.Complete();
        }
    }
}
