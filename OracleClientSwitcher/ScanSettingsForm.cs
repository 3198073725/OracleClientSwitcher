namespace OracleClientSwitcher;

internal sealed class ScanSettingsForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly ComboBox modeCombo = new();
    private readonly CheckedListBox drivesList = new();
    private readonly ListBox customList = new();
    private readonly ListBox exclusionsList = new();
    private readonly CheckBox cacheCheck = new();
    private readonly NumericUpDown cacheHours = new();
    private readonly Label modeDescription = new();
    private readonly Label statusLabel = new();
    private readonly OracleScanSettings settings;

    public ScanSettingsForm()
    {
        settings = ScanConfigurationManager.LoadSettings();
        Text = "扫描设置中心";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1040, 720);
        MinimumSize = new Size(940, 650);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadSettings();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, BackColor = Page };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        var header = new GradientPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105) };
        header.Controls.Add(new Label { Text = "扫描设置中心", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), AutoSize = true, Location = new Point(30, 11) });
        header.Controls.Add(new Label { Text = "控制启动扫描范围、速度、排除目录和结果缓存", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(33, 56) });
        root.Controls.Add(header, 0, 0);

        var modeCard = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(22, 13, 22, 5), BackColor = Color.White, BorderColor = Border };
        modeCard.Controls.Add(new Label { Text = "扫描模式", ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold), AutoSize = true, Location = new Point(16, 13) });
        modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        modeCombo.Items.AddRange(new object[] { "快速扫描", "标准扫描", "完整扫描" });
        modeCombo.Location = new Point(105, 9);
        modeCombo.Size = new Size(155, 30);
        modeCombo.SelectedIndexChanged += (_, _) => UpdateModePresentation();
        modeCard.Controls.Add(modeCombo);
        modeDescription.Location = new Point(280, 13);
        modeDescription.AutoSize = true;
        modeDescription.ForeColor = Color.FromArgb(71, 85, 105);
        modeCard.Controls.Add(modeDescription);
        root.Controls.Add(modeCard, 0, 1);

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(22, 6, 22, 8), BackColor = Page };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        columns.Controls.Add(CreateDrivesCard(), 0, 0);
        columns.Controls.Add(CreateDirectoryCard("固定扫描目录", "自动扫描时深入检查这些目录。", customList, AddCustomDirectory), 1, 0);
        columns.Controls.Add(CreateExclusionsCard(), 2, 0);
        root.Controls.Add(columns, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        statusLabel.Text = "保存后旧缓存会自动清除。";
        statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        statusLabel.AutoEllipsis = true;
        statusLabel.Location = new Point(24, 25);
        statusLabel.Size = new Size(600, 24);
        var cancel = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel };
        StyleButton(cancel, false, 88);
        cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var save = new RoundedButton { Text = "保存并重新扫描" };
        StyleButton(save, true, 148);
        save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        save.Click += (_, _) => Save();
        void AlignFooter()
        {
            save.Location = new Point(footer.ClientSize.Width - save.Width - 22, 16);
            cancel.Location = new Point(save.Left - cancel.Width - 10, 16);
            statusLabel.Width = Math.Max(250, cancel.Left - statusLabel.Left - 15);
        }
        footer.Resize += (_, _) => AlignFooter();
        footer.Controls.AddRange(new Control[] { statusLabel, cancel, save });
        AlignFooter();
        root.Controls.Add(footer, 0, 3);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private Control CreateDrivesCard()
    {
        var card = CreateCard("扫描盘符", "快速模式跳过；其他模式使用所选盘。");
        drivesList.Dock = DockStyle.Fill;
        drivesList.CheckOnClick = true;
        drivesList.BorderStyle = BorderStyle.FixedSingle;
        drivesList.Margin = new Padding(14, 4, 14, 14);
        drivesList.Font = new Font("Microsoft YaHei UI", 9.5F);
        ((TableLayoutPanel)card).Controls.Add(drivesList, 0, 2);
        return card;
    }

    private Control CreateDirectoryCard(string title, string description, ListBox list, Action addAction)
    {
        var card = CreateCard(title, description);
        list.Dock = DockStyle.Fill;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.HorizontalScrollbar = true;
        list.Margin = new Padding(14, 4, 14, 4);
        ((TableLayoutPanel)card).Controls.Add(list, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(14, 4, 14, 10) };
        var add = new RoundedButton { Text = "添加目录" };
        StyleButton(add, false, 92, 31);
        add.Click += (_, _) => addAction();
        var remove = new RoundedButton { Text = "移除" };
        StyleButton(remove, false, 72, 31);
        remove.Click += (_, _) => RemoveSelected(list);
        actions.Controls.Add(add);
        actions.Controls.Add(remove);
        ((TableLayoutPanel)card).Controls.Add(actions, 0, 3);
        return card;
    }

    private Control CreateExclusionsCard()
    {
        var card = (TableLayoutPanel)CreateDirectoryCard("排除目录", "跳过这些目录及全部子目录。", exclusionsList, AddExcludedDirectory);
        card.RowCount = 5;
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        var cachePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(14, 4, 14, 10),
            Padding = new Padding(10, 7, 10, 7),
            BackColor = Color.FromArgb(248, 250, 252)
        };
        cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        cachePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        cachePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        cacheCheck.Text = "使用启动缓存";
        cacheCheck.AutoSize = false;
        cacheCheck.Dock = DockStyle.Fill;
        cacheCheck.Margin = new Padding(0, 0, 4, 2);
        cacheCheck.TextAlign = ContentAlignment.MiddleLeft;
        cacheCheck.CheckedChanged += (_, _) => cacheHours.Enabled = cacheCheck.Checked;
        cachePanel.Controls.Add(cacheCheck, 0, 0);

        var clear = new RoundedButton { Text = "清除缓存" };
        StyleButton(clear, false, 112, 32);
        clear.Dock = DockStyle.Fill;
        clear.Margin = new Padding(8, 0, 0, 3);
        clear.Click += (_, _) => { ScanConfigurationManager.ClearCache(); statusLabel.Text = "扫描缓存已清除。"; };
        cachePanel.Controls.Add(clear, 1, 0);

        var durationRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        durationRow.Controls.Add(new Label
        {
            Text = "有效时间",
            AutoSize = true,
            ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(0, 8, 0, 0)
        });
        cacheHours.Minimum = 1;
        cacheHours.Maximum = 168;
        cacheHours.AutoSize = false;
        cacheHours.Size = new Size(88, 30);
        cacheHours.Margin = new Padding(8, 2, 8, 0);
        cacheHours.BorderStyle = BorderStyle.FixedSingle;
        cacheHours.TextAlign = HorizontalAlignment.Center;
        cacheHours.Font = new Font("Microsoft YaHei UI", 9F);
        durationRow.Controls.Add(cacheHours);
        durationRow.Controls.Add(new Label
        {
            Text = "小时",
            AutoSize = true,
            ForeColor = Color.FromArgb(71, 85, 105),
            Margin = new Padding(0, 8, 0, 0)
        });
        cachePanel.Controls.Add(durationRow, 0, 1);
        cachePanel.SetColumnSpan(durationRow, 2);
        card.Controls.Add(cachePanel, 0, 4);
        return card;
    }

    private static TableLayoutPanel CreateCard(string title, string description)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 0, 10, 0), BackColor = Color.White, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), ForeColor = Navy, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 8, 0) }, 0, 0);
        card.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Microsoft YaHei UI", 8.5F), Padding = new Padding(14, 7, 12, 4) }, 0, 1);
        return card;
    }

    private void LoadSettings()
    {
        modeCombo.SelectedIndex = settings.Mode switch { OracleScanMode.Quick => 0, OracleScanMode.Full => 2, _ => 1 };
        IReadOnlyList<string> selectedRoots = settings.SelectedDriveRoots.Count > 0 ? settings.SelectedDriveRoots : ScanConfigurationManager.GetDefaultDriveRoots();
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(x => x.IsReady && (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable)))
        {
            int index = drivesList.Items.Add(new DriveItem(drive));
            drivesList.SetItemChecked(index, selectedRoots.Any(x => string.Equals(Path.GetPathRoot(x), drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)));
        }
        foreach (string path in settings.CustomDirectories) customList.Items.Add(path);
        foreach (string path in settings.ExcludedDirectories) exclusionsList.Items.Add(path);
        cacheCheck.Checked = settings.UseCache;
        cacheHours.Value = Math.Clamp(settings.CacheHours, 1, 168);
        cacheHours.Enabled = cacheCheck.Checked;
        UpdateModePresentation();
    }

    private void UpdateModePresentation()
    {
        OracleScanMode mode = SelectedMode;
        drivesList.Enabled = mode != OracleScanMode.Quick;
        modeDescription.Text = mode switch
        {
            OracleScanMode.Quick => "仅检查 PATH、注册表、固定目录和有效缓存，启动最快。",
            OracleScanMode.Full => "深度遍历所选盘符，耗时较长，适合首次查找或目录较深的客户端。",
            _ => "按有限深度扫描所选盘符，兼顾发现率和启动速度。"
        };
        modeDescription.ForeColor = mode == OracleScanMode.Full ? Color.FromArgb(180, 83, 9) : Color.FromArgb(71, 85, 105);
    }

    private OracleScanMode SelectedMode => modeCombo.SelectedIndex switch { 0 => OracleScanMode.Quick, 2 => OracleScanMode.Full, _ => OracleScanMode.Standard };

    private void AddCustomDirectory() => AddDirectory(customList, "选择每次自动扫描的固定目录");
    private void AddExcludedDirectory() => AddDirectory(exclusionsList, "选择需要排除的目录");

    private void AddDirectory(ListBox list, string description)
    {
        using var dialog = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string selected = OracleScanner.Normalize(dialog.SelectedPath);
        if (!list.Items.Cast<string>().Any(x => string.Equals(OracleScanner.Normalize(x), selected, StringComparison.OrdinalIgnoreCase)))
            list.Items.Add(selected);
    }

    private static void RemoveSelected(ListBox list)
    {
        if (list.SelectedIndex >= 0) list.Items.RemoveAt(list.SelectedIndex);
    }

    private void Save()
    {
        var updated = new OracleScanSettings
        {
            Mode = SelectedMode,
            SelectedDriveRoots = drivesList.CheckedItems.Cast<DriveItem>().Select(x => x.Root).ToList(),
            CustomDirectories = customList.Items.Cast<string>().ToList(),
            ExcludedDirectories = exclusionsList.Items.Cast<string>().ToList(),
            UseCache = cacheCheck.Checked,
            CacheHours = (int)cacheHours.Value
        };
        if (updated.Mode != OracleScanMode.Quick && updated.SelectedDriveRoots.Count == 0 && updated.CustomDirectories.Count == 0 &&
            MessageBox.Show(this, "没有选择盘符或固定目录，自动扫描将只检查 PATH 和注册表。确定继续吗？", "扫描范围为空",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        bool overlap = updated.CustomDirectories.Any(custom => updated.ExcludedDirectories.Any(excluded => IsSameOrChild(custom, excluded))) ||
                       updated.SelectedDriveRoots.Any(root => updated.ExcludedDirectories.Any(excluded => IsSameOrChild(root, excluded)));
        if (overlap && MessageBox.Show(this, "部分扫描范围同时位于排除目录中，这些位置将不会被扫描。确定保存吗？", "扫描范围冲突",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (updated.Mode == OracleScanMode.Full && updated.SelectedDriveRoots.Any(IsSystemDrive) &&
            MessageBox.Show(this, "完整扫描系统盘可能需要较长时间。确定保存此设置吗？", "完整扫描系统盘",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        TnsOperationResult result = ScanConfigurationManager.SaveSettings(updated);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool IsSystemDrive(string root) => string.Equals(
        Path.GetPathRoot(root)?.TrimEnd('\\'),
        (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\'),
        StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChild(string path, string parent)
    {
        string normalizedPath = OracleScanner.Normalize(path);
        string normalizedParent = OracleScanner.Normalize(parent);
        return string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedParent.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void StyleButton(Button button, bool primary, int width, int height = 38)
    {
        button.Size = new Size(width, height);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(148, 163, 184);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Color.FromArgb(239, 246, 255);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(30, 64, 175) : Color.FromArgb(219, 234, 254);
        button.BackColor = primary ? Blue : Color.FromArgb(248, 250, 252);
        button.ForeColor = primary ? Color.White : Navy;
        button.Font = new Font("Microsoft YaHei UI", 9F, primary ? FontStyle.Bold : FontStyle.Regular);
        button.Cursor = Cursors.Hand;
    }

    private sealed class DriveItem
    {
        public DriveItem(DriveInfo drive)
        {
            Root = drive.RootDirectory.FullName;
            string kind = drive.DriveType == DriveType.Removable ? "可移动磁盘" : "固定磁盘";
            long freeGb = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
            Display = $"{Root}  {kind}  ·  可用 {freeGb} GB";
        }
        public string Root { get; }
        private string Display { get; }
        public override string ToString() => Display;
    }
}
