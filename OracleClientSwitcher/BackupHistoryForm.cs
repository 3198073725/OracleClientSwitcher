using System.Diagnostics;

namespace OracleClientSwitcher;

internal sealed class BackupHistoryForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly DataGridView grid = new();
    private readonly TextBox details = new();
    private readonly Button restoreButton = new RoundedButton();
    private readonly IReadOnlyList<EnvironmentBackupRecord> records;

    public string? SelectedBackupPath { get; private set; }

    public BackupHistoryForm()
    {
        records = EnvironmentManager.GetBackups();
        Text = "Oracle 环境备份历史";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1080, 700);
        MinimumSize = new Size(900, 600);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadRecords();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        var header = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            StartColor = Navy,
            EndColor = Color.FromArgb(24, 61, 105),
            Padding = new Padding(30, 14, 30, 10)
        };
        header.Controls.Add(new Label
        {
            Text = "环境备份历史",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(27, 12)
        });
        header.Controls.Add(new Label
        {
            Text = "每次切换和恢复前都会自动创建备份，可选择任意记录恢复",
            ForeColor = Color.FromArgb(211, 225, 243),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(31, 57)
        });
        root.Controls.Add(header, 0, 0);

        ConfigureGrid();
        root.Controls.Add(grid, 0, 1);

        details.Dock = DockStyle.Fill;
        details.Margin = new Padding(22, 8, 22, 10);
        details.Multiline = true;
        details.ReadOnly = true;
        details.ScrollBars = ScrollBars.Both;
        details.BackColor = Color.White;
        details.ForeColor = Color.FromArgb(30, 41, 59);
        details.BorderStyle = BorderStyle.FixedSingle;
        details.Font = new Font("Consolas", 9.5F);
        details.WordWrap = false;
        root.Controls.Add(details, 0, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(20, 14, 22, 12),
            BackColor = Color.White
        };
        restoreButton.Text = "恢复所选备份";
        restoreButton.DialogResult = DialogResult.OK;
        restoreButton.Enabled = false;
        StyleButton(restoreButton, true, 146);
        restoreButton.Click += (_, _) => SelectedBackupPath = SelectedRecord?.FilePath;
        var cancel = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel };
        StyleButton(cancel, false, 88);
        var folder = new RoundedButton { Text = "打开备份目录" };
        StyleButton(folder, false, 130);
        folder.Click += (_, _) => OpenBackupFolder();
        actions.Controls.Add(restoreButton);
        actions.Controls.Add(cancel);
        actions.Controls.Add(folder);
        root.Controls.Add(actions, 0, 3);
        AcceptButton = restoreButton;
        CancelButton = cancel;
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.Margin = new Padding(22, 18, 22, 6);
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 38;
        grid.RowTemplate.Height = 38;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(30, 41, 59),
            SelectionBackColor = Color.FromArgb(219, 234, 254),
            SelectionForeColor = Navy,
            Padding = new Padding(5)
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(236, 242, 249),
            ForeColor = Navy,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Padding = new Padding(5)
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "备份时间", Width = 198 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "作用域", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "PATH 中的首个目录", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ORACLE_HOME", Width = 240 });
        grid.SelectionChanged += (_, _) => UpdateDetails();
    }

    private void LoadRecords()
    {
        foreach (EnvironmentBackupRecord record in records)
        {
            string firstPath = record.Backup.PathValue?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "（空）";
            int index = grid.Rows.Add(
                record.Backup.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                record.Backup.Scope == EnvironmentVariableTarget.Machine.ToString() ? "整个系统" : "当前用户",
                firstPath,
                string.IsNullOrWhiteSpace(record.Backup.OracleHome) ? "（未设置）" : record.Backup.OracleHome);
            grid.Rows[index].Tag = record;
        }

        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            UpdateDetails();
        }
        else
        {
            details.Text = "尚未创建环境备份。首次执行切换后，这里会显示备份历史。";
        }
    }

    private EnvironmentBackupRecord? SelectedRecord =>
        grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as EnvironmentBackupRecord : null;

    private void UpdateDetails()
    {
        EnvironmentBackupRecord? record = SelectedRecord;
        restoreButton.Enabled = record is not null;
        if (record is null) return;

        EnvironmentBackup backup = record.Backup;
        var target = Enum.TryParse(backup.Scope, out EnvironmentVariableTarget parsed) ? parsed : EnvironmentVariableTarget.User;
        string currentPath = Environment.GetEnvironmentVariable("Path", target) ?? string.Empty;
        string currentHome = Environment.GetEnvironmentVariable("ORACLE_HOME", target) ?? "（未设置）";
        string currentTns = Environment.GetEnvironmentVariable("TNS_ADMIN", target) ?? "（未设置）";
        details.Text =
            $"备份文件：{record.FilePath}\r\n\r\n" +
            $"PATH\r\n  当前：{currentPath}\r\n  恢复：{backup.PathValue ?? "（空）"}\r\n\r\n" +
            $"ORACLE_HOME\r\n  当前：{currentHome}\r\n  恢复：{backup.OracleHome ?? "（未设置）"}\r\n\r\n" +
            $"TNS_ADMIN\r\n  当前：{currentTns}\r\n  恢复：{backup.TnsAdmin ?? "（未设置）"}";
    }

    private void OpenBackupFolder()
    {
        string? path = SelectedRecord is null ? null : Path.GetDirectoryName(SelectedRecord.FilePath);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private static void StyleButton(Button button, bool primary, int width)
    {
        button.Size = new Size(width, 38);
        button.Margin = new Padding(10, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Blue : Color.White;
        button.ForeColor = primary ? Color.White : Navy;
        button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand;
    }
}
