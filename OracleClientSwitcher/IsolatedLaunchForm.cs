namespace OracleClientSwitcher;

internal sealed class IsolatedLaunchForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Green = Color.FromArgb(4, 120, 87);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly OracleClientInfo client;
    private readonly ComboBox recentCombo = new();
    private readonly TextBox executableText = new();
    private readonly TextBox argumentsText = new();
    private readonly TextBox workingDirectoryText = new();
    private readonly CheckBox oracleHomeCheck = new();
    private readonly Label compatibilityLabel = new();
    private readonly Label statusLabel = new();
    private readonly DataGridView environmentGrid = new();
    private readonly Button launchButton = new RoundedButton();
    private bool loadingFields;

    public IsolatedLaunchForm(OracleClientInfo client, string? initialExecutable = null)
    {
        this.client = client;
        Text = "应用隔离启动";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 740);
        MinimumSize = new Size(880, 680);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadRecentProfiles();
        if (!string.IsNullOrWhiteSpace(initialExecutable)) SetExecutable(initialExecutable);
        UpdatePreview();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty, BackColor = Page };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        var header = new GradientPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105) };
        header.Controls.Add(new Label { Text = "应用隔离启动", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), AutoSize = true, Location = new Point(30, 11) });
        header.Controls.Add(new Label { Text = "仅为目标程序注入 Oracle 环境，不修改 Windows 环境变量", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(33, 56) });
        var badge = new Label { Text = $"Oracle {client.Version} · {client.Architecture}", ForeColor = Color.White, BackColor = Color.FromArgb(43, 78, 124), Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), AutoSize = true, Padding = new Padding(12, 7, 12, 7), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        void AlignBadge() => badge.Location = new Point(Math.Max(650, header.ClientSize.Width - badge.Width - 28), 25);
        header.Resize += (_, _) => AlignBadge();
        badge.SizeChanged += (_, _) => AlignBadge();
        header.Controls.Add(badge);
        AlignBadge();
        root.Controls.Add(header, 0, 0);

        var notice = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(22, 14, 22, 6), BackColor = Color.White, BorderColor = Border, Padding = new Padding(16, 10, 16, 8) };
        notice.Controls.Add(new Label { Text = "使用场景", ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold), AutoSize = true, Location = new Point(15, 9) });
        notice.Controls.Add(new Label { Text = "适合 Navicat、PL/SQL Developer、业务程序等。若目标程序已在运行，请先退出，避免单实例程序复用旧进程。", ForeColor = Color.FromArgb(71, 85, 105), AutoSize = false, AutoEllipsis = true, Location = new Point(15, 34), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Size = new Size(860, 24) });
        root.Controls.Add(notice, 0, 1);

        root.Controls.Add(CreateInputCard(), 0, 2);

        var compatibility = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(22, 4, 22, 4), BackColor = Color.White, BorderColor = Border };
        compatibilityLabel.AutoSize = false;
        compatibilityLabel.Dock = DockStyle.Fill;
        compatibilityLabel.TextAlign = ContentAlignment.MiddleLeft;
        compatibilityLabel.Padding = new Padding(16, 0, 16, 0);
        compatibilityLabel.ForeColor = Color.FromArgb(71, 85, 105);
        compatibility.Controls.Add(compatibilityLabel);
        root.Controls.Add(compatibility, 0, 3);

        ConfigureEnvironmentGrid();
        var gridCard = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(22, 6, 22, 8), BackColor = Color.White, BorderColor = Border, Padding = new Padding(1) };
        gridCard.Controls.Add(environmentGrid);
        root.Controls.Add(gridCard, 0, 4);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        statusLabel.Text = "请选择要启动的程序。";
        statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        statusLabel.AutoEllipsis = true;
        statusLabel.Location = new Point(24, 25);
        statusLabel.Size = new Size(520, 24);
        var copy = new RoundedButton { Text = "复制环境明细" };
        StyleButton(copy, false, 126);
        copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        copy.Click += (_, _) => CopyEnvironmentReport();
        var close = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel };
        StyleButton(close, false, 88);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        launchButton.Text = "隔离启动";
        StyleButton(launchButton, true, 112);
        launchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        launchButton.Click += (_, _) => Launch();
        void AlignFooter()
        {
            launchButton.Location = new Point(footer.ClientSize.Width - launchButton.Width - 22, 16);
            close.Location = new Point(launchButton.Left - close.Width - 10, 16);
            copy.Location = new Point(close.Left - copy.Width - 10, 16);
            statusLabel.Width = Math.Max(220, copy.Left - statusLabel.Left - 15);
        }
        footer.Resize += (_, _) => AlignFooter();
        footer.Controls.AddRange(new Control[] { statusLabel, copy, close, launchButton });
        AlignFooter();
        root.Controls.Add(footer, 0, 5);
        AcceptButton = launchButton;
        CancelButton = close;
    }

    private Control CreateInputCard()
    {
        var card = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(22, 6, 22, 6), BackColor = Color.White, BorderColor = Border };
        card.Controls.Add(FieldLabel("最近启动", 14));
        recentCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        recentCombo.Location = new Point(122, 10);
        recentCombo.Size = new Size(790, 30);
        recentCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        recentCombo.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        card.Controls.Add(recentCombo);

        card.Controls.Add(FieldLabel("目标程序", 58));
        ConfigureField(executableText, 54, 676);
        executableText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        executableText.TextChanged += (_, _) => UpdatePreview();
        card.Controls.Add(executableText);
        var browseExecutable = new RoundedButton { Text = "选择程序" };
        StyleButton(browseExecutable, false, 104, 32);
        browseExecutable.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseExecutable.Location = new Point(808, 51);
        browseExecutable.Click += (_, _) => BrowseExecutable();
        card.Controls.Add(browseExecutable);

        card.Controls.Add(FieldLabel("启动参数", 102));
        ConfigureField(argumentsText, 98, 790);
        argumentsText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        argumentsText.TextChanged += (_, _) => UpdatePreview();
        card.Controls.Add(argumentsText);

        card.Controls.Add(FieldLabel("工作目录", 146));
        ConfigureField(workingDirectoryText, 142, 676);
        workingDirectoryText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        workingDirectoryText.TextChanged += (_, _) => UpdatePreview();
        card.Controls.Add(workingDirectoryText);
        var browseDirectory = new RoundedButton { Text = "选择目录" };
        StyleButton(browseDirectory, false, 104, 32);
        browseDirectory.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseDirectory.Location = new Point(808, 139);
        browseDirectory.Click += (_, _) => BrowseWorkingDirectory();
        card.Controls.Add(browseDirectory);

        oracleHomeCheck.Text = "同时向目标程序传入 ORACLE_HOME（仅兼容旧程序时使用）";
        oracleHomeCheck.AutoSize = true;
        oracleHomeCheck.Location = new Point(122, 187);
        oracleHomeCheck.Checked = client.ClientType == "完整客户端";
        oracleHomeCheck.CheckedChanged += (_, _) => UpdatePreview();
        card.Controls.Add(oracleHomeCheck);
        card.Resize += (_, _) =>
        {
            recentCombo.Width = Math.Max(420, card.ClientSize.Width - recentCombo.Left - 18);
            browseExecutable.Left = card.ClientSize.Width - browseExecutable.Width - 18;
            executableText.Width = Math.Max(360, browseExecutable.Left - executableText.Left - 10);
            argumentsText.Width = Math.Max(420, card.ClientSize.Width - argumentsText.Left - 18);
            browseDirectory.Left = card.ClientSize.Width - browseDirectory.Width - 18;
            workingDirectoryText.Width = Math.Max(360, browseDirectory.Left - workingDirectoryText.Left - 10);
        };
        return card;
    }

    private void ConfigureEnvironmentGrid()
    {
        environmentGrid.Dock = DockStyle.Fill;
        environmentGrid.BackgroundColor = Color.White;
        environmentGrid.BorderStyle = BorderStyle.None;
        environmentGrid.ReadOnly = true;
        environmentGrid.AllowUserToAddRows = false;
        environmentGrid.AllowUserToDeleteRows = false;
        environmentGrid.AllowUserToResizeRows = false;
        environmentGrid.RowHeadersVisible = false;
        environmentGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        environmentGrid.MultiSelect = false;
        environmentGrid.ShowCellToolTips = false;
        environmentGrid.EnableHeadersVisualStyles = false;
        environmentGrid.ColumnHeadersHeight = 34;
        environmentGrid.RowTemplate.Height = 31;
        environmentGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(236, 242, 249), ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Padding = new Padding(5) };
        environmentGrid.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.FromArgb(51, 65, 85), SelectionBackColor = Color.FromArgb(239, 246, 255), SelectionForeColor = Navy, Padding = new Padding(5) };
        environmentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Variable", HeaderText = "环境变量", Width = 175 });
        environmentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "传入值", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        environmentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "处理方式", Width = 230 });
    }

    private void LoadRecentProfiles()
    {
        loadingFields = true;
        recentCombo.Items.Clear();
        foreach (IsolatedLaunchProfile profile in IsolatedLaunchService.LoadProfiles()) recentCombo.Items.Add(new ProfileItem(profile));
        recentCombo.SelectedIndex = -1;
        loadingFields = false;
    }

    private void LoadSelectedProfile()
    {
        if (loadingFields || recentCombo.SelectedItem is not ProfileItem item) return;
        loadingFields = true;
        executableText.Text = item.Profile.ExecutablePath;
        argumentsText.Text = item.Profile.Arguments;
        workingDirectoryText.Text = item.Profile.WorkingDirectory;
        oracleHomeCheck.Checked = item.Profile.SetOracleHome;
        loadingFields = false;
        UpdatePreview();
    }

    private void BrowseExecutable()
    {
        using var dialog = new OpenFileDialog { Title = "选择要隔离启动的程序", Filter = "Windows 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*", CheckFileExists = true };
        if (File.Exists(executableText.Text)) dialog.InitialDirectory = Path.GetDirectoryName(executableText.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK) SetExecutable(dialog.FileName);
    }

    private void SetExecutable(string path)
    {
        loadingFields = true;
        executableText.Text = path;
        workingDirectoryText.Text = Path.GetDirectoryName(path) ?? string.Empty;
        loadingFields = false;
        UpdatePreview();
    }

    private void BrowseWorkingDirectory()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择目标程序的工作目录", UseDescriptionForTitle = true, ShowNewFolderButton = false, InitialDirectory = Directory.Exists(workingDirectoryText.Text) ? workingDirectoryText.Text : Path.GetDirectoryName(executableText.Text) ?? string.Empty };
        if (dialog.ShowDialog(this) == DialogResult.OK) workingDirectoryText.Text = dialog.SelectedPath;
    }

    private IsolatedLaunchPlan? TryBuildPlan()
    {
        string executable = Environment.ExpandEnvironmentVariables(executableText.Text.Trim().Trim('"'));
        if (!File.Exists(executable)) return null;
        try { return IsolatedLaunchService.BuildPlan(client, executableText.Text, argumentsText.Text, workingDirectoryText.Text, oracleHomeCheck.Checked); }
        catch { return null; }
    }

    private void UpdatePreview()
    {
        if (loadingFields) return;
        IsolatedLaunchPlan? plan = TryBuildPlan();
        environmentGrid.Rows.Clear();
        if (plan is null)
        {
            compatibilityLabel.Text = "请选择有效的 Windows 可执行文件。";
            compatibilityLabel.ForeColor = Color.FromArgb(71, 85, 105);
            statusLabel.Text = "请选择要启动的程序。";
            launchButton.Enabled = false;
            return;
        }

        environmentGrid.Rows.Add("PATH", client.PathDirectory, "置顶客户端，清理其它 Oracle 项");
        environmentGrid.Rows.Add("TNS_ADMIN", plan.TnsAdmin, "仅传给目标程序");
        environmentGrid.Rows.Add("ORACLE_HOME", plan.OracleHome ?? "（不传入）", plan.OracleHome is null ? "清除继承值" : "仅传给目标程序");
        environmentGrid.ClearSelection();
        if (!Directory.Exists(plan.WorkingDirectory))
        {
            compatibilityLabel.Text = "✕ 工作目录不存在，请重新选择。";
            compatibilityLabel.ForeColor = Color.FromArgb(185, 28, 28);
            statusLabel.Text = "工作目录无效。";
            launchButton.Enabled = false;
            return;
        }
        if (!plan.ArchitectureKnown)
        {
            compatibilityLabel.Text = $"▲ 无法识别目标程序位数；Oracle 客户端为 {client.Architecture}，可以继续但请自行确认兼容性。";
            compatibilityLabel.ForeColor = Color.FromArgb(180, 83, 9);
            statusLabel.Text = "目标程序位数未知；确认兼容后可继续隔离启动。";
            launchButton.Enabled = true;
        }
        else if (plan.ArchitectureCompatible)
        {
            compatibilityLabel.Text = $"✓ 位数匹配：目标程序与 Oracle 客户端均为 {plan.TargetArchitecture}。";
            compatibilityLabel.ForeColor = Green;
            statusLabel.Text = "准备就绪；启动不会修改 Windows 全局环境变量。";
            launchButton.Enabled = true;
        }
        else
        {
            compatibilityLabel.Text = $"✕ 位数不匹配：目标程序 {plan.TargetArchitecture} / Oracle {client.Architecture}。请选择相同位数客户端。";
            compatibilityLabel.ForeColor = Color.FromArgb(185, 28, 28);
            statusLabel.Text = "位数不匹配，已阻止启动。";
            launchButton.Enabled = false;
        }
    }

    private void CopyEnvironmentReport()
    {
        IsolatedLaunchPlan? plan = TryBuildPlan();
        if (plan is null) return;
        try
        {
            Clipboard.SetText(IsolatedLaunchService.BuildEnvironmentReport(plan));
            statusLabel.Text = "环境明细已复制。";
        }
        catch (Exception ex) { statusLabel.Text = "复制失败：" + ex.Message; }
    }

    private void Launch()
    {
        IsolatedLaunchPlan? plan = TryBuildPlan();
        if (plan is null) return;
        IsolatedLaunchResult result = IsolatedLaunchService.Launch(plan);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "隔离启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        MessageBox.Show(this, result.Message + "\n\nWindows 全局环境变量没有被修改。", "已启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label FieldLabel(string text, int y) => new() { Text = text, AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Location = new Point(22, y) };

    private static void ConfigureField(TextBox field, int y, int width)
    {
        field.Location = new Point(122, y);
        field.Size = new Size(width, 30);
    }

    private static void StyleButton(Button button, bool primary, int width, int height = 38)
    {
        button.Size = new Size(width, height);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Blue : Color.White;
        button.ForeColor = primary ? Color.White : Navy;
        button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand;
    }

    private sealed class ProfileItem
    {
        public ProfileItem(IsolatedLaunchProfile profile) => Profile = profile;
        public IsolatedLaunchProfile Profile { get; }
        public override string ToString() => $"{Path.GetFileName(Profile.ExecutablePath)}    {Profile.ExecutablePath}";
    }
}
