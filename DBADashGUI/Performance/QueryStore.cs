using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DBADash;
using DBADash.Messaging;
using DBADashGUI.CustomReports;
using DBADashGUI.SchemaCompare;
using DBADashGUI.Theme;
using Microsoft.Data.SqlClient;

namespace DBADashGUI.Performance
{
    public partial class QueryStore : UserControl, ISetContext, IRefreshData
    {

        private DBADashContext CurrentContext;

        public QueryStore()
        {
            InitializeComponent();
        }

        public void SetContext(DBADashContext context)
        {
            CurrentContext = context;
        }

        public async void RefreshData()
        {
            var message = new QueryStoreMessage() { CollectAgent = CurrentContext.CollectAgent, ImportAgent = CurrentContext.ImportAgent };
            message.ConnectionID = CurrentContext.InstanceName;
            message.DatabaseName = CurrentContext.DatabaseName;
            message.From = new DateTimeOffset(DateRange.FromUTC, TimeSpan.Zero);
            message.To = new DateTimeOffset(DateRange.ToUTC, TimeSpan.Zero);
            var messageGroup = Guid.NewGuid();
            await MessageProcessing.SendMessageToService(message.Serialize(), (int)CurrentContext.ImportAgentID, messageGroup, Common.ConnectionString, 120);
            var completed = false;
            while (!completed)
            {
                completed = true;
                var reply = await ReceiveReply(messageGroup);
                switch (reply.Type)
                {
                    case ResponseMessage.ResponseTypes.Progress:
                        completed = false;
                        break;
                    case ResponseMessage.ResponseTypes.Failure:
                        MessageBox.Show(reply.Message);
                        break;
                    case ResponseMessage.ResponseTypes.Success:
                        {
                            var ds = reply.Data;
                            dgv.Columns.Clear();
                            AddColumns(dgv, ds.Tables[0], topQueriesResult);
                            dgv.DataSource = ds.Tables[0];
                            dgv.LoadColumnLayout(topQueriesResult.ColumnLayout);
                            dgv.ApplyTheme();
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// /////////////
        /// </summary>
        private static void AddColumns(DataGridView dgv, DataTable dt, CustomReportResult customReportResult)
        {
            foreach (DataColumn dataColumn in dt.Columns)
            {
                DataGridViewColumn column;

                if (customReportResult.LinkColumns.ContainsKey(dataColumn.ColumnName))
                {
                    column = new DataGridViewLinkColumn();
                }
                else if (dataColumn.DataType == typeof(bool))
                {
                    column = new DataGridViewCheckBoxColumn();
                }
                else
                {
                    column = new DataGridViewTextBoxColumn();
                }

                column.SortMode = DataGridViewColumnSortMode.Automatic;
                column.DefaultCellStyle.Format =
                    customReportResult.CellFormatString.TryGetValue(dataColumn.ColumnName, out var value)
                        ? value
                        : "";

                column.DataPropertyName = dataColumn.ColumnName;
                column.Name = dataColumn.ColumnName;
                column.HeaderText =
                    customReportResult.ColumnAlias.TryGetValue(column.DataPropertyName, out var alias)
                        ? alias
                        : dataColumn.Caption;
                column.ValueType = dataColumn.DataType;
                dgv.Columns.Add(column);
            }
        }

        private readonly CustomReportResult topQueriesResult = new()
        {
            ColumnAlias = new Dictionary<string, string>
            {
                { "DB", "DB" },
                { "query_id", "Query ID" },
                { "object_id", "Object ID" },
                { "object_name", "Object Name" },
                { "query_sql_text", "Text" },
                { "total_cpu_time_ms", "Total CPU (ms)" },
                { "avg_cpu_time_ms", "Avg CPU (ms)" },
                { "total_duration_ms", "Total Duration (ms)" },
                { "avg_duration_ms", "Avg Duration (ms)" },
                { "count_executions", "Execs" },
                { "executions_per_min", "Execs/min" },
                { "max_memory_grant_kb", "Max Memory Grant KB" },
                { "total_physical_io_reads_kb", "Physical Reads KB" },
                { "avg_physical_io_reads_kb", "Avg Physical Reads KB" },
                { "num_plans", "Plan Count" },
            },
            CellFormatString = new Dictionary<string, string>
            {
                { "total_cpu_time_ms", "N0" },
                { "avg_cpu_time_ms", "N1" },
                { "total_duration_ms", "N0" },
                { "avg_duration_ms", "N1" },
                { "count_executions", "N0" },
                { "executions_per_min", "N2" },
                { "max_memory_grant_kb", "N0" },
                { "total_physical_io_reads_kb", "N0" },
                { "avg_physical_io_reads_kb", "N0" },
                {"num_plans","N0"}
            },
            ResultName = "Top Queries",
            LinkColumns = new Dictionary<string, LinkColumnInfo>
            {
                {
                    "query_sql_text",
                    new TextLinkColumnInfo()
                    {
                        TargetColumn = "query_sql_text",
                        TextHandling = CodeEditor.CodeEditorModes.SQL
                    }
                }
            },
            ColumnLayout = new List<KeyValuePair<string, PersistedColumnLayout>>()
            {
                new KeyValuePair<string, PersistedColumnLayout>("DB", new PersistedColumnLayout() { Width = 150, Visible = false }),
                new KeyValuePair<string, PersistedColumnLayout>("query_id", new PersistedColumnLayout() { Width = 100, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("object_id", new PersistedColumnLayout() { Width = 100, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("object_name", new PersistedColumnLayout() { Width = 150, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("query_sql_text", new PersistedColumnLayout() { Width = 200, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("total_cpu_time_ms", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("avg_cpu_time_ms", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("total_duration_ms", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("avg_duration_ms", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("count_executions", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("executions_per_min", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("max_memory_grant_kb", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("total_physical_io_reads_kb", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("avg_physical_io_reads_kb", new PersistedColumnLayout() { Width = 70, Visible = true }),
                new KeyValuePair<string, PersistedColumnLayout>("num_plans", new PersistedColumnLayout() { Width = 70, Visible = true }),
            },  
        };

        public static async Task<ResponseMessage> ReceiveReply(Guid group)
        {

            await using var cn = new SqlConnection(Common.ConnectionString);
            await using var cmd = new SqlCommand("Messaging.ReceiveReplyFromServiceToGUI", cn)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
            cmd.Parameters.AddWithValue("@ConversationGroupID", group);
            await cn.OpenAsync();
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!rdr.Read()) throw new Exception("No results");
            var handle = (Guid)rdr["conversation_handle"];
            var type = (string)rdr["message_type_name"];

            var reply = rdr["message_body"] == DBNull.Value ? null : (byte[])rdr["message_body"];

            var msg = ResponseMessage.Deserialize(reply);
            return msg;

        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            RefreshData();
        }

        private void Dgv_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var colName = dgv.Columns[e.ColumnIndex].DataPropertyName;
            LinkColumnInfo linkColumnInfo = null;
            topQueriesResult.LinkColumns?.TryGetValue(colName, out linkColumnInfo);
            try
            {
                linkColumnInfo?.Navigate(CurrentContext, dgv.Rows[e.RowIndex], 0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error navigating to link: " + ex.Message, "Error", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
