namespace OracleClientSwitcher;

internal sealed class LogViewerForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private readonly IReadOnlyList<OracleClientInfo> clients;
    private readonly OracleEnvironmentStatus environmentStatus;
    private readonly DataGridView grid = new();
    private readonly Label status = new();

    public LogViewerForm(IReadOnlyList<OracleClientInfo> clients, OracleEnvironmentStatus environmentStatus)
    {
        this.clients = clients;
        this.environmentStatus = environmentStatus;
        Text = "日志与故障报告";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 700);
        MinimumSize = new Size(860, 560);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        Reload();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Page };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(root);
        var header = new GradientPanel { Dock = DockStyle.Fill, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105), Margin = Padding.Empty };
        header.Controls.Add(new Label { Text = "日志与故障报告", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 10) });
        header.Controls.Add(new Label { Text = "查看关键操作、清理日志或生成已脱敏的诊断包", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(31, 54) });
        root.Controls.Add(header, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 10, 22, 8), BackColor = Color.White, WrapContents = false };
        toolbar.Controls.Add(ButtonFor("刷新", (_, _) => Reload()));
        toolbar.Controls.Add(ButtonFor("清理日志", (_, _) => ClearLogs()));
        toolbar.Controls.Add(ButtonFor("生成诊断报告", (_, _) => CreateReport(), true, 140));
        root.Controls.Add(toolbar, 0, 1);

        grid.Dock = DockStyle.Fill;
        grid.Margin = new Padding(22, 10, 22, 10);
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowTemplate.Height = 36;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(238, 243, 249), ForeColor = Navy, Font = new Font(Font, FontStyle.Bold) };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "时间", Width = 190 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Level", HeaderText = "级别", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "类别", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "操作", Width = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "结果", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "详情", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        root.Controls.Add(grid, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        status.Location = new Point(24, 20);
        status.AutoSize = true;
        status.ForeColor = Color.FromArgb(71, 85, 105);
        var close = ButtonFor("关闭", (_, _) => Close(), true, 92);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        void Align() => close.Location = new Point(footer.ClientSize.Width - close.Width - 22, 12);
        footer.Resize += (_, _) => Align();
        footer.Controls.Add(status);
        footer.Controls.Add(close);
        Align();
        root.Controls.Add(footer, 0, 3);
    }

    private void Reload()
    {
        IReadOnlyList<AppLogEntry> entries = AppLog.Read();
        grid.Rows.Clear();
        foreach (AppLogEntry entry in entries)
        {
            int row = grid.Rows.Add(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), entry.Level switch { AppLogLevel.Error => "错误", AppLogLevel.Warning => "警告", _ => "信息" }, entry.Category, entry.Action, entry.Result, entry.Detail);
            grid.Rows[row].Cells[1].Style.ForeColor = entry.Level switch { AppLogLevel.Error => Color.FromArgb(185, 28, 28), AppLogLevel.Warning => Color.FromArgb(180, 83, 9), _ => Color.FromArgb(4, 120, 87) };
        }
        grid.ClearSelection();
        status.Text = $"共 {entries.Count} 条日志 · 路径和用户名已自动脱敏";
    }

    private void ClearLogs()
    {
        if (MessageBox.Show(this, "确定清理当前日志吗？", "清理日志", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        TnsOperationResult result = AppLog.Clear();
        if (!result.Success) MessageBox.Show(this, result.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Reload();
    }

    private void CreateReport()
    {
        using var dialog = new SaveFileDialog { Title = "保存诊断报告", Filter = "诊断报告 (*.zip)|*.zip", FileName = $"OracleClientSwitcher-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TnsOperationResult result = DiagnosticReportService.Create(dialog.FileName, clients, environmentStatus);
        MessageBox.Show(this, result.Message, result.Success ? "报告已生成" : "生成失败", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        Reload();
    }

    private static Button ButtonFor(string text, EventHandler click, bool primary = false, int width = 104)
    {
        var button = new RoundedButton { Text = text, Size = new Size(width, 34), Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White, ForeColor = primary ? Color.White : Navy, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
        button.Click += click;
        return button;
    }
}
