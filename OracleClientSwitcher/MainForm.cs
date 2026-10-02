using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace OracleClientSwitcher;

internal sealed class MainForm : Form
{
    private readonly DataGridView grid = new();
    private readonly Label statusLabel = new();
    private readonly Label activeLabel = new();
    private readonly Label detailsLabel = new();
    private readonly Label warningLabel = new();
    private readonly ComboBox scopeCombo = new();
    private readonly CheckBox oracleHomeCheck = new();
    private readonly Button switchButton = new RoundedButton();
    private readonly Button verifyButton = new RoundedButton();
    private readonly Button folderButton = new RoundedButton();
    private readonly Button isolatedLaunchButton = new RoundedButton();
    private readonly Button tnsButton = new RoundedButton();
    private readonly Button restoreButton = new RoundedButton();
    private readonly Button scanButton = new RoundedButton();
    private readonly Button addDirectoryButton = new RoundedButton();
    private readonly Button scanSettingsButton = new RoundedButton();
    private readonly Button diagnosticsButton = new RoundedButton();
    private readonly Button clientProfileButton = new RoundedButton();
    private readonly Button logsButton = new RoundedButton();
    private readonly List<OracleClientInfo> clients = new();
    private ClientProfileSettings profileSettings = ClientProfileManager.Load();
    private readonly ToolTip environmentToolTip = new() { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 150 };
    private OracleEnvironmentStatus environmentStatus = new() { State = OracleEnvironmentState.NotConfigured, Summary = "正在检测环境…" };
    private CancellationTokenSource? scanCancellation;

    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Green = Color.FromArgb(5, 150, 105);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);

    public MainForm()
    {
        Text = "Oracle 客户端切换器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 720);
        Size = new Size(1180, 800);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        BuildUi();
        Shown += async (_, _) => await ScanAsync(forceRefresh: false);
        FormClosing += (_, _) => RememberSelection();
    }

    private void BuildUi()
    {
        SuspendLayout();

        // 使用明确的三行布局，避免 Fill 控件被顶部区域覆盖。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            StartColor = Navy,
            EndColor = Color.FromArgb(24, 61, 105),
            Padding = new Padding(30, 14, 30, 10)
        };
        var title = new Label
        {
            Text = "Oracle 客户端切换器",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 19F, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(560, 48),
            Location = new Point(28, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true
        };
        var subtitle = new Label { Text = "自动发现  ·  位数识别  ·  环境隔离  ·  一键切换", ForeColor = Color.FromArgb(205, 219, 238), BackColor = Color.Transparent, AutoSize = true, Location = new Point(31, 55) };
        activeLabel.AutoSize = true;
        activeLabel.Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
        activeLabel.ForeColor = Color.White;
        activeLabel.BackColor = Color.FromArgb(43, 78, 124);
        activeLabel.Padding = new Padding(12, 7, 12, 7);
        activeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        activeLabel.Location = new Point(820, 27);
        void AlignActiveLabel() => activeLabel.Left = Math.Max(600, header.ClientSize.Width - activeLabel.Width - 30);
        header.Resize += (_, _) => AlignActiveLabel();
        activeLabel.SizeChanged += (_, _) => AlignActiveLabel();
        activeLabel.Text = "当前环境：正在检测…";
        header.Controls.AddRange(new Control[] { title, subtitle, activeLabel });
        root.Controls.Add(header, 0, 0);

        var commandBar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(24, 13, 24, 10), BackColor = Color.White };
        scanButton.Text = "重新扫描";
        StyleSecondaryButton(scanButton, 102, 36);
        scanButton.Location = new Point(24, 13);
        scanButton.Click += async (_, _) =>
        {
            if (scanCancellation is not null)
            {
                scanCancellation.Cancel();
                statusLabel.Text = "正在取消扫描…";
                return;
            }
            await ScanAsync(forceRefresh: true);
        };
        addDirectoryButton.Text = "扫描目录";
        addDirectoryButton.Location = new Point(136, 13);
        StyleSecondaryButton(addDirectoryButton, 118, 36);
        addDirectoryButton.Click += async (_, _) => await AddDirectoryAsync();
        scanSettingsButton.Text = "扫描设置";
        scanSettingsButton.Location = new Point(264, 13);
        StyleSecondaryButton(scanSettingsButton, 112, 36);
        scanSettingsButton.Click += async (_, _) =>
        {
            using var dialog = new ScanSettingsForm();
            if (dialog.ShowDialog(this) == DialogResult.OK) await ScanAsync(forceRefresh: true);
        };
        clientProfileButton.Text = "客户端整理";
        clientProfileButton.Location = new Point(386, 13);
        StyleSecondaryButton(clientProfileButton, 112, 36);
        clientProfileButton.Click += (_, _) => EditSelectedClientProfile();
        statusLabel.AutoSize = false;
        statusLabel.ForeColor = Color.FromArgb(82, 96, 115);
        statusLabel.Font = new Font(Font.FontFamily, 9.5F);
        statusLabel.BackColor = Color.FromArgb(239, 246, 255);
        statusLabel.Padding = new Padding(10, 6, 10, 6);
        statusLabel.Location = new Point(510, 15);
        statusLabel.Size = new Size(240, 34);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.AutoEllipsis = true;
        logsButton.Text = "日志报告";
        StyleSecondaryButton(logsButton, 112, 36);
        logsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        logsButton.Click += (_, _) => ShowLogs();
        diagnosticsButton.Text = "环境诊断";
        StyleSecondaryButton(diagnosticsButton, 126, 36);
        diagnosticsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        diagnosticsButton.Location = new Point(990, 13);
        diagnosticsButton.Click += (_, _) => ShowEnvironmentDiagnostics();
        void AlignDiagnosticsButton()
        {
            diagnosticsButton.Left = Math.Max(820, commandBar.ClientSize.Width - diagnosticsButton.Width - 24);
            logsButton.Left = diagnosticsButton.Left - logsButton.Width - 8;
            logsButton.Top = diagnosticsButton.Top;
            statusLabel.Width = Math.Max(120, logsButton.Left - statusLabel.Left - 12);
        }
        commandBar.Resize += (_, _) => AlignDiagnosticsButton();
        var separator = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Border };
        commandBar.Controls.AddRange(new Control[] { scanButton, addDirectoryButton, scanSettingsButton, clientProfileButton, statusLabel, logsButton, diagnosticsButton, separator });
        root.Controls.Add(commandBar, 0, 1);

        var content = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(24, 0, 24, 18),
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10,
            IsSplitterFixed = false,
            FixedPanel = FixedPanel.Panel2,
            BackColor = Page,
            Padding = Padding.Empty
        };
        root.Controls.Add(content, 0, 2);
        content.SizeChanged += (_, _) =>
        {
            const int panel1Minimum = 250;
            const int panel2Minimum = 248;
            int desired = content.Height - 258;
            int maximum = content.Height - panel2Minimum - content.SplitterWidth;
            if (desired >= panel1Minimum && desired <= maximum)
                content.SplitterDistance = desired;
        };

        ConfigureGrid();
        content.Panel1.Padding = new Padding(0, 0, 0, 2);
        var gridCard = CreateCard();
        gridCard.Padding = new Padding(1);
        gridCard.Controls.Add(grid);
        content.Panel1.Controls.Add(gridCard);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Page };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        content.Panel2.Controls.Add(bottom);

        var detailsCard = CreateCard();
        detailsCard.Padding = new Padding(22, 16, 22, 14);
        var detailsTitle = new Label { Text = "客户端详情", Dock = DockStyle.Top, Height = 30, Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), ForeColor = Navy };
        detailsLabel.Dock = DockStyle.Fill;
        detailsLabel.ForeColor = Color.FromArgb(55, 65, 81);
        detailsLabel.Font = new Font(Font.FontFamily, 8.7F);
        detailsLabel.Text = "选择一个客户端查看详情。";
        detailsLabel.Padding = new Padding(0, 6, 0, 0);
        detailsCard.Controls.Add(detailsLabel);
        detailsCard.Controls.Add(detailsTitle);
        bottom.Controls.Add(detailsCard, 0, 0);

        var actionCard = CreateCard();
        actionCard.Padding = new Padding(20, 16, 20, 14);
        bottom.Controls.Add(actionCard, 1, 0);
        bottom.SetCellPosition(actionCard, new TableLayoutPanelCellPosition(1, 0));
        bottom.Padding = new Padding(0);
        actionCard.Margin = new Padding(12, 0, 0, 0);
        detailsCard.Margin = new Padding(0);

        BuildActionCard(actionCard);
        ResumeLayout(true);
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Color.FromArgb(231, 235, 241);
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.RowTemplate.Height = 38;
        grid.ColumnHeadersHeight = 42;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersVisible = true;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 243, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 254);
        grid.DefaultCellStyle.ForeColor = Color.FromArgb(42, 52, 66);
        grid.DefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 236, 253);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 41, 59);
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Active", HeaderText = "状态", Width = 86 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Favorite", HeaderText = "收藏", Width = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DisplayName", HeaderText = "名称", Width = 170 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "版本", Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Arch", HeaderText = "位数", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "客户端类型", Width = 150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SqlPlus", HeaderText = "SQL*Plus", Width = 84 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "发现来源", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "客户端目录", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.HeaderCell.Style.BackColor = Color.FromArgb(238, 243, 249);
            column.HeaderCell.Style.ForeColor = Navy;
        }
        grid.SelectionChanged += (_, _) => UpdateSelection();
    }

    private void BuildActionCard(Panel card)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "切换设置",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            ForeColor = Navy,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        layout.Controls.Add(title, 0, 0);

        var scopeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scopeLabel = new Label
        {
            Text = "生效范围",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(75, 85, 99),
            Margin = Padding.Empty
        };
        scopeCombo.Dock = DockStyle.Fill;
        scopeCombo.Margin = new Padding(0, 2, 0, 3);
        scopeCombo.FlatStyle = FlatStyle.Standard;
        scopeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        scopeCombo.Items.AddRange(new object[] { "当前用户（无需管理员）", "整个系统（需要管理员）" });
        scopeCombo.SelectedIndex = EnvironmentManager.GetOracleEntries(EnvironmentVariableTarget.Machine).Count > 0 ? 1 : 0;
        scopeCombo.SelectedIndexChanged += (_, _) => UpdateScopeWarning();
        scopeRow.Controls.Add(scopeLabel, 0, 0);
        scopeRow.Controls.Add(scopeCombo, 1, 0);
        layout.Controls.Add(scopeRow, 0, 1);

        oracleHomeCheck.Text = "同时设置 ORACLE_HOME（兼容旧程序）";
        oracleHomeCheck.AutoSize = false;
        oracleHomeCheck.Dock = DockStyle.Fill;
        oracleHomeCheck.Margin = Padding.Empty;
        oracleHomeCheck.TextAlign = ContentAlignment.MiddleLeft;
        oracleHomeCheck.ForeColor = Color.FromArgb(75, 85, 99);
        layout.Controls.Add(oracleHomeCheck, 0, 2);

        warningLabel.Dock = DockStyle.Fill;
        warningLabel.Margin = Padding.Empty;
        warningLabel.TextAlign = ContentAlignment.MiddleLeft;
        warningLabel.ForeColor = Color.FromArgb(180, 83, 9);
        warningLabel.Font = new Font(Font.FontFamily, 8.5F);
        layout.Controls.Add(warningLabel, 0, 3);

        switchButton.Text = "切换到选中版本";
        StylePrimaryButton(switchButton);
        switchButton.Dock = DockStyle.Fill;
        switchButton.Margin = new Padding(0, 3, 6, 4);
        switchButton.Click += (_, _) => SwitchSelected();

        verifyButton.Text = "验证";
        StyleSecondaryButton(verifyButton, 78, 36);
        verifyButton.Dock = DockStyle.Fill;
        verifyButton.Margin = new Padding(6, 3, 0, 4);
        verifyButton.Click += (_, _) => VerifySelected();

        var primaryButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        primaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        primaryButtons.Controls.Add(switchButton, 0, 0);
        primaryButtons.Controls.Add(verifyButton, 1, 0);
        layout.Controls.Add(primaryButtons, 0, 4);

        folderButton.Text = "打开目录";
        StyleSecondaryButton(folderButton, 94, 32);
        folderButton.Dock = DockStyle.Fill;
        folderButton.Margin = new Padding(0, 3, 5, 0);
        folderButton.Click += (_, _) => OpenSelectedFolder(false);

        isolatedLaunchButton.Text = "隔离启动";
        StyleSecondaryButton(isolatedLaunchButton, 98, 32);
        isolatedLaunchButton.Dock = DockStyle.Fill;
        isolatedLaunchButton.Margin = new Padding(4, 3, 4, 0);
        isolatedLaunchButton.Click += (_, _) => ShowIsolatedLauncher();

        tnsButton.Text = "TNS 管理";
        StyleSecondaryButton(tnsButton, 94, 32);
        tnsButton.Dock = DockStyle.Fill;
        tnsButton.Margin = new Padding(5, 3, 5, 0);
        tnsButton.Click += (_, _) => ShowTnsConfiguration();

        restoreButton.Text = "备份历史";
        StyleSecondaryButton(restoreButton, 116, 32);
        restoreButton.Dock = DockStyle.Fill;
        restoreButton.Margin = new Padding(5, 3, 0, 0);
        restoreButton.Click += (_, _) => ShowBackupHistory();

        var secondaryButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        secondaryButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        secondaryButtons.Controls.Add(folderButton, 0, 0);
        secondaryButtons.Controls.Add(isolatedLaunchButton, 1, 0);
        secondaryButtons.Controls.Add(tnsButton, 2, 0);
        secondaryButtons.Controls.Add(restoreButton, 3, 0);
        layout.Controls.Add(secondaryButtons, 0, 5);

        card.Controls.Add(layout);
        UpdateScopeWarning();
    }

    private async Task ScanAsync(bool forceRefresh)
    {
        if (scanCancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        scanCancellation = cancellation;
        activeLabel.Text = "当前环境：正在检测…";
        activeLabel.BackColor = Color.FromArgb(43, 78, 124);
        SetBusy(true, "准备扫描…", cancellable: true);
        AppLog.Information("扫描", forceRefresh ? "强制扫描" : "启动扫描", "开始");
        try
        {
            var progress = new Progress<string>(message => statusLabel.Text = message);
            List<OracleClientInfo> found = await OracleScanner.ScanAutomaticallyAsync(progress, cancellation.Token, forceRefresh);
            clients.Clear();
            clients.AddRange(found);
            RefreshGrid();
            bool fromCache = clients.Count > 0 && clients.All(x => x.DiscoverySource.Contains("缓存", StringComparison.OrdinalIgnoreCase));
            statusLabel.Text = $"发现 {clients.Count} 个客户端{(fromCache ? "（缓存）" : string.Empty)}  ·  {environmentStatus.Summary}";
            AppLog.Information("扫描", forceRefresh ? "强制扫描" : "启动扫描", $"发现 {clients.Count} 个客户端", fromCache ? "使用缓存" : "实时扫描");
        }
        catch (OperationCanceledException) { statusLabel.Text = "扫描已取消"; AppLog.Warning("扫描", "扫描", "用户取消"); }
        catch (Exception ex) { statusLabel.Text = "扫描失败：" + ex.Message; AppLog.Error("扫描", "扫描失败", ex); }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(scanCancellation, cancellation)) scanCancellation = null;
            SetBusy(false);
        }
    }

    private async Task AddDirectoryAsync()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择 Oracle 客户端目录或其上级目录", UseDescriptionForTitle = true, ShowNewFolderButton = false, InitialDirectory = @"E:\" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true, "正在扫描指定目录…");
        try
        {
            var progress = new Progress<string>(message => statusLabel.Text = message);
            var found = await OracleScanner.ScanDirectoryAsync(dialog.SelectedPath, progress);
            foreach (OracleClientInfo client in found)
            {
                if (clients.All(x => !string.Equals(OracleScanner.Normalize(x.PathDirectory), OracleScanner.Normalize(client.PathDirectory), StringComparison.OrdinalIgnoreCase)))
                    clients.Add(client);
            }
            clients.Sort((a, b) => string.Compare(b.Version, a.Version, StringComparison.OrdinalIgnoreCase));
            RefreshGrid();
            statusLabel.Text = $"发现 {clients.Count} 个客户端  ·  {environmentStatus.Summary}";
        }
        finally { SetBusy(false); }
    }

    private void RefreshGrid()
    {
        environmentStatus = EnvironmentManager.DetectOracleEnvironment(clients);
        string? active = environmentStatus.ActiveClient?.PathDirectory;
        grid.Rows.Clear();
        foreach (OracleClientInfo client in ClientProfileManager.SortAndFilter(clients, profileSettings))
        {
            OracleClientProfile profile = ClientProfileManager.GetProfile(profileSettings, client.PathDirectory);
            bool isActive = active is not null && string.Equals(OracleScanner.Normalize(client.PathDirectory), active, StringComparison.OrdinalIgnoreCase);
            bool isSuggested = environmentStatus.State == OracleEnvironmentState.Invalid &&
                environmentStatus.SuggestedClient is not null &&
                string.Equals(OracleScanner.Normalize(client.PathDirectory), OracleScanner.Normalize(environmentStatus.SuggestedClient.PathDirectory), StringComparison.OrdinalIgnoreCase);
            string stateText = isActive ? "● 当前" : isSuggested ? "建议修复" : string.Empty;
            int index = grid.Rows.Add(stateText, profile.Favorite ? "★" : string.Empty,
                ClientProfileManager.GetDisplayName(profileSettings, client), client.Version, client.Architecture, client.ClientType,
                client.HasSqlPlus ? "有" : "无", client.DiscoverySource, client.PathDirectory);
            grid.Rows[index].Tag = client;
            if (isActive)
            {
                grid.Rows[index].Cells[0].Style.ForeColor = Green;
                grid.Rows[index].Cells[0].Style.SelectionForeColor = Green;
            }
            if (isSuggested)
            {
                grid.Rows[index].Cells[0].Style.ForeColor = Color.FromArgb(217, 119, 6);
                grid.Rows[index].Cells[0].Style.SelectionForeColor = Color.FromArgb(180, 83, 9);
            }
        }
        UpdateEnvironmentPresentation();

        OracleClientInfo? preferred = clients.FirstOrDefault(x => string.Equals(
            OracleScanner.Normalize(x.PathDirectory), OracleScanner.Normalize(profileSettings.LastSelectedPath), StringComparison.OrdinalIgnoreCase))
            ?? environmentStatus.ActiveClient ?? environmentStatus.SuggestedClient;
        DataGridViewRow? preferredRow = preferred is null ? null : grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
            row.Tag is OracleClientInfo item && string.Equals(OracleScanner.Normalize(item.PathDirectory), OracleScanner.Normalize(preferred.PathDirectory), StringComparison.OrdinalIgnoreCase));
        if (preferredRow is not null) preferredRow.Selected = true;
        else if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
        if (grid.Rows.Count > 0) grid.FirstDisplayedScrollingRowIndex = 0;
        UpdateSelection();
        UpdateScopeWarning();
    }

    private void UpdateEnvironmentPresentation()
    {
        int issueCount = environmentStatus.Issues.Count(x => x.Severity != EnvironmentIssueSeverity.Info);
        switch (environmentStatus.State)
        {
            case OracleEnvironmentState.Active:
                activeLabel.Text = environmentStatus.HasWarnings
                    ? $"当前：{environmentStatus.ActiveClient?.Version} · 有警告"
                    : $"当前：{environmentStatus.ActiveClient?.Version} · {environmentStatus.ActiveClient?.Architecture}";
                activeLabel.BackColor = environmentStatus.HasWarnings ? Color.FromArgb(180, 83, 9) : Color.FromArgb(4, 120, 87);
                statusLabel.BackColor = environmentStatus.HasWarnings ? Color.FromArgb(255, 247, 237) : Color.FromArgb(236, 253, 245);
                statusLabel.ForeColor = environmentStatus.HasWarnings ? Color.FromArgb(154, 52, 18) : Color.FromArgb(4, 120, 87);
                break;
            case OracleEnvironmentState.Invalid:
                activeLabel.Text = "环境异常：配置路径失效";
                activeLabel.BackColor = Color.FromArgb(185, 70, 38);
                statusLabel.BackColor = Color.FromArgb(255, 247, 237);
                statusLabel.ForeColor = Color.FromArgb(154, 52, 18);
                break;
            default:
                activeLabel.Text = "当前：尚未配置";
                activeLabel.BackColor = Color.FromArgb(71, 85, 105);
                statusLabel.BackColor = Color.FromArgb(241, 245, 249);
                statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
                break;
        }
        environmentToolTip.SetToolTip(activeLabel, environmentStatus.Detail);
        diagnosticsButton.Text = issueCount > 0 ? $"查看问题 ({issueCount})" : "环境诊断  ✓";
        diagnosticsButton.ForeColor = issueCount > 0 ? Color.FromArgb(180, 83, 9) : Color.FromArgb(4, 120, 87);
        diagnosticsButton.FlatAppearance.BorderColor = issueCount > 0 ? Color.FromArgb(253, 186, 116) : Color.FromArgb(110, 231, 183);
        environmentToolTip.SetToolTip(diagnosticsButton, issueCount > 0 ? "查看具体问题、当前值和建议修改内容" : "当前 Oracle 环境检查正常");
    }

    private void ShowEnvironmentDiagnostics()
    {
        environmentStatus = EnvironmentManager.DetectOracleEnvironment(clients);
        UpdateEnvironmentPresentation();
        statusLabel.Text = $"发现 {clients.Count} 个客户端  ·  {environmentStatus.Summary}";
        using var dialog = new EnvironmentDiagnosticsForm(environmentStatus);
        dialog.ShowDialog(this);
    }

    private OracleClientInfo? SelectedClient => grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as OracleClientInfo : null;

    private void UpdateSelection()
    {
        OracleClientInfo? client = SelectedClient;
        bool enabled = client is not null;
        switchButton.Enabled = verifyButton.Enabled = folderButton.Enabled = isolatedLaunchButton.Enabled = tnsButton.Enabled = enabled;
        clientProfileButton.Enabled = clients.Count > 0;
        if (client is null)
        {
            detailsLabel.Text = "选择一个客户端查看详情。";
            return;
        }
        string state = environmentStatus.ActiveClient == client ? "当前生效" : environmentStatus.SuggestedClient == client ? "建议修复" : "可用";
        string tnsDirectory = TnsConfigurationManager.GetEffectiveDirectory(client);
        OracleClientProfile profile = ClientProfileManager.GetProfile(profileSettings, client.PathDirectory);
        string name = ClientProfileManager.GetDisplayName(profileSettings, client);
        string notes = string.IsNullOrWhiteSpace(profile.Notes) ? "未设置" : profile.Notes.Replace("\r", " ").Replace("\n", " ");
        detailsLabel.Text = $"名称  {(profile.Favorite ? "★ " : string.Empty)}{name}      状态  {state}\n版本  {client.Version}      位数  {client.Architecture}      类型  {client.ClientType}\n备注  {notes}\n\n客户端目录  {client.PathDirectory}\nTNS 目录    {tnsDirectory}";
        oracleHomeCheck.Checked = client.ClientType == "完整客户端";
    }

    private void UpdateScopeWarning()
    {
        var staleSystem = EnvironmentManager.GetStaleOracleEntries(EnvironmentVariableTarget.Machine);
        var allSystem = EnvironmentManager.GetOracleEntries(EnvironmentVariableTarget.Machine);
        if (environmentStatus.State == OracleEnvironmentState.Invalid && scopeCombo.SelectedIndex == 1)
            warningLabel.Text = "将替换失效的系统 Oracle 配置，并自动创建备份。";
        else if (scopeCombo.SelectedIndex == 1)
            warningLabel.Text = EnvironmentManager.IsAdministrator() ? "将写入系统环境变量。" : "切换时会弹出 Windows 管理员确认。";
        else if (staleSystem.Count > 0)
            warningLabel.Text = $"系统 PATH 有 {staleSystem.Count} 个失效 Oracle 条目；建议选“整个系统”。";
        else if (allSystem.Count > 0)
            warningLabel.Text = "系统 PATH 已含 Oracle，优先级高于用户 PATH；建议选“整个系统”。";
        else
            warningLabel.Text = "仅影响当前 Windows 用户，新启动的程序生效。";
    }

    private void SwitchSelected()
    {
        OracleClientInfo? client = SelectedClient;
        if (client is null) return;
        bool system = scopeCombo.SelectedIndex == 1;
        var target = system ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User;
        EnvironmentChangePreview preview = EnvironmentManager.BuildChangePreview(client, target, oracleHomeCheck.Checked);
        if (!preview.HasChanges)
        {
            MessageBox.Show(this, "所选客户端已经是该作用域的默认配置，无需修改。", "无需切换",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (var previewDialog = new EnvironmentChangePreviewForm(preview))
            if (previewDialog.ShowDialog(this) != DialogResult.OK) return;

        if (system && !EnvironmentManager.IsAdministrator())
        {
            string resultFile = EnvironmentManager.CreateOperationResultPath();
            bool started = EnvironmentManager.RelaunchElevated(
                "--apply-system", client.PathDirectory, oracleHomeCheck.Checked.ToString(), "--result-file", resultFile);
            statusLabel.Text = started ? "已提交系统级切换，正在等待写入和校验结果…" : "未执行系统级切换。";
            if (started) WatchForOperationResult(resultFile, "切换");
            return;
        }

        ApplyResult result = EnvironmentManager.Apply(client, target, oracleHomeCheck.Checked);
        ShowOperationResult(result, "切换");
        if (result.Success) RefreshGrid();
    }

    private void WatchForOperationResult(string resultFile, string operation)
    {
        int remainingChecks = 80;
        var timer = new System.Windows.Forms.Timer { Interval = 750 };
        timer.Tick += (_, _) =>
        {
            if (EnvironmentManager.TryReadOperationResult(resultFile, out ApplyResult? result) && result is not null)
            {
                timer.Stop();
                timer.Dispose();
                RefreshGrid();
                statusLabel.Text = $"发现 {clients.Count} 个客户端  ·  {environmentStatus.Summary}";
                ShowOperationResult(result, operation);
                return;
            }

            if (--remainingChecks <= 0)
            {
                timer.Stop();
                timer.Dispose();
                RefreshGrid();
                statusLabel.Text = $"{operation}结果未能回传，请通过环境诊断确认当前配置。";
            }
        };
        timer.Start();
    }

    private void ShowOperationResult(ApplyResult result, string operation)
    {
        string backupText = result.Success && result.BackupPath is not null && operation == "切换"
            ? "\n\n切换前配置已备份，写入后校验通过。"
            : string.Empty;
        AppLog.Information("环境", operation, result.Success ? "成功" : "失败", result.Message);
        MessageBox.Show(this, result.Message + backupText,
            result.Success ? $"{operation}成功" : $"{operation}失败",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    private void VerifySelected()
    {
        OracleClientInfo? client = SelectedClient;
        if (client is null) return;
        AppLog.Information("验证", "打开客户端验证", "开始", client.PathDirectory);
        using var dialog = new ClientVerificationForm(client);
        dialog.ShowDialog(this);
    }

    private void OpenSelectedFolder(bool tns)
    {
        OracleClientInfo? client = SelectedClient;
        if (client is null) return;
        string path = tns ? client.TnsAdmin : client.PathDirectory;
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开目录", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowTnsConfiguration()
    {
        OracleClientInfo? client = SelectedClient;
        if (client is null) return;
        using var dialog = new TnsConfigurationForm(client, clients);
        dialog.ShowDialog(this);
        RefreshGrid();
    }

    private void ShowIsolatedLauncher()
    {
        OracleClientInfo? client = SelectedClient;
        if (client is null) return;
        using var dialog = new IsolatedLaunchForm(client);
        dialog.ShowDialog(this);
    }

    private void ShowBackupHistory()
    {
        using var history = new BackupHistoryForm();
        if (history.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(history.SelectedBackupPath)) return;
        string backup = history.SelectedBackupPath;

        EnvironmentVariableTarget target;
        try { target = EnvironmentManager.ReadBackupTarget(backup); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法读取备份：" + ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (MessageBox.Show(this, $"确定恢复所选环境备份吗？\n{File.GetLastWriteTime(backup):yyyy-MM-dd HH:mm:ss}\n\n恢复前的当前配置也会自动备份。", "确认恢复",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        if (target == EnvironmentVariableTarget.Machine && !EnvironmentManager.IsAdministrator())
        {
            string resultFile = EnvironmentManager.CreateOperationResultPath();
            bool started = EnvironmentManager.RelaunchElevated("--restore", backup, "--result-file", resultFile);
            statusLabel.Text = started ? "已提交系统级恢复，正在等待写入和校验结果…" : "未执行恢复操作。";
            if (started) WatchForOperationResult(resultFile, "恢复");
            return;
        }
        ApplyResult result = EnvironmentManager.Restore(backup);
        ShowOperationResult(result, "恢复");
        if (result.Success) RefreshGrid();
    }

    private void EditSelectedClientProfile()
    {
        OracleClientInfo? client = SelectedClient ?? clients.FirstOrDefault();
        if (client is null) return;
        using var dialog = new ClientProfileForm(client, profileSettings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            profileSettings = ClientProfileManager.Load();
            RefreshGrid();
        }
    }

    private void ShowLogs()
    {
        environmentStatus = EnvironmentManager.DetectOracleEnvironment(clients);
        using var dialog = new LogViewerForm(clients, environmentStatus);
        dialog.ShowDialog(this);
    }

    private void RememberSelection()
    {
        OracleClientInfo? client = SelectedClient;
        if (client is not null)
        {
            profileSettings.LastSelectedPath = OracleScanner.Normalize(client.PathDirectory);
            ClientProfileManager.GetProfile(profileSettings, client.PathDirectory).LastSelectedAt = DateTime.Now;
        }
        ClientProfileManager.Save(profileSettings);
    }

    private void SetBusy(bool busy, string? text = null, bool cancellable = false)
    {
        scanButton.Text = busy && cancellable ? "取消扫描" : "重新扫描";
        scanButton.Enabled = !busy || cancellable;
        addDirectoryButton.Enabled = !busy;
        scanSettingsButton.Enabled = !busy;
        clientProfileButton.Enabled = !busy && clients.Count > 0;
        UseWaitCursor = busy;
        if (text is not null) statusLabel.Text = text;
    }

    private static Panel CreateCard() => new BorderPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Border };

    private static void StylePrimaryButton(Button button)
    {
        button.BackColor = Blue;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 64, 175);
        button.Cursor = Cursors.Hand;
        button.Font = new Font(button.Font, FontStyle.Bold);
    }

    private static void StyleSecondaryButton(Button button, int width = 98, int height = 34)
    {
        button.Size = new Size(width, height);
        button.BackColor = Color.White;
        button.ForeColor = Navy;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 246, 255);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(219, 234, 254);
        button.Cursor = Cursors.Hand;
    }
}

internal sealed class GradientPanel : Panel
{
    public Color StartColor { get; set; } = Color.FromArgb(20, 42, 74);
    public Color EndColor { get; set; } = Color.FromArgb(24, 61, 105);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

internal sealed class BorderPanel : Panel
{
    public Color BorderColor { get; set; } = Color.FromArgb(218, 225, 235);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class RoundedButton : Button
{
    public int CornerRadius { get; set; } = 7;
    private bool hovered;
    private bool pressed;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left) pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF bounds = new(0.5F, 0.5F, Math.Max(1, ClientSize.Width - 1.5F), Math.Max(1, ClientSize.Height - 1.5F));
        using GraphicsPath path = CreateRoundedPath(bounds, CornerRadius);
        Color fill = !Enabled
            ? Color.FromArgb(241, 245, 249)
            : pressed && FlatAppearance.MouseDownBackColor != Color.Empty
                ? FlatAppearance.MouseDownBackColor
                : hovered && FlatAppearance.MouseOverBackColor != Color.Empty
                    ? FlatAppearance.MouseOverBackColor
                    : BackColor;
        using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
        if (FlatAppearance.BorderSize > 0)
        {
            using var pen = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
            e.Graphics.DrawPath(pen, path);
        }

        Color textColor = Enabled ? ForeColor : Color.FromArgb(148, 163, 184);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            Rectangle focus = Rectangle.Inflate(ClientRectangle, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, textColor, fill);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 1 || Height <= 1 || !IsHandleCreated) return;
        using GraphicsPath path = CreateRoundedPath(new RectangleF(0, 0, Width - 1, Height - 1), CornerRadius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, int requestedRadius)
    {
        float radius = Math.Min(requestedRadius, Math.Min(bounds.Width, bounds.Height) / 2F);
        float diameter = Math.Max(1F, radius * 2F);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
