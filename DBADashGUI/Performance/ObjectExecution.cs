﻿using DBADashGUI.Theme;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Data;
using System.Windows.Forms;

namespace DBADashGUI.Performance
{
    public partial class ObjectExecution : UserControl, IMetricChart
    {
        public ObjectExecution()
        {
            InitializeComponent();
        }

        public class DateModel
        {
            public DateTime DateTime { get; set; }
            public double Value { get; set; }
        }

        //public string measure = "TotalDuration";
        public DateTimePoint x;

        private int instanceID;
        private DateTime chartMaxDate = DateTime.MinValue;

        private int mins;
        private long objectID;
        private int databaseid;
        private int dateGrouping;

        public event EventHandler<EventArgs> Close;

        public event EventHandler<EventArgs> MoveUp;

        public bool CloseVisible
        {
            get => tsClose.Visible;
            set => tsClose.Visible = value;
        }

        public bool MoveUpVisible
        {
            get => tsUp.Visible;
            set => tsUp.Visible = value;
        }

        public ObjectExecutionMetric Metric { get; set; } = new();

        IMetric IMetricChart.Metric => Metric;

        public void RefreshData(int instanceID, long objectID, int databaseID)
        {
            this.instanceID = instanceID;
            if (mins != DateRange.DurationMins)
            {
                dateGrouping = DateHelper.DateGrouping(DateRange.DurationMins, 35);
                if (dateGrouping < 1)
                {
                    dateGrouping = 1;
                }
                tsDateGroup.Text = DateHelper.DateGroupString(dateGrouping);
                mins = DateRange.DurationMins;
            }
            this.objectID = objectID;

            databaseid = databaseID;
            RefreshData();
        }

        public void RefreshData(int InstanceID)
        {
            RefreshData(InstanceID, -1, -1);
        }

        public void RefreshData()
        {
            objectExecChart.Series.Clear();
            objectExecChart.AxisX.Clear();
            objectExecChart.AxisY.Clear();
            chartMaxDate = DateTime.MinValue;
            lblExecution.Text = databaseid > 0 ? "Execution Stats: Database" : "Execution Stats: Instance";
            toolStrip1.Tag = databaseid > 0 ? "ALT" : null; // set tag to ALT to use the alternate menu renderer
            toolStrip1.ApplyTheme(DBADashUser.SelectedTheme);
            var dt = CommonData.ObjectExecutionStats(instanceID, databaseid, objectID, dateGrouping, Metric.Measure, DateRange.FromUTC, DateRange.ToUTC);

            if (dt.Rows.Count == 0)
            {
                return;
            }
            var dPoints = new Dictionary<string, ChartValues<DateTimePoint>>();
            var current = string.Empty;
            ChartValues<DateTimePoint> values = new();
            foreach (DataRow r in dt.Rows)
            {
                var objectName = (string)r["DatabaseName"] + " | " + (string)r["object_name"];
                var time = (DateTime)r["SnapshotDate"];
                if (time > chartMaxDate)
                {
                    chartMaxDate = time;
                }
                if (current != objectName)
                {
                    if (values.Count > 0) { dPoints.Add(current, values); }
                    values = new ChartValues<DateTimePoint>();
                    current = objectName;
                }
                values.Add(new DateTimePoint(((DateTime)r["SnapshotDate"]), Convert.ToDouble(r["Measure"])));
            }
            if (values.Count > 0)
            {
                dPoints.Add(current, values);
                values = new ChartValues<DateTimePoint>();
            }

            var dayConfig = Mappers.Xy<DateTimePoint>()
.X(dateModel => dateModel.DateTime.Ticks / TimeSpan.FromMinutes(dateGrouping == 0 ? 1 : dateGrouping).Ticks)
.Y(dateModel => dateModel.Value);

            SeriesCollection s1 = new(dayConfig);
            foreach (var x in dPoints)
            {
                s1.Add(new StackedColumnSeries
                {
                    Title = x.Key,
                    Values = x.Value
                });
            }
            objectExecChart.Series = s1;

            var format = "t";
            if (dateGrouping >= 1440)
            {
                format = "yyyy-MM-dd";
            }
            else if (mins >= 1440)
            {
                format = "yyyy-MM-dd HH:mm";
            }
            objectExecChart.AxisX.Add(new Axis
            {
                LabelFormatter = value => new DateTime((long)(value * TimeSpan.FromMinutes(dateGrouping == 0 ? 1 : dateGrouping).Ticks)).ToString(format)
            });

            objectExecChart.AxisY.Add(new Axis
            {
                LabelFormatter = val => val.ToString(measures[Metric.Measure].LabelFormat)
            });
        }

        private class Measure
        {
            public string Name { get; set; }
            public string DisplayName { get; init; }
            public string LabelFormat { get; init; }
        }

        private class Measures : Dictionary<string, Measure>
        {
            public void Add(string Name, string displayName, string labelFormat)
            {
                Add(Name, new Measure() { Name = Name, DisplayName = displayName, LabelFormat = labelFormat });
            }
        }

        private readonly Measures measures = new()
            {
                {"TotalDuration", "Total Duration","#,##0.000 sec"},
                {"AvgDuration", "Avg Duration","#,##0.000 sec"},
                {"TotalCPU", "Total CPU","#,##0.000 sec"},
                {"AvgCPU","Avg CPU", "#,##0.000  sec"},
                {"ExecutionCount", "Execution Count","N0"},
                {"ExecutionsPerMin", "Executions Per Min","#,##0.000"},
                {"MaxExecutionsPerMin", "Max Executions Per Min","#,##0.000"},
                { "TotalLogicalReads","Total Logical Reads","N0" },
                {"AvgLogicalReads","Avg Logical Reads","N0" },
                {"TotalPhysicalReads","TotalPhysicalReads" ,"N0"},
                {"AvgPhysicalReads","Avg Physical Reads" ,"N0"},
                {"TotalWrites","Total Writes" ,"N0"},
                {"AvgWrites","Avg Writes","N0" }
            };

        private void ObjectExecution_Load(object sender, EventArgs e)
        {
            DateHelper.AddDateGroups(tsDateGroup, TsDateGrouping_Click);
            foreach (var m in measures)
            {
                ToolStripMenuItem itm = new(m.Value.DisplayName)
                {
                    Name = m.Key
                };
                if (m.Key == Metric.Measure)
                {
                    itm.Checked = true;
                    tsMeasures.Text = m.Value.DisplayName;
                }

                itm.Click += Itm_Click;
                tsMeasures.DropDownItems.Add(itm);
            }
        }

        private void TsDateGrouping_Click(object sender, EventArgs e)
        {
            var ts = (ToolStripMenuItem)sender;
            dateGrouping = Convert.ToInt32(ts.Tag);
            tsDateGroup.Text = DateHelper.DateGroupString(dateGrouping);
            RefreshData();
        }

        private void Itm_Click(object sender, EventArgs e)
        {
            var tsItm = ((ToolStripMenuItem)sender);
            if (Metric.Measure != tsItm.Name)
            {
                Metric.Measure = tsItm.Name;
                foreach (ToolStripMenuItem itm in tsMeasures.DropDownItems)
                {
                    itm.Checked = itm.Name == Metric.Measure;
                }
                tsMeasures.Text = tsItm.Text;
            }
            RefreshData(instanceID, objectID, databaseid);
        }

        private void TsClose_Click(object sender, EventArgs e)
        {
            Close?.Invoke(this, EventArgs.Empty);
        }

        private void TsUp_Click(object sender, EventArgs e)
        {
            MoveUp?.Invoke(this, EventArgs.Empty);
        }
    }
}