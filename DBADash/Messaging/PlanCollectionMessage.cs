﻿using Microsoft.Data.SqlClient;
using Serilog;
using SerilogTimings;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DBADash.Messaging
{
    public class PlanCollectionMessage : MessageBase
    {
        public string ConnectionID { get; set; }
        public string DatabaseName { get; set; }

        public long PlanID { get; set; }

        public override async Task<DataSet> Process(CollectionConfig cfg, Guid handle, CancellationToken cancellationToken)
        {
            if (IsExpired)
            {
                throw new Exception("Message expired");
            }

            using var op = Operation.Begin(
                "Get Plan Id {id} from {database} on {instance} triggered from message {handle}",
                PlanID,
                DatabaseName,
                ConnectionID,
                handle);
            try
            {
                var src = await cfg.GetSourceConnectionAsync(ConnectionID);
                var builder = new SqlConnectionStringBuilder(src.SourceConnection.ConnectionString)
                {
                    InitialCatalog = DatabaseName
                };
                await using var cn = new SqlConnection(builder.ConnectionString);
                await cn.OpenAsync(cancellationToken);
                await using var cmd =
                    new SqlCommand("SELECT plan_id,query_plan FROM sys.query_store_plan WHERE plan_id = @plan_id", cn);
                cmd.Parameters.AddWithValue("@plan_id", PlanID);
                var ds = new DataSet();
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                var dataTable = new DataTable();
                dataTable.Load(reader);
                ds.Tables.Add(dataTable);
                return ds;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Plan collection request from {handle} failed", handle);
                throw;
            }
        }
    }
}