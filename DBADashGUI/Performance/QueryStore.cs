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
            if (context != CurrentContext)
            {
                dgv.DataSource = null;
                txtObjectName.Text = string.Empty;
            }
            tsExecute.Text = string.IsNullOrEmpty(context.DatabaseName) ? "Execute (ALL Databases)" : "Execute";
            CurrentContext = context;
        }

        private const int Timeout = 120;
        private int top = 25;

        private string sortColumn = "total_cpu_time_ms";

        public async void RefreshData()
        {
            var objectName = string.Empty;
            int? objectId = null;
            if (!int.TryParse(txtObjectName.Text, out var objectIdResult))
            {
                objectName = txtObjectName.Text;
            }
            else
            {
                objectId = objectIdResult;
            }
            dgv.DataSource = null;
            var message = new QueryStoreMessage() { CollectAgent = CurrentContext.CollectAgent, ImportAgent = CurrentContext.ImportAgent, Top = top, SortColumn = sortColumn, ObjectName = objectName, ObjectID = objectId, NearestInterval = tsNearestInterval.Checked };
            message.ConnectionID = CurrentContext.InstanceName;
            message.DatabaseName = CurrentContext.DatabaseName;
            message.From = new DateTimeOffset(DateRange.FromUTC, TimeSpan.Zero);
            message.To = new DateTimeOffset(DateRange.ToUTC, TimeSpan.Zero);
            var messageGroup = Guid.NewGuid();
            await MessageProcessing.SendMessageToService(message.Serialize(), (int)CurrentContext.ImportAgentID, messageGroup, Common.ConnectionString, Timeout);
            var completed = false;
            while (!completed)
            {
                completed = true;
                ResponseMessage reply;
                try
                {
                    reply = await ReceiveReply(messageGroup, Timeout * 1000);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                switch (reply.Type)
                {
                    case ResponseMessage.ResponseTypes.Progress:
                        completed = false;
                        lblStatus.InvokeSetStatus(reply.Message, string.Empty, DashColors.Information);
                        break;

                    case ResponseMessage.ResponseTypes.Failure:
                        lblStatus.InvokeSetStatus(reply.Message, reply.Exception?.ToString(), DashColors.Fail);
                        break;

                    case ResponseMessage.ResponseTypes.Success:
                        {
                            lblStatus.InvokeSetStatus(reply.Message, string.Empty, DashColors.Success);
                            var ds = reply.Data;
                            if (ds == null || ds.Tables.Count == 0)
                            {
                                MessageBox.Show("No data returned");
                                return;
                            }
                            var dt = ds.Tables[0];
                            if (dt.Columns.Count == 0)
                            {
                                MessageBox.Show("No data returned");
                                return;
                            }
                            dgv.Columns.Clear();
                            dgv.AddColumns(dt, topQueriesResult);
                            dgv.DataSource = new DataView(dt, null, $"{sortColumn} DESC", DataViewRowState.CurrentRows);
                            dgv.LoadColumnLayout(topQueriesResult.ColumnLayout);
                            dgv.ApplyTheme();
                            break;
                        }
                }
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
                new KeyValuePair<string, PersistedColumnLayout>("DB", new PersistedColumnLayout() { Width = 150, Visible = true }),
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

        public static async Task<ResponseMessage> ReceiveReply(Guid group, int timeout)
        {
            await using var cn = new SqlConnection(Common.ConnectionString);
            await using var cmd = new SqlCommand("Messaging.ReceiveReplyFromServiceToGUI", cn)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 0 };
            cmd.Parameters.AddWithValue("@ConversationGroupID", group);
            cmd.Parameters.AddWithValue("@Timeout", timeout);
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

        private void Top_Select(object sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender;
            var topString = menuItem.Tag?.ToString();
            if (string.IsNullOrEmpty(topString))
            {
                CommonShared.ShowInputDialog(ref topString, "Enter top value");
            }

            if (!int.TryParse(topString, out top)) return;

            foreach (var item in tsTop.DropDownItems.OfType<ToolStripMenuItem>())
            {
                item.Checked = false;
            }
            tsTop.Text = $"Top {top}";
            menuItem.Checked = true;
        }

        private void Sort_Select(object sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender;
            sortColumn = menuItem.Tag?.ToString();
            foreach (var item in tsSort.DropDownItems.OfType<ToolStripMenuItem>())
            {
                item.Checked = false;
            }
            menuItem.Checked = true;
            tsSort.Text = $"Sort by {menuItem.Text}";
        }

        private void TsExcel_Click(object sender, EventArgs e)
        {
            Common.PromptSaveDataGridView(ref dgv);
        }

        private void TsCopy_Click(object sender, EventArgs e)
        {
            Common.CopyDataGridViewToClipboard(dgv);
        }
    }
}