namespace DBADashGUI.Performance
{
    partial class QueryStore
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(QueryStore));
            dgv = new System.Windows.Forms.DataGridView();
            toolStrip1 = new System.Windows.Forms.ToolStrip();
            TsCopy = new System.Windows.Forms.ToolStripButton();
            tsExcel = new System.Windows.Forms.ToolStripButton();
            tsExecute = new System.Windows.Forms.ToolStripButton();
            tsTop = new System.Windows.Forms.ToolStripDropDownButton();
            tsTop10 = new System.Windows.Forms.ToolStripMenuItem();
            tsTop25 = new System.Windows.Forms.ToolStripMenuItem();
            tsTop50 = new System.Windows.Forms.ToolStripMenuItem();
            tsTop100 = new System.Windows.Forms.ToolStripMenuItem();
            toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            tsTopCustom = new System.Windows.Forms.ToolStripMenuItem();
            tsSort = new System.Windows.Forms.ToolStripDropDownButton();
            totalCPUToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            avgCPUToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            totalDurationToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            avgDurationToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            executionCountToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            memoryGrantToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            physicalReadsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            avgPhysicalReadsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            txtObjectName = new System.Windows.Forms.ToolStripTextBox();
            tsOptions = new System.Windows.Forms.ToolStripDropDownButton();
            tsNearestInterval = new System.Windows.Forms.ToolStripMenuItem();
            statusStrip1 = new System.Windows.Forms.StatusStrip();
            lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            ((System.ComponentModel.ISupportInitialize)dgv).BeginInit();
            toolStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // dgv
            // 
            dgv.AllowUserToAddRows = false;
            dgv.AllowUserToDeleteRows = false;
            dgv.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgv.Dock = System.Windows.Forms.DockStyle.Fill;
            dgv.Location = new System.Drawing.Point(0, 27);
            dgv.Name = "dgv";
            dgv.ReadOnly = true;
            dgv.RowHeadersVisible = false;
            dgv.RowHeadersWidth = 51;
            dgv.Size = new System.Drawing.Size(1046, 472);
            dgv.TabIndex = 0;
            dgv.CellContentClick += Dgv_CellContentClick;
            // 
            // toolStrip1
            // 
            toolStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { TsCopy, tsExcel, tsExecute, tsTop, tsSort, toolStripLabel1, txtObjectName, tsOptions });
            toolStrip1.Location = new System.Drawing.Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new System.Drawing.Size(1046, 27);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // TsCopy
            // 
            TsCopy.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            TsCopy.Image = Properties.Resources.ASX_Copy_blue_16x;
            TsCopy.ImageTransparentColor = System.Drawing.Color.Magenta;
            TsCopy.Name = "TsCopy";
            TsCopy.Size = new System.Drawing.Size(29, 24);
            TsCopy.Text = "Copy";
            TsCopy.Click += TsCopy_Click;
            // 
            // tsExcel
            // 
            tsExcel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            tsExcel.Image = Properties.Resources.excel16x16;
            tsExcel.ImageTransparentColor = System.Drawing.Color.Magenta;
            tsExcel.Name = "tsExcel";
            tsExcel.Size = new System.Drawing.Size(29, 24);
            tsExcel.Text = "toolStripButton1";
            tsExcel.Click += TsExcel_Click;
            // 
            // tsExecute
            // 
            tsExecute.Image = Properties.Resources.ProjectSystemModelRefresh_16x;
            tsExecute.ImageTransparentColor = System.Drawing.Color.Magenta;
            tsExecute.Name = "tsExecute";
            tsExecute.Size = new System.Drawing.Size(84, 24);
            tsExecute.Text = "Execute";
            tsExecute.Click += toolStripButton1_Click;
            // 
            // tsTop
            // 
            tsTop.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            tsTop.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { tsTop10, tsTop25, tsTop50, tsTop100, toolStripMenuItem1, tsTopCustom });
            tsTop.Image = (System.Drawing.Image)resources.GetObject("tsTop.Image");
            tsTop.ImageTransparentColor = System.Drawing.Color.Magenta;
            tsTop.Name = "tsTop";
            tsTop.Size = new System.Drawing.Size(68, 24);
            tsTop.Text = "Top 25";
            // 
            // tsTop10
            // 
            tsTop10.Name = "tsTop10";
            tsTop10.Size = new System.Drawing.Size(142, 26);
            tsTop10.Tag = "10";
            tsTop10.Text = "10";
            tsTop10.Click += Top_Select;
            // 
            // tsTop25
            // 
            tsTop25.Checked = true;
            tsTop25.CheckState = System.Windows.Forms.CheckState.Checked;
            tsTop25.Name = "tsTop25";
            tsTop25.Size = new System.Drawing.Size(142, 26);
            tsTop25.Tag = "25";
            tsTop25.Text = "25";
            tsTop25.Click += Top_Select;
            // 
            // tsTop50
            // 
            tsTop50.Name = "tsTop50";
            tsTop50.Size = new System.Drawing.Size(142, 26);
            tsTop50.Tag = "50";
            tsTop50.Text = "50";
            tsTop50.Click += Top_Select;
            // 
            // tsTop100
            // 
            tsTop100.Name = "tsTop100";
            tsTop100.Size = new System.Drawing.Size(142, 26);
            tsTop100.Tag = "100";
            tsTop100.Text = "100";
            tsTop100.Click += Top_Select;
            // 
            // toolStripMenuItem1
            // 
            toolStripMenuItem1.Name = "toolStripMenuItem1";
            toolStripMenuItem1.Size = new System.Drawing.Size(139, 6);
            // 
            // tsTopCustom
            // 
            tsTopCustom.Name = "tsTopCustom";
            tsTopCustom.Size = new System.Drawing.Size(142, 26);
            tsTopCustom.Text = "Custom";
            tsTopCustom.Click += Top_Select;
            // 
            // tsSort
            // 
            tsSort.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            tsSort.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { totalCPUToolStripMenuItem, avgCPUToolStripMenuItem, totalDurationToolStripMenuItem, avgDurationToolStripMenuItem, executionCountToolStripMenuItem, memoryGrantToolStripMenuItem, physicalReadsToolStripMenuItem, avgPhysicalReadsToolStripMenuItem });
            tsSort.Image = (System.Drawing.Image)resources.GetObject("tsSort.Image");
            tsSort.ImageTransparentColor = System.Drawing.Color.Magenta;
            tsSort.Name = "tsSort";
            tsSort.Size = new System.Drawing.Size(138, 24);
            tsSort.Text = "Sort by Total CPU";
            // 
            // totalCPUToolStripMenuItem
            // 
            totalCPUToolStripMenuItem.Checked = true;
            totalCPUToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            totalCPUToolStripMenuItem.Name = "totalCPUToolStripMenuItem";
            totalCPUToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            totalCPUToolStripMenuItem.Tag = "total_cpu_time_ms";
            totalCPUToolStripMenuItem.Text = "Total CPU";
            totalCPUToolStripMenuItem.Click += Sort_Select;
            // 
            // avgCPUToolStripMenuItem
            // 
            avgCPUToolStripMenuItem.Name = "avgCPUToolStripMenuItem";
            avgCPUToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            avgCPUToolStripMenuItem.Tag = "avg_cpu_time_ms";
            avgCPUToolStripMenuItem.Text = "Avg CPU";
            avgCPUToolStripMenuItem.Click += Sort_Select;
            // 
            // totalDurationToolStripMenuItem
            // 
            totalDurationToolStripMenuItem.Name = "totalDurationToolStripMenuItem";
            totalDurationToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            totalDurationToolStripMenuItem.Tag = "total_duration_ms";
            totalDurationToolStripMenuItem.Text = "Total Duration";
            totalDurationToolStripMenuItem.Click += Sort_Select;
            // 
            // avgDurationToolStripMenuItem
            // 
            avgDurationToolStripMenuItem.Name = "avgDurationToolStripMenuItem";
            avgDurationToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            avgDurationToolStripMenuItem.Tag = "avg_duration_ms";
            avgDurationToolStripMenuItem.Text = "Avg Duration";
            avgDurationToolStripMenuItem.Click += Sort_Select;
            // 
            // executionCountToolStripMenuItem
            // 
            executionCountToolStripMenuItem.Name = "executionCountToolStripMenuItem";
            executionCountToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            executionCountToolStripMenuItem.Tag = "count_executions";
            executionCountToolStripMenuItem.Text = "Execution Count";
            executionCountToolStripMenuItem.Click += Sort_Select;
            // 
            // memoryGrantToolStripMenuItem
            // 
            memoryGrantToolStripMenuItem.Name = "memoryGrantToolStripMenuItem";
            memoryGrantToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            memoryGrantToolStripMenuItem.Tag = "max_memory_grant_kb";
            memoryGrantToolStripMenuItem.Text = "Max Memory Grant";
            memoryGrantToolStripMenuItem.Click += Sort_Select;
            // 
            // physicalReadsToolStripMenuItem
            // 
            physicalReadsToolStripMenuItem.Name = "physicalReadsToolStripMenuItem";
            physicalReadsToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            physicalReadsToolStripMenuItem.Tag = "total_physical_io_reads_kb";
            physicalReadsToolStripMenuItem.Text = "Total Physical Reads";
            physicalReadsToolStripMenuItem.Click += Sort_Select;
            // 
            // avgPhysicalReadsToolStripMenuItem
            // 
            avgPhysicalReadsToolStripMenuItem.Name = "avgPhysicalReadsToolStripMenuItem";
            avgPhysicalReadsToolStripMenuItem.Size = new System.Drawing.Size(225, 26);
            avgPhysicalReadsToolStripMenuItem.Tag = "avg_physical_io_reads_kb";
            avgPhysicalReadsToolStripMenuItem.Text = "Avg Physical Reads";
            avgPhysicalReadsToolStripMenuItem.Click += Sort_Select;
            // 
            // toolStripLabel1
            // 
            toolStripLabel1.Name = "toolStripLabel1";
            toolStripLabel1.Size = new System.Drawing.Size(121, 24);
            toolStripLabel1.Text = "Object Name/ID:";
            // 
            // txtObjectName
            // 
            txtObjectName.Name = "txtObjectName";
            txtObjectName.Size = new System.Drawing.Size(100, 27);
            // 
            // tsOptions
            // 
            tsOptions.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            tsOptions.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            tsOptions.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { tsNearestInterval });
            tsOptions.Image = Properties.Resources.SettingsOutline_16x;
            tsOptions.ImageTransparentColor = System.Drawing.Color.Magenta;
            tsOptions.Name = "tsOptions";
            tsOptions.Size = new System.Drawing.Size(34, 24);
            tsOptions.Text = "Options";
            // 
            // tsNearestInterval
            // 
            tsNearestInterval.Checked = true;
            tsNearestInterval.CheckOnClick = true;
            tsNearestInterval.CheckState = System.Windows.Forms.CheckState.Checked;
            tsNearestInterval.Name = "tsNearestInterval";
            tsNearestInterval.Size = new System.Drawing.Size(224, 26);
            tsNearestInterval.Text = "Use nearest interval";
            tsNearestInterval.ToolTipText = "Use the nearest query store interval.  Uncheck to filter on first/last execution time";
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { lblStatus });
            statusStrip1.Location = new System.Drawing.Point(0, 499);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new System.Drawing.Size(1046, 22);
            statusStrip1.TabIndex = 2;
            statusStrip1.Text = "statusStrip1";
            // 
            // lblStatus
            // 
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(0, 16);
            // 
            // QueryStore
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(dgv);
            Controls.Add(toolStrip1);
            Controls.Add(statusStrip1);
            Name = "QueryStore";
            Size = new System.Drawing.Size(1046, 521);
            ((System.ComponentModel.ISupportInitialize)dgv).EndInit();
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.DataGridView dgv;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton tsExecute;
        private System.Windows.Forms.ToolStripDropDownButton tsTop;
        private System.Windows.Forms.ToolStripMenuItem tsTop10;
        private System.Windows.Forms.ToolStripMenuItem tsTop25;
        private System.Windows.Forms.ToolStripMenuItem tsTop50;
        private System.Windows.Forms.ToolStripMenuItem tsTop100;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem tsTopCustom;
        private System.Windows.Forms.ToolStripDropDownButton tsSort;
        private System.Windows.Forms.ToolStripMenuItem totalCPUToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem avgCPUToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem totalDurationToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem avgDurationToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem executionCountToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem memoryGrantToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem physicalReadsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem avgPhysicalReadsToolStripMenuItem;
        private System.Windows.Forms.ToolStripButton tsExcel;
        private System.Windows.Forms.ToolStripButton TsCopy;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripLabel toolStripLabel1;
        private System.Windows.Forms.ToolStripTextBox txtObjectName;
        private System.Windows.Forms.ToolStripDropDownButton tsOptions;
        private System.Windows.Forms.ToolStripMenuItem tsNearestInterval;
    }
}
