using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace LakeShoreControl
{
    public sealed class MainForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(24, 43, 66), Blue = Color.FromArgb(30, 92, 184), Teal = Color.FromArgb(0, 137, 129), Muted = Color.FromArgb(104, 120, 139);
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly ComboBox port = Combo(), source = Combo(), input = Combo(), output = Combo(), heaterRange = Combo(), manualUnit = Combo(), ending = Combo();
        private readonly TextBox target = Box("245"), speed = Box("1"), tolerance = Box("0.5"), stability = Box("10");
        private readonly Label current = Metric(), celsius = Small(""), setpoint = Metric(), heater = Metric(), deviceLabel = Small("未连接设备"), status = Small("连接后开始只读监测。"), runDetail = Small("尚未运行温度程序。"), sensorLabel = Small("A —    B —    C —    D —"), footer = Small(""), logPathLabel = Small("");
        private readonly DataGridView grid = new DataGridView();
        private readonly Chart chart = new Chart();
        private readonly TextBox events = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
        private Button connect, refresh, start, restart, manual, pause, stop, allOff, add, remove, generate, import, save;
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Top, Height = 7, Style = ProgressBarStyle.Continuous };
        private ControlSession session;
        private StreamWriter csv, eventFile;
        private TemperatureLog temperatureLog;
        private readonly TableLayoutPanel overview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 7, Padding = new Padding(2) };
        private string csvPath;
        private bool busy, faulted, closing, stopRequested;
        private readonly string initialPort;
        private readonly bool initialDemo;
        private string lastMessage = "";
        private readonly Label controlState = Small("等待读取仪器状态");
        private readonly TextBox pointInterval = Box("35");
        private readonly Label intervalSummary = Small("填写分钟数，点击应用后更新全部温度点。");
        private Button applyInterval;
        private bool editedWhilePaused;
        private bool changingLanguage;
        private readonly ComboBox language = Combo();
        private readonly System.Collections.Generic.List<string> eventLines = new System.Collections.Generic.List<string>();
        private bool CanEditProgram { get { return !busy && (session == null || !session.Owned || session.Engine.State == RunState.Paused); } }

        public MainForm(string autoPort, bool demo, string recipePath, string pausedPath = null)
        {
            initialPort = autoPort; initialDemo = demo;
            Text = "Lake Shore 336 · 温度控制"; Font = new Font("Segoe UI", 9F);
            ForeColor = Ink; BackColor = Color.FromArgb(242, 246, 250); AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1060, 760); StartPosition = FormStartPosition.CenterScreen;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1420, area.Width - 40), Math.Min(980, area.Height - 50));

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(20, 12, 20, 8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);
            Panel header = new Panel { Dock = DockStyle.Fill };
            Label title = new Label { Text = "Lake Shore 336", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0), ForeColor = Ink };
            Label subtitle = Small("温度控制  /  TEMPERATURE STUDIO"); subtitle.Location = new Point(3, 35); subtitle.AutoSize = true;
            header.Controls.Add(title); header.Controls.Add(subtitle);
            language.Items.AddRange(new object[] { "English", "中文" });
            language.SelectedIndex = UiText.English ? 0 : 1;
            language.Width = 120; language.AccessibleName = "Language / 语言";
            language.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(language);
            header.Resize += (s, e) => language.Location = new Point(Math.Max(275, header.ClientSize.Width - 345), 8);
            language.SelectedIndexChanged += (s, e) => {
                try { UiText.SetLanguage(language.SelectedIndex == 0, true); }
                catch (Exception error) { UiMessageBox.Show(this, "Language changed, but the preference could not be saved: " + error.Message, "Language preference"); }
            };
            allOff = ButtonOf("全部输出关闭 · All Off", Color.FromArgb(177, 50, 50)); allOff.Width = 210; allOff.Dock = DockStyle.Right;
            allOff.Click += async (s, e) => await StopAsync(true);
            header.Controls.Add(allOff); root.Controls.Add(header, 0, 0);

            FlowLayoutPanel connection = Flow(); connection.WrapContents = false;
            source.Items.AddRange(new object[] { "真实设备", "演示设备" }); source.SelectedIndex = 0; source.Width = 110;
            port.Width = 92; refresh = ButtonOf("刷新", Color.White); refresh.Width = 80;
            connect = ButtonOf("连接", Blue); connect.Width = 100;
            input.Items.AddRange(new object[] { "A", "B", "C", "D" }); input.SelectedIndex = 0; input.Width = 55;
            output.Items.AddRange(new object[] { "1", "2" }); output.SelectedIndex = 0; output.Width = 55;
            connection.Controls.AddRange(new Control[] { source, port, refresh, connect, Inline("控制输入"), input, Inline("加热输出"), output, deviceLabel });
            deviceLabel.AutoSize = true; deviceLabel.Margin = new Padding(12, 9, 0, 0);
            refresh.Click += (s, e) => RefreshPorts();
            connect.Click += async (s, e) => await ConnectAsync();
            input.SelectedIndexChanged += async (s, e) => await ChangeChannelAsync();
            output.SelectedIndexChanged += async (s, e) => await ChangeChannelAsync();
            root.Controls.Add(connection, 0, 1);

            TableLayoutPanel metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 0, 0, 12) };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            metrics.Controls.Add(MetricCard("实测温度", current, celsius), 0, 0);
            metrics.Controls.Add(MetricCard("仪器当前设定值", setpoint, controlState), 1, 0);
            metrics.Controls.Add(MetricCard("当前加热输出", heater, sensorLabel), 2, 0); root.Controls.Add(metrics, 0, 2);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
            root.Controls.Add(body, 0, 3);
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 12, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); body.Controls.Add(left, 0, 0);

            Panel programPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel programLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            programLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); programLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            programLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); programLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            programLayout.Controls.Add(Heading("温度程序"), 0, 0);
            FlowLayoutPanel toolbar = Flow(); toolbar.WrapContents = false;
            add = ButtonOf("＋ 添加", Color.White); remove = ButtonOf("删除", Color.White); generate = ButtonOf("等步长生成", Color.White); import = ButtonOf("导入", Color.White); save = ButtonOf("保存", Color.White);
            add.Width = 80; remove.Width = 78; generate.Width = 105; import.Width = 78; save.Width = 70;
            toolbar.Controls.AddRange(new Control[] { add, remove, generate, import, save });
            add.Click += (s, e) => AddStage(new Stage { TargetK = grid.Rows.Count == 0 ? 245 : ReadLastTarget() });
            remove.Click += (s, e) => { if (grid.CurrentRow != null && !grid.ReadOnly) grid.Rows.Remove(grid.CurrentRow); };
            generate.Click += (s, e) => GenerateStages(); import.Click += (s, e) => ImportRecipe(); save.Click += (s, e) => SaveRecipe();
            programLayout.Controls.Add(toolbar, 0, 1); ConfigureGrid();
            TabControl programTabs = new TabControl { Dock = DockStyle.Fill };
            TabPage allPoints = new TabPage("全部温度点"), editPoints = new TabPage("编辑程序表（可滚动）");
            for (int c = 0; c < 4; c++) overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            for (int r = 0; r < 7; r++) overview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 7));
            allPoints.Controls.Add(overview); editPoints.Controls.Add(grid); programTabs.TabPages.Add(allPoints); programTabs.TabPages.Add(editPoints); programLayout.Controls.Add(programTabs, 0, 2);
            grid.RowsAdded += (s, e) => ProgramEdited(); grid.RowsRemoved += (s, e) => ProgramEdited(); grid.CellValueChanged += (s, e) => ProgramEdited();
            Label recipeHint = Small("定时阶跃：设定目标后立即计时，不等待到温。其余方式：实测到温并稳定后计时。保持可填分钟或分:秒，例如 24:28。");
            recipeHint.Dock = DockStyle.Fill; recipeHint.Padding = new Padding(0, 6, 0, 0); programLayout.Controls.Add(recipeHint, 0, 3);
            programPanel.Controls.Add(programLayout); left.Controls.Add(programPanel, 0, 0);

            TabControl tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
            TabPage trace = new TabPage("实时曲线") { BackColor = Color.White, Padding = new Padding(5) };
            TabPage journal = new TabPage("操作记录") { BackColor = Color.White, Padding = new Padding(10) };
            ConfigureChart(); trace.Controls.Add(chart); journal.Controls.Add(events); tabs.TabPages.Add(trace); tabs.TabPages.Add(journal); left.Controls.Add(tabs, 0, 1);

            Panel right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(14) }; body.Controls.Add(right, 1, 0);
            TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 0 };
            right.Controls.Add(settings);
            applyInterval = ButtonOf("应用到全部点", Blue);
            applyInterval.Click += (s, e) => ApplyPointInterval();
            AddRow(settings, Pair("每点保持 min / 分:秒", pointInterval, "统一调整并保存", applyInterval), 54);
            intervalSummary.Dock = DockStyle.Fill; AddRow(settings, intervalSummary, 40);
            AddRow(settings, Heading("控温设置"), 27);
            heaterRange.Items.AddRange(new object[] { "低 · Low", "中 · Medium", "高 · High" }); heaterRange.SelectedIndex = 0;
            AddRow(settings, Pair("加热档位", heaterRange, "到温容差 K", tolerance), 54);
            ending.Items.AddRange(new object[] { "完成后关闭输出", "完成后保持末温" }); ending.SelectedIndex = 1;
            AddRow(settings, Pair("到温确认 秒", stability, "程序结束动作", ending), 54);
            AddRow(settings, Heading("手动控温"), 30);
            manualUnit.Items.AddRange(new object[] { "K", "°C" }); manualUnit.SelectedIndex = 0; manualUnit.Width = 62;
            TableLayoutPanel targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            target.Dock = manualUnit.Dock = DockStyle.Fill; targetRow.Controls.Add(target, 0, 0); targetRow.Controls.Add(manualUnit, 1, 0);
            AddRow(settings, Pair("目标温度", targetRow, "变化速率 K/min", speed), 54);
            manual = ButtonOf("应用手动目标", Blue); manual.Dock = DockStyle.Fill; manual.Click += async (s, e) => await StartAsync(true);
            AddRow(settings, manual, 40);
            AddRow(settings, Heading("程序运行"), 33);
            FlowLayoutPanel actions = Flow(); actions.WrapContents = false;
            start = ButtonOf("开始程序", Teal); pause = ButtonOf("暂停", Color.White); stop = ButtonOf("关闭加热", Color.White);
            start.Width = 86; pause.Width = 80; stop.Width = 106;
            start.Click += async (s, e) => await StartAsync(false); pause.Click += async (s, e) => await PauseAsync(); stop.Click += async (s, e) => await StopAsync(false);
            actions.Controls.AddRange(new Control[] { start, pause, stop }); AddRow(settings, actions, 43);
            restart = ButtonOf("↻ 重新开始 / Refresh", Blue); restart.Dock = DockStyle.Fill;
            restart.Click += async (s, e) => await RestartAsync(); AddRow(settings, restart, 38);
            AddRow(settings, Small("回到第 1 点，计时清零并运行；保留全部数据。"), 40);
            status.Font = new Font(Font, FontStyle.Bold); status.ForeColor = Teal; status.Dock = DockStyle.Fill; AddRow(settings, status, 48);
            AddRow(settings, progress, 13); runDetail.Dock = DockStyle.Fill; AddRow(settings, runDetail, 78);
            Button openLogs = ButtonOf("打开数据记录文件夹", Color.White); openLogs.Dock = DockStyle.Fill;
            openLogs.Click += (s, e) => { string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temperature_data"); Directory.CreateDirectory(folder); Process.Start("explorer.exe", "\"" + folder + "\""); };
            AddRow(settings, openLogs, 38); logPathLabel.Dock = DockStyle.Fill; AddRow(settings, logPathLabel, 40);
            Label controlHint = Small("软件梯度需保持程序运行和电脑唤醒。断线或强制退出后，仪器可能保持最后设定值；必要时按面板 All Off。制冷能力决定实际降温速度。"); controlHint.Dock = DockStyle.Fill; AddRow(settings, controlHint, 74);
            footer.Dock = DockStyle.Fill; footer.Text = "USB 57600 · 7O1    |    约每 2 秒采样    |    曲线与记录统一使用 K"; root.Controls.Add(footer, 0, 4);
            RefreshPorts(); AddStage(new Stage { TargetK = 245 }); AddStage(new Stage { TargetK = 250, HoldMinutes = 10 });
            timer.Interval = 2000; timer.Tick += async (s, e) => await PollAsync();
            Shown += async (s, e) =>
            {
                if (!string.IsNullOrEmpty(recipePath))
                {
                    try { LoadRecipe(recipePath); } catch (Exception error) { UiMessageBox.Show(this, error.Message, "温度程序加载失败"); }
                }
                if (initialDemo) source.SelectedIndex = 1;
                if (!string.IsNullOrEmpty(initialPort)) port.Text = initialPort;
                if (initialDemo || !string.IsNullOrEmpty(initialPort)) await ConnectAsync();
                if (session != null && !string.IsNullOrEmpty(pausedPath)) await RestorePausedAsync(pausedPath);
            };
            FormClosing += OnClosing; UpdateEnabled();
            UiText.Bind(this);
            UiText.LanguageChanged += ApplyLanguage;
            Disposed += (s, e) => UiText.LanguageChanged -= ApplyLanguage;
            ApplyLanguage();
        }

        private static void TranslateChoices(ComboBox combo, string[] values)
        {
            int selected = combo.SelectedIndex;
            combo.DisplayMember = "Label"; combo.ValueMember = "Value";
            combo.DataSource = values.Select(value => new LanguageChoice(value)).ToList();
            combo.SelectedIndex = selected;
        }
        private void ApplyLanguage()
        {
            changingLanguage = true;
            try
            {
                SuspendLayout();
                TranslateChoices(source, new[] { "真实设备", "演示设备" });
                TranslateChoices(heaterRange, new[] { "低 · Low", "中 · Medium", "高 · High" });
                TranslateChoices(ending, new[] { "完成后关闭输出", "完成后保持末温" });
                string[] headers = { "目标 K", "变化方式", "梯度值", "保持 min / 分:秒", "总超时 min" };
                for (int i = 0; i < headers.Length; i++) grid.Columns[i].HeaderText = UiText.T(headers[i]);
                DataGridViewComboBoxColumn mode = (DataGridViewComboBoxColumn)grid.Columns[1];
                mode.DisplayMember = "Label"; mode.ValueMember = "Value";
                mode.DataSource = new[] { "速率", "时间", "阶跃", "定时阶跃" }.Select(value => new LanguageChoice(value)).ToList();
                chart.ChartAreas[0].AxisY.Title = UiText.T("温度 / K");
                chart.Series[0].LegendText = UiText.T("实测温度"); chart.Series[1].LegendText = UiText.T("仪器设定值");
                events.Text = string.Join(Environment.NewLine, eventLines.Select(UiText.T)) + (eventLines.Count > 0 ? Environment.NewLine : "");
                events.SelectionStart = events.TextLength; events.ScrollToCaret();
                grid.Invalidate(); chart.Invalidate();
            }
            finally { ResumeLayout(true); changingLanguage = false; }
        }

        private static TextBox Box(string text) { return new TextBox { Text = text, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 2, 6, 2) }; }
        private static ComboBox Combo() { return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 4, 7, 2) }; }
        private static Label Small(string text) { return new Label { Text = text, ForeColor = Muted, AutoSize = false }; }
        private static Label Inline(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(8, 8, 1, 0) }; }
        private static Label Metric() { return new Label { Text = "—", AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Ink }; }
        private static Label Heading(string text) { return new Label { Text = text, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Ink }; }
        private static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) }; }
        private static Button ButtonOf(string text, Color color)
        {
            Button b = new Button { Text = text, Height = 32, Width = 100, BackColor = color, ForeColor = color == Color.White ? Ink : Color.White,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 6, 2), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = color == Color.White ? Color.FromArgb(216, 224, 235) : color;
            return b;
        }
        private static Control MetricCard(string title, Label number, Label detail)
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 8, 8, 6), Margin = new Padding(0, 0, 10, 0), RowCount = 3, ColumnCount = 1 };
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); p.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
            Label label = Small(title); label.Dock = DockStyle.Fill; detail.Dock = DockStyle.Fill;
            p.Controls.Add(label, 0, 0); p.Controls.Add(number, 0, 1); p.Controls.Add(detail, 0, 2); return p;
        }
        private static Control Pair(string leftText, Control left, string rightText, Control right)
        {
            TableLayoutPanel pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pair.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label l = Small(leftText), r = Small(rightText); l.Dock = DockStyle.Fill; r.Dock = DockStyle.Fill;
            left.Dock = DockStyle.Fill; right.Dock = DockStyle.Fill;
            left.AccessibleName = leftText; right.AccessibleName = rightText;
            pair.Controls.Add(l, 0, 0); pair.Controls.Add(r, 1, 0); pair.Controls.Add(left, 0, 1); pair.Controls.Add(right, 1, 1); return pair;
        }
        private static void AddRow(TableLayoutPanel panel, Control control, int height)
        {
            int row = panel.RowCount++; panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); control.Dock = DockStyle.Fill; panel.Controls.Add(control, 0, row);
        }
        private void ConfigureGrid()
        {
            grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersWidth = 42; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38; grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(233, 240, 248); grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 237, 249); grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.DefaultCellStyle.Padding = new Padding(4); grid.RowTemplate.Height = 33; grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "target", HeaderText = "目标 K", FillWeight = 95, SortMode = DataGridViewColumnSortMode.NotSortable });
            DataGridViewComboBoxColumn mode = new DataGridViewComboBoxColumn { Name = "mode", HeaderText = "变化方式", FillWeight = 100, FlatStyle = FlatStyle.Flat };
            mode.Items.AddRange("速率", "时间", "阶跃", "定时阶跃"); grid.Columns.Add(mode);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "梯度值", FillWeight = 80, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "hold", HeaderText = "保持 min / 分:秒", FillWeight = 85, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "timeout", HeaderText = "总超时 min", FillWeight = 100, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.RowPostPaint += (s, e) => TextRenderer.DrawText(e.Graphics, (e.RowIndex + 1).ToString(), Font, new Rectangle(e.RowBounds.Left, e.RowBounds.Top, 36, e.RowBounds.Height), Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            grid.DataError += (s, e) => { e.ThrowException = false; };
        }
        private void ConfigureChart()
        {
            chart.Dock = DockStyle.Fill; chart.BackColor = Color.White;
            ChartArea area = new ChartArea("Temperature"); area.BackColor = Color.White;
            area.AxisX.LabelStyle.Format = "HH:mm:ss"; area.AxisX.MajorGrid.LineColor = Color.FromArgb(237, 242, 246); area.AxisY.MajorGrid.LineColor = Color.FromArgb(237, 242, 246);
            area.AxisY.Title = "温度 / K"; area.AxisY.IsStartedFromZero = false; area.AxisY.LabelStyle.Format = "0.00";
            area.AxisX.LineColor = area.AxisY.LineColor = Color.FromArgb(215, 222, 230); chart.ChartAreas.Add(area);
            chart.Legends.Add(new Legend { Docking = Docking.Top, Alignment = StringAlignment.Far, Font = Font });
            chart.Series.Add(new Series("实测温度") { ChartType = SeriesChartType.FastLine, XValueType = ChartValueType.DateTime, Color = Teal, BorderWidth = 2 });
            chart.Series.Add(new Series("仪器设定值") { ChartType = SeriesChartType.FastLine, XValueType = ChartValueType.DateTime, Color = Color.FromArgb(230, 151, 49), BorderWidth = 2 });
        }
        private void ProgramEdited()
        {
            if (changingLanguage) return;
            RefreshOverview();
            if (session != null && session.Owned && session.Engine.State == RunState.Paused)
            {
                editedWhilePaused = true;
                intervalSummary.Text = "程序已修改；点击重新开始执行新方案。";
            }
        }

        private void RefreshOverview()
        {
            overview.SuspendLayout(); foreach (Control old in overview.Controls.Cast<Control>().ToArray()) old.Dispose(); overview.Controls.Clear();
            int rows = Math.Max(7, (grid.Rows.Count + 3) / 4); overview.RowCount = rows; overview.RowStyles.Clear();
            for (int r = 0; r < rows; r++) overview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
            for (int n = 0; n < grid.Rows.Count; n++) {
                DataGridViewRow row = grid.Rows[n]; string text = (n + 1).ToString("00") + "   " + Convert.ToString(row.Cells[0].Value) + " K   / " + Convert.ToString(row.Cells[3].Value).Replace(":", "分") + (Convert.ToString(row.Cells[3].Value).Contains(":") ? "秒" : " min");
                Label label = new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false, Margin = new Padding(2), ForeColor = Ink, BackColor = n % 2 == 0 ? Color.FromArgb(233, 245, 244) : Color.White, Font = new Font(Font.FontFamily, 8.5F) };
                overview.Controls.Add(label, n / rows, n % rows);
            } overview.ResumeLayout();
        }
        private void RefreshPorts()
        {
            string selected = port.Text; port.Items.Clear(); port.Items.AddRange(SerialPort.GetPortNames().OrderBy(x => x).Cast<object>().ToArray());
            if (port.Items.Contains(selected)) port.SelectedItem = selected;
            else if (port.Items.Contains("COM6")) port.SelectedItem = "COM6";
            else if (port.Items.Count > 0) port.SelectedIndex = 0;
        }
        private void AddStage(Stage s) { grid.Rows.Add(Numbers.F(s.TargetK), s.Mode == "TimedStep" ? "定时阶跃" : s.Mode == "Step" ? "阶跃" : s.Mode == "Duration" ? "时间" : "速率", (s.Mode == "Step" || s.Mode == "TimedStep") ? "—" : s.Value.ToString("0.###", Numbers.Invariant), Numbers.FormatMinutes(s.HoldMinutes), s.TimeoutMinutes.ToString("0.###", Numbers.Invariant)); }
        private double ReadLastTarget() { try { return Numbers.Parse(Convert.ToString(grid.Rows[grid.Rows.Count - 1].Cells[0].Value)); } catch { return 245; } }
        private List<Stage> ReadStages()
        {
            grid.EndEdit(); List<Stage> stages = new List<Stage>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                try
                {
                    string mode = Convert.ToString(row.Cells[1].Value);
                    stages.Add(new Stage { TargetK = Numbers.Parse(Convert.ToString(row.Cells[0].Value)), Mode = mode == "定时阶跃" ? "TimedStep" : mode == "阶跃" ? "Step" : mode == "时间" ? "Duration" : "Rate",
                        Value = (mode == "阶跃" || mode == "定时阶跃") ? 0 : Numbers.Parse(Convert.ToString(row.Cells[2].Value)), HoldMinutes = Numbers.ParseMinutes(Convert.ToString(row.Cells[3].Value)), TimeoutMinutes = Numbers.Parse(Convert.ToString(row.Cells[4].Value)) });
                }
                catch (Exception e) { throw new ArgumentException("第 " + (row.Index + 1) + " 行：" + e.Message); }
            }
            return stages;
        }
        private Limits ReadLimits() { return Limits.ForInstrument(Numbers.Parse(tolerance.Text), Numbers.Parse(stability.Text)); }
        private void UpdateEnabled()
        {
            bool connected = session != null, owned = connected && session.Owned;
            connect.Text = connected ? "断开" : "连接"; connect.Enabled = !busy && !owned;
            source.Enabled = port.Enabled = refresh.Enabled = !busy && !connected;
            input.Enabled = output.Enabled = !busy && !owned; heaterRange.Enabled = !busy && !owned;
            start.Enabled = manual.Enabled = connected && !busy && !owned && !faulted;
            restart.Enabled = connected && !busy && !stopRequested && !faulted && grid.Rows.Count > 0;
            pause.Enabled = connected && owned && !busy && session.Engine.State != RunState.Completed && !editedWhilePaused;
            pause.Text = connected && session.Engine.State == RunState.Paused ? "继续" : "暂停";
            stop.Enabled = allOff.Enabled = connected && !stopRequested;
            grid.ReadOnly = !CanEditProgram; add.Enabled = remove.Enabled = generate.Enabled = import.Enabled = CanEditProgram; save.Enabled = !busy;
            tolerance.Enabled = stability.Enabled = ending.Enabled = target.Enabled = speed.Enabled = manualUnit.Enabled = !busy && !owned;
            pointInterval.Enabled = applyInterval.Enabled = CanEditProgram;
        }

        private void ApplyPointInterval()
        {
            if (!CanEditProgram) return;
            try
            {
                double minutes = Numbers.ParseMinutes(pointInterval.Text);
                if (minutes <= 0 || minutes > 10080) throw new ArgumentException("每点保持时间需大于 0 且不超过 10080 分钟，支持小数或分:秒（如 24:28）。");
                List<Stage> stages = ReadStages();
                if (stages.Count == 0) throw new ArgumentException("请先添加或导入温度点。");
                for (int n = 0; n < stages.Count; n++)
                {
                    if (minutes >= stages[n].TimeoutMinutes) throw new ArgumentException("第 " + (n + 1) + " 点的总超时为 " + stages[n].TimeoutMinutes + " 分钟。请先在编辑程序表中增大总超时，再设置保持时间。");
                    stages[n].HoldMinutes = minutes;
                }
                Recipe recipe = new Recipe { Version = stages.Any(s => s.Mode == "TimedStep") ? 3 : stages.Any(s => s.Mode == "Step") ? 2 : 1,
                    Stages = stages, ToleranceK = Numbers.Parse(tolerance.Text), StableSeconds = Numbers.Parse(stability.Text), HoldAtEnd = ending.SelectedIndex == 1 };
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recipes"); Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "默认温度程序.json");
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(recipe), new System.Text.UTF8Encoding(true));
                foreach (DataGridViewRow row in grid.Rows) row.Cells[3].Value = Numbers.FormatMinutes(minutes);
                intervalSummary.Text = "已应用：每点 " + Numbers.FormatMinutes(minutes) + "（min 或分:秒）；总计 " + (stages.Count * minutes / 60).ToString("0.##", Numbers.Invariant) + " 小时。";
                if (editedWhilePaused) intervalSummary.Text = "已修改并保存；点击重新开始执行新方案。";
                Note("全部 " + stages.Count + " 点保持时间更新为 " + minutes + " 分钟，已保存默认程序；" + (editedWhilePaused ? "当前仍暂停，点击重新开始应用新方案。" : "尚未启动序列。"));
            }
            catch (Exception error) { UiMessageBox.Show(this, error.Message, "检查每点保持时间", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private async Task RestorePausedAsync(string path)
        {
            busy = true; UpdateEnabled(); await gate.WaitAsync();
            try
            {
                PausedRun saved = new JavaScriptSerializer().Deserialize<PausedRun>(File.ReadAllText(path));
                await Task.Run(() => session.RestorePaused(saved));
                grid.Rows.Clear(); foreach (Stage stage in saved.Recipe.Stages) AddStage(stage);
                input.SelectedIndex = saved.Input - 1; output.SelectedIndex = saved.Output - 1; heaterRange.SelectedIndex = saved.Range - 1;
                ending.SelectedIndex = saved.Recipe.HoldAtEnd ? 1 : 0;
                editedWhilePaused = false; intervalSummary.Text = "已暂停，可修改；修改后点击重新开始。";
                RenderSnapshot(session.Last); Note(session.Message + "；恢复已计时 " + saved.HoldSeconds + " 秒，未发送仪器控制命令。");
                timer.Start(); NativeMethods.KeepAwake(true);
            }
            catch (Exception error) { UiMessageBox.Show(this, "未恢复暂停程序：" + error.Message, "暂停状态恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { gate.Release(); busy = false; UpdateEnabled(); }
        }

        private async Task ConnectAsync()
        {
            if (busy) return;
            if (session != null && session.ShutdownUnconfirmed)
            { UiMessageBox.Show(this, "上次关闭输出未确认。请先在仪器面板按 All Off，再使用“全部输出关闭”重新确认。", "关闭状态未确认", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            busy = true; timer.Stop(); UpdateEnabled(); await gate.WaitAsync();
            try
            {
                if (session != null)
                {
                    session.Dispose(); session = null; CloseLogs(); status.Text = "已断开 · 仪器保留原有设置"; deviceLabel.Text = "未连接设备"; current.Text = setpoint.Text = heater.Text = "—";
                }
                else
                {
                    bool demo = source.SelectedIndex == 1; string com = port.Text; int selectedOutput = output.SelectedIndex + 1, selectedInput = input.SelectedIndex + 1;
                    if (!demo && string.IsNullOrWhiteSpace(com)) throw new ArgumentException("没有可用串口，请确认 USB 已接入并在面板启用。");
                    IInstrument instrument = await Task.Run<IInstrument>(() => demo ? (IInstrument)new SimInstrument() : new SerialInstrument(com));
                    session = new ControlSession(instrument) { Output = selectedOutput, Input = selectedInput };
                    OpenLogs(demo); faulted = false; lastMessage = "";
                    foreach (Series series in chart.Series) series.Points.Clear();
                    deviceLabel.Text = demo ? "演示 · 无硬件连接" : "336 / " + com;
                    Text = demo ? "Lake Shore 336 · 演示模式（无硬件）" : "Lake Shore 336 · 温度控制";
                    Note("连接成功：" + instrument.Identity + "；未发送控温设置。");
                    Snapshot s = await Task.Run(() => session.Poll()); if (s.Range >= 1 && s.Range <= 3) heaterRange.SelectedIndex = s.Range - 1; RenderSnapshot(s); timer.Start();
                }
            }
            catch (Exception e)
            {
                if (session != null) { session.Dispose(); session = null; } CloseLogs();
                status.Text = "连接失败"; UiMessageBox.Show(this, e.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { gate.Release(); busy = false; UpdateEnabled(); }
        }
        private async Task ChangeChannelAsync()
        {
            if (session == null || busy || session.Owned) return;
            busy = true; UpdateEnabled(); await gate.WaitAsync();
            try
            {
                Exception failure = null;
                try
                {
                    session.Input = input.SelectedIndex + 1; session.Output = output.SelectedIndex + 1;
                    foreach (Series series in chart.Series) series.Points.Clear();
                    Snapshot s = await Task.Run(() => session.Poll()); RenderSnapshot(s);
                }
                catch (Exception e) { failure = e; }
                if (failure != null) await HandleFault(failure);
            }
            finally { gate.Release(); busy = false; UpdateEnabled(); }
        }
        private async Task PollAsync()
        {
            if (session == null || busy || faulted || !gate.Wait(0)) return;
            try
            {
                Exception failure = null;
                try { Snapshot s = await Task.Run(() => session.Poll()); RenderSnapshot(s); }
                catch (Exception e) { failure = e; }
                if (failure != null) await HandleFault(failure);
            }
            finally { gate.Release(); UpdateEnabled(); }
        }
        private async Task StartAsync(bool manualMode)
        {
            if (session == null || busy || session.Owned || faulted) return;
            List<Stage> steps; Limits bounds;
            try
            {
                bounds = ReadLimits(); bounds.Validate();
                if (manualMode)
                {
                    double k = Numbers.Parse(target.Text) + (manualUnit.SelectedIndex == 1 ? 273.15 : 0), rate = Numbers.Parse(speed.Text);
                    steps = new List<Stage> { new Stage { TargetK = k, Mode = "Rate", Value = rate, HoldMinutes = 0, TimeoutMinutes = 20160 } };
                }
                else steps = ReadStages();
            }
            catch (Exception e) { UiMessageBox.Show(this, e.Message, "检查控温参数", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            busy = true; stopRequested = false; UpdateEnabled(); await gate.WaitAsync();
            try
            {
                Exception failure = null;
                try
                {
                if (stopRequested) return;
                session.StopRequested = false;
                session.Range = heaterRange.SelectedIndex + 1;
                bool holdEnd = manualMode || ending.SelectedIndex == 1;
                Note((manualMode ? "请求手动控温" : "请求运行程序") + "，输入 " + input.Text + " / 输出 " + output.Text + " / 档位 " + heaterRange.Text + "；按程序表执行，保留传感器与仪器温度保护。");
                for (int i = 0; i < steps.Count; i++) Note("温度段 " + (i + 1) + ": " + Numbers.F(steps[i].TargetK) + " K; " + steps[i].Mode + "=" + steps[i].Value + "; 保温=" + steps[i].HoldMinutes + " min; 超时=" + steps[i].TimeoutMinutes + " min");
                await Task.Run(() => session.Start(steps, bounds, manualMode, holdEnd));
                editedWhilePaused = false;
                status.Text = session.Message; Note(session.Message); timer.Start();
                NativeMethods.KeepAwake(true);
                }
                catch (Exception e) { failure = e; }
                if (failure != null)
                {
                    if (session.Owned) await HandleFault(failure);
                    else { Note("未启动：" + failure.Message); UiMessageBox.Show(this, failure.Message, "未启动控温", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
            finally { gate.Release(); busy = false; UpdateEnabled(); }
        }
        private async Task RestartAsync()
        {
            if (session == null || busy || stopRequested || faulted) return;
            if (!session.Owned) { await StartAsync(false); return; }
            List<Stage> steps; Limits bounds;
            try { steps = ReadStages(); bounds = ReadLimits(); ScheduleEngine.ValidateStages(steps, bounds, session.Last.Temperatures[session.Input - 1]); }
            catch (Exception error) { UiMessageBox.Show(this, error.Message, "检查程序参数", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            busy = true; UpdateEnabled(); await gate.WaitAsync();
            try
            {
                Note("请求重新开始：原第 " + (session.Engine.Index + 1) + " 点，已保持 " + session.Engine.HoldSeconds.ToString("0.000", Numbers.Invariant) + " 秒；返回第 1 点重新计时，保留现有记录。");
                Exception failure = null;
                try
                {
                    bool holdEnd = ending.SelectedIndex == 1;
                    await Task.Run(() => session.Restart(steps, bounds, holdEnd));
                    editedWhilePaused = false; intervalSummary.Text = "新程序已从第 1 点开始运行。";
                    status.Text = session.Message; Note(session.Message); timer.Start(); NativeMethods.KeepAwake(true);
                }
                catch (Exception error) { failure = error; }
                if (failure != null) await HandleFault(failure);
            }
            finally { gate.Release(); busy = false; UpdateEnabled(); }
        }

        private async Task PauseAsync()
        {
            if (editedWhilePaused) { UiMessageBox.Show(this, "程序已修改，请点击重新开始执行新方案。", "程序已修改"); return; }
            if (session == null || busy) return; busy = true; UpdateEnabled(); await gate.WaitAsync();
            try { session.PauseOrResume(); status.Text = session.Message; Note(session.Message); }
            finally { gate.Release(); busy = false; UpdateEnabled(); }
        }
        private async Task StopAsync(bool all)
        {
            if (session == null || stopRequested) return;
            stopRequested = true; session.StopRequested = true; busy = true; timer.Stop(); UpdateEnabled();
            status.Text = "正在关闭输出…"; await gate.WaitAsync();
            try
            {
                if (session == null) return;
                await Task.Run(() => session.Stop(all)); faulted = false; status.ForeColor = Teal; status.Text = session.Message; Note(session.Message);
                timer.Start();
            }
            catch (Exception e) { faulted = true; status.ForeColor = Color.Firebrick; status.Text = session.Message; Note(session.Message + " " + e.Message); UiMessageBox.Show(this, session.Message + "\n" + e.Message, "需要现场检查", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { NativeMethods.KeepAwake(false); gate.Release(); busy = false; stopRequested = false; UpdateEnabled(); }
        }
        private async Task HandleFault(Exception e)
        {
            timer.Stop(); faulted = true; NativeMethods.KeepAwake(false);
            string message = await Task.Run(() => session.FailSafe(e));
            status.ForeColor = Color.Firebrick; status.Text = session.Message; SafeNote(message);
            UiMessageBox.Show(this, message, "温控已中止", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void RenderSnapshot(Snapshot s)
        {
            int i = session.Input - 1; bool valid = s.Status[i] == 0;
            current.Text = valid ? s.Temperatures[i].ToString("0.000", Numbers.Invariant) + " K" : "输入异常";
            current.ForeColor = valid ? Ink : Color.Firebrick;
            celsius.Text = valid ? (s.Temperatures[i] - 273.15).ToString("0.000", Numbers.Invariant) + " °C  ·  输入 " + input.Text : "状态码 " + s.Status[i];
            setpoint.Text = Numbers.Finite(s.SetpointK) ? Numbers.F(s.SetpointK) + " K" : "原始单位";
            bool pidActive = s.Mode == 1 && s.Range > 0;
            controlState.Text = s.Range == 0 ? "加热已关闭 · 设定值仅保留" : pidActive ? "PID 控温已启用 · 持续保持目标" : "输出已启用 · 非 PID 控温模式";
            controlState.ForeColor = s.Range == 0 ? Color.Firebrick : Teal;
            heater.Text = s.Heater.ToString("0.0", Numbers.Invariant) + " %";
            sensorLabel.Text = "档位 " + new string[] { "关闭", "低", "中", "高" }[Math.Max(0, Math.Min(3, s.Range))] + "  ·  输入 " + (s.ControlInput >= 1 && s.ControlInput <= 4 ? ((char)('A' + s.ControlInput - 1)).ToString() : "无");
            status.ForeColor = session.ShutdownUnconfirmed ? Color.Firebrick : Teal; status.Text = session.Message;
            if (lastMessage != session.Message) { Note(session.Message); lastMessage = session.Message; }
            ScheduleEngine engine = session.Engine;
            if (engine.Stages.Count > 0)
            {
                Stage stage = engine.Stages[engine.Index];
                runDetail.Text = "段目标  " + Numbers.F(stage.TargetK) + " K\n已用  " + (engine.StageElapsed / 60).ToString("0.0") + " min    保持  " + (engine.HoldSeconds / 60).ToString("0.0") + " / " + Numbers.DurationLabel(stage.HoldMinutes) + "\n" + (stage.Mode == "TimedStep" ? "设定后计时 · 不等待实测到温" : "到温确认  " + engine.StableElapsed.ToString("0") + " 秒");
                progress.Value = engine.State == RunState.Completed ? 100 : Math.Min(99, (int)(100.0 * engine.Index / engine.Stages.Count));
                for (int row = 0; row < grid.Rows.Count; row++) grid.Rows[row].DefaultCellStyle.BackColor = session.Owned && row == engine.Index ? Color.FromArgb(226, 246, 242) : Color.Empty;
            }
            else runDetail.Text = (pidActive ? "仪器正在自主 PID 控温，持续保持目标。" : s.Range == 0 ? "加热已关闭，当前没有加热来维持目标。" : "仪器处于非 PID 输出模式。") + "\nGUI 正在监测并记录温度；未运行多段程序。";
            double x = s.Time.ToOADate();
            if (valid) chart.Series[0].Points.AddXY(x, s.Temperatures[i]);
            else { int p = chart.Series[0].Points.AddXY(x, 0); chart.Series[0].Points[p].IsEmpty = true; }
            if (Numbers.Finite(s.SetpointK)) chart.Series[1].Points.AddXY(x, s.SetpointK);
            foreach (Series series in chart.Series) while (series.Points.Count > 1800) series.Points.RemoveAt(0);
            chart.ChartAreas[0].RecalculateAxesScale();
            if (csv != null)
            {
                string[] fields = new string[] { s.Time.ToString("o"), session.Device.IsSimulation ? "SIM" : "REAL", session.Input.ToString(), session.Output.ToString(),
                    Numbers.F(s.Temperatures[0]), Numbers.F(s.Temperatures[1]), Numbers.F(s.Temperatures[2]), Numbers.F(s.Temperatures[3]),
                    s.Status[0].ToString(), s.Status[1].ToString(), s.Status[2].ToString(), s.Status[3].ToString(),
                    Numbers.Finite(s.SetpointK) ? Numbers.F(s.SetpointK) : "", Numbers.F(s.Heater), s.Range.ToString(),
                    engine.Stages.Count > 0 ? (engine.Index + 1).ToString() : "", engine.State.ToString(), engine.Phase,
                    engine.HoldSeconds.ToString("0.000", Numbers.Invariant), engine.Stages.Count > 0 ? Numbers.F(engine.Stages[engine.Index].TargetK) : "" };
                csv.WriteLine(string.Join(",", fields));
            }
            if (temperatureLog != null) { temperatureLog.Record(new DateTimeOffset(s.Time), s.Temperatures[i], valid, session.Input); logPathLabel.Text = "温度 + 当地时间 · 每 10 秒保存\n" + Path.GetFileName(temperatureLog.FilePath); }
            footer.Text = "最新采样 " + s.Time.ToString("HH:mm:ss") + "  |  A:" + s.Status[0] + "  B:" + s.Status[1] + "  C:" + s.Status[2] + "  D:" + s.Status[3] + "（0=有效） | " + (session.Device.IsSimulation ? "演示数据" : "真实设备") + " | 10 秒温度文件持续追加保存；完整原始采样另存 logs";
            if (!session.Owned) NativeMethods.KeepAwake(false);
        }
        private void OpenLogs(bool demo)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"); Directory.CreateDirectory(dir);
            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + (demo ? "_SIM" : "_REAL");
            temperatureLog = new TemperatureLog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temperature_data"), demo ? "SIM" : "REAL");
            csvPath = Path.Combine(dir, id + ".csv");
            csv = new StreamWriter(csvPath, false, new System.Text.UTF8Encoding(true)); csv.AutoFlush = true;
            csv.WriteLine("timestamp,source,control_input,output,A_K,B_K,C_K,D_K,A_status,B_status,C_status,D_status,setpoint_K,heater_percent,heater_range,stage,state,phase,hold_seconds,stage_target_K");
            eventFile = new StreamWriter(Path.Combine(dir, id + "_events.txt"), false, new System.Text.UTF8Encoding(true)); eventFile.AutoFlush = true;
            logPathLabel.Text = "自动保存 CSV + 操作日志\n" + Path.GetFileName(csvPath);
        }
        private void CloseLogs() { if (temperatureLog != null) { temperatureLog.Dispose(); temperatureLog = null; } if (csv != null) { csv.Dispose(); csv = null; } if (eventFile != null) { eventFile.Dispose(); eventFile = null; } }
        private void Note(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            eventLines.Add(line);
            if (eventLines.Count > 1500) eventLines.RemoveRange(0, 500);
            if (events.TextLength > 100000) events.Text = events.Text.Substring(events.TextLength - 50000);
            events.AppendText(UiText.T(line) + Environment.NewLine); if (eventFile != null) eventFile.WriteLine(line);
        }
        private void SafeNote(string text) { try { Note(text); } catch { events.AppendText(UiText.T("日志写入失败。") + Environment.NewLine); } }
        private void SaveRecipe()
        {
            try
            {
                Recipe recipe = new Recipe { Stages = ReadStages(), ToleranceK = Numbers.Parse(tolerance.Text), StableSeconds = Numbers.Parse(stability.Text), HoldAtEnd = ending.SelectedIndex == 1 };
                if (recipe.Stages.Any(s => s.Mode == "Step")) recipe.Version = 2; if (recipe.Stages.Any(s => s.Mode == "TimedStep")) recipe.Version = 3;
                using (SaveFileDialog dialog = new SaveFileDialog { Filter = UiText.T("温度程序 JSON|*.json"), InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recipes"), FileName = "temperature_program.json" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dialog.FileName, new JavaScriptSerializer().Serialize(recipe), new System.Text.UTF8Encoding(true)); Note("已保存程序：" + dialog.FileName); }
            }
            catch (Exception e) { UiMessageBox.Show(this, e.Message, "保存失败"); }
        }
        private void ImportRecipe()
        {
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter = UiText.T("温度程序 JSON|*.json"), InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recipes") })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    LoadRecipe(dialog.FileName);
                }
            }
            catch (Exception e) { UiMessageBox.Show(this, e.Message, "导入失败"); }
        }
        private void LoadRecipe(string path)
        {
            Recipe recipe = Recipe.Parse(File.ReadAllText(path));
            grid.Rows.Clear(); foreach (Stage stage in recipe.Stages) AddStage(stage);
            tolerance.Text = recipe.ToleranceK.ToString(Numbers.Invariant); stability.Text = recipe.StableSeconds.ToString(Numbers.Invariant); ending.SelectedIndex = recipe.HoldAtEnd ? 1 : 0;
            bool uniform = recipe.Stages.All(s => Math.Abs(s.HoldMinutes - recipe.Stages[0].HoldMinutes) < 0.000001);
            pointInterval.Text = uniform ? Numbers.FormatMinutes(recipe.Stages[0].HoldMinutes) : "";
            intervalSummary.Text = uniform ? "当前每点 " + pointInterval.Text + "（min 或分:秒），修改后点击应用。" : "当前各点时长不同；填写后可统一调整。";
            Note("已导入程序（尚未执行）：" + path + "；" + recipe.Stages.Count + " 个温度点，总保持 " + (recipe.Stages.Sum(s => s.HoldMinutes) / 60).ToString("0.##") + " 小时。");
        }
        private void GenerateStages()
        {
            using (Form dialog = new Form { Text = "生成等步长温度程序", Size = new Size(435, 405), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = Font })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, AutoSize = true };
                TextBox from = Box("245"), to = Box("255"), step = Box("5"), rate = Box("1"), hold = Box("5"), timeout = Box("180");
                AddRow(fields, Pair("起始温度 K", from, "终止温度 K", to), 62);
                AddRow(fields, Pair("步长 K（正数）", step, "变温速率 K/min", rate), 62);
                AddRow(fields, Pair("每点保温 min", hold, "每段总超时 min", timeout), 62);
                AddRow(fields, Small("自动判断升温或降温；包含首尾温度，末步可小于步长。将替换当前表格，生成后可逐行编辑。"), 68);
                Button confirm = ButtonOf("生成到表格", Teal); AddRow(fields, confirm, 42); dialog.Controls.Add(fields);
                confirm.Click += (s, e) =>
                {
                    try
                    {
                        double a = Numbers.Parse(from.Text), b = Numbers.Parse(to.Text), delta = Numbers.Parse(step.Text), r = Numbers.Parse(rate.Text), h = Numbers.Parse(hold.Text), t = Numbers.Parse(timeout.Text);
                        if (a < 0 || b < 0 || a > 1505 || b > 1505 || delta <= 0 || r < 0.001 || r > 100 || h < 0 || t <= 0) throw new ArgumentException("请检查温度、步长、速率和时间。");
                        int count = (int)Math.Ceiling(Math.Abs(b - a) / delta) + 1;
                        if (count < 1 || count > 1000) throw new ArgumentException("最多生成 1000 段，请增大步长。");
                        List<Stage> stages = new List<Stage>();
                        for (int i = 0; i < count; i++) stages.Add(new Stage { TargetK = i == count - 1 ? b : a + Math.Sign(b - a) * delta * i, Value = r, HoldMinutes = h, TimeoutMinutes = t });
                        grid.Rows.Clear(); foreach (Stage stage in stages) AddStage(stage); dialog.Close();
                    }
                    catch (Exception error) { UiMessageBox.Show(dialog, error.Message, "检查参数"); }
                };
                UiText.Bind(dialog);
                dialog.ShowDialog(this);
            }
        }
        private async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (closing) return;
            e.Cancel = true;
            if (busy || stopRequested) { e.Cancel = true; return; }
            if (session != null && (session.Owned || session.ShutdownUnconfirmed))
            {
                e.Cancel = true;
                if (UiMessageBox.Show(this, "控温正在运行，或关闭状态尚未确认。关闭前停止并确认当前输出已关闭？", "退出温控", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                await StopAsync(false);
                if (session.ShutdownUnconfirmed) return;
            }
            timer.Stop(); busy = true; await gate.WaitAsync();
            try { if (session != null) { session.Dispose(); session = null; } CloseLogs(); NativeMethods.KeepAwake(false); }
            finally { gate.Release(); }
            closing = true; Close();
        }
    }

    public static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);
        public static void KeepAwake(bool active) { SetThreadExecutionState(active ? 0x80000001u : 0x80000000u); }
    }
}
