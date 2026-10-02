using System.Diagnostics;

namespace OracleClientSwitcher;

internal sealed class TnsConfigurationForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Green = Color.FromArgb(4, 120, 87);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly OracleClientInfo client;
    private readonly IReadOnlyList<OracleClientInfo> clients;
    private readonly RichTextBox tnsEditor = new();
    private readonly RichTextBox sqlNetEditor = new();
    private readonly ListBox serviceList = new();
    private readonly TextBox serviceSearch = new();
    private readonly TextBox aliasText = new();
    private readonly ComboBox protocolCombo = new();
    private readonly TextBox hostText = new();
    private readonly NumericUpDown portNumber = new();
    private readonly ComboBox connectTypeCombo = new();
    private readonly TextBox connectNameText = new();
    private readonly Label visualHint = new();
    private readonly Button applyServiceButton = new RoundedButton();
    private readonly Button deleteServiceButton = new RoundedButton();
    private readonly DataGridView issuesGrid = new();
    private readonly TabControl tabs = new();
    private readonly CheckBox sharedCheck = new();
    private readonly TextBox sharedPath = new();
    private readonly Label activeDirectoryLabel = new();
    private readonly Label statusLabel = new();
    private readonly System.Windows.Forms.Timer validationTimer = new() { Interval = 450 };
    private string currentDirectory = string.Empty;
    private string loadedTnsText = string.Empty;
    private string loadedSqlNetText = string.Empty;
    private string observedTnsText = string.Empty;
    private string observedSqlNetText = string.Empty;
    private string acceptedSearchText = string.Empty;
    private bool loading;
    private bool refreshingServices;
    private bool refreshServicesAfterValidation;
    private bool populatingVisualFields;
    private bool visualDraftDirty;
    private bool revertingServiceSelection;
    private bool suppressSearchChange;
    private int acceptedServiceIndex = -1;

    public TnsConfigurationForm(OracleClientInfo client, IReadOnlyList<OracleClientInfo> clients)
    {
        this.client = client;
        this.clients = clients;
        Text = "TNS 配置管理中心";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 800);
        MinimumSize = new Size(1000, 720);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadSettingsAndFiles();
        Shown += (_, _) =>
        {
            // 控件句柄创建后 RichTextBox 可能再次规范化换行，以此时文本作为未修改基线。
            validationTimer.Stop();
            loadedTnsText = tnsEditor.Text;
            loadedSqlNetText = sqlNetEditor.Text;
            observedTnsText = tnsEditor.Text;
            observedSqlNetText = sqlNetEditor.Text;
            refreshServicesAfterValidation = false;
            UpdateTabTitles();
        };
        validationTimer.Tick += (_, _) =>
        {
            validationTimer.Stop();
            ValidateEditor();
            if (refreshServicesAfterValidation)
            {
                refreshServicesAfterValidation = false;
                RefreshServiceList();
            }
        };
        FormClosing += (_, e) =>
        {
            if (!visualDraftDirty && !IsDirty) return;
            string message = visualDraftDirty
                ? "当前服务表单或 TNS 配置还有未保存的修改，确定关闭并放弃吗？"
                : "TNS 配置尚未保存，确定关闭吗？";
            DialogResult result = MessageBox.Show(this, message, "未保存的修改",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) e.Cancel = true;
        };
    }

    private bool IsDirty => !string.Equals(tnsEditor.Text, loadedTnsText, StringComparison.Ordinal) ||
                            !string.Equals(sqlNetEditor.Text, loadedSqlNetText, StringComparison.Ordinal);

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Page };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        Controls.Add(root);

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateDirectoryPanel(), 0, 1);
        root.Controls.Add(CreateToolbar(), 0, 2);
        root.Controls.Add(CreateEditors(), 0, 3);
        ConfigureIssuesGrid();
        root.Controls.Add(issuesGrid, 0, 4);
        root.Controls.Add(CreateFooter(), 0, 5);
    }

    private Control CreateHeader()
    {
        var header = new GradientPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105) };
        header.Controls.Add(new Label { Text = "TNS 配置管理中心", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), AutoSize = true, Location = new Point(30, 11) });
        header.Controls.Add(new Label { Text = "编辑、检查、导入和同步 Oracle 网络配置", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(33, 57) });
        var badge = new Label
        {
            Text = $"Oracle {client.Version} · {client.Architecture}",
            ForeColor = Color.White,
            BackColor = Color.FromArgb(43, 78, 124),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(12, 7, 12, 7),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(930, 26)
        };
        void AlignBadge() => badge.Left = Math.Max(700, header.ClientSize.Width - badge.Width - 28);
        header.Resize += (_, _) => AlignBadge();
        badge.SizeChanged += (_, _) => AlignBadge();
        header.Controls.Add(badge);
        AlignBadge();
        return header;
    }

    private Control CreateDirectoryPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 14, 22, 6), BackColor = Page };
        var card = new BorderPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Border };
        sharedCheck.Text = "使用公共 TNS 目录（切换客户端时保持不变）";
        sharedCheck.AutoSize = true;
        sharedCheck.Location = new Point(16, 10);
        sharedCheck.CheckedChanged += (_, _) => sharedPath.Enabled = sharedCheck.Checked;
        sharedPath.Location = new Point(18, 40);
        sharedPath.Size = new Size(760, 30);
        sharedPath.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        var browse = new RoundedButton { Text = "选择目录" };
        StyleButton(browse, false, 96, 32);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Location = new Point(850, 38);
        browse.Click += (_, _) => BrowseSharedDirectory();
        var saveSetting = new RoundedButton { Text = "保存设置" };
        StyleButton(saveSetting, true, 96, 32);
        saveSetting.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        saveSetting.Location = new Point(954, 38);
        saveSetting.Click += (_, _) => SaveSharedSetting();
        activeDirectoryLabel.AutoSize = true;
        activeDirectoryLabel.ForeColor = Color.FromArgb(71, 85, 105);
        activeDirectoryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        activeDirectoryLabel.Location = new Point(780, 12);
        void Align()
        {
            saveSetting.Left = card.ClientSize.Width - saveSetting.Width - 16;
            browse.Left = saveSetting.Left - browse.Width - 8;
            sharedPath.Width = Math.Max(360, browse.Left - sharedPath.Left - 10);
            activeDirectoryLabel.Left = Math.Max(470, card.ClientSize.Width - activeDirectoryLabel.Width - 16);
        }
        card.Resize += (_, _) => Align();
        activeDirectoryLabel.SizeChanged += (_, _) => Align();
        card.Controls.Add(sharedCheck);
        card.Controls.Add(sharedPath);
        card.Controls.Add(browse);
        card.Controls.Add(saveSetting);
        card.Controls.Add(activeDirectoryLabel);
        Align();
        host.Controls.Add(card);
        return host;
    }

    private Control CreateToolbar()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(22, 8, 22, 6), BackColor = Page };
        Button save = ToolbarButton("保存", true, (_, _) => SaveFiles());
        Button reload = ToolbarButton("重新加载", false, (_, _) => ReloadFiles());
        Button check = ToolbarButton("格式检查", false, (_, _) => ValidateEditor());
        Button import = ToolbarButton("导入配置", false, (_, _) => ImportTnsNames());
        Button sync = ToolbarButton("同步到客户端", false, (_, _) => SyncToClients());
        Button advanced = ToolbarButton("高级配置", false, (_, _) => OpenAdvancedEditor());
        Button ping = ToolbarButton("TNSPING", false, async (_, _) => await RunTnsPingAsync());
        Button portTest = ToolbarButton("端口测试", false, async (_, _) => await TestSelectedPortsAsync());
        Button folder = ToolbarButton("打开目录", false, (_, _) => OpenDirectory());
        toolbar.Controls.AddRange(new Control[] { save, reload, check, import, sync, advanced, ping, portTest, folder });
        return toolbar;
    }

    private Control CreateEditors()
    {
        tabs.Dock = DockStyle.Fill;
        tabs.Margin = new Padding(22, 4, 22, 6);
        tabs.Font = new Font("Microsoft YaHei UI", 9F);
        var visualTab = new TabPage("可视化服务") { BackColor = Color.White, Padding = new Padding(6) };
        var tnsTab = new TabPage("高级源码") { BackColor = Color.White, Padding = new Padding(6) };
        var sqlTab = new TabPage("sqlnet.ora") { BackColor = Color.White, Padding = new Padding(6) };
        ConfigureEditor(tnsEditor);
        ConfigureEditor(sqlNetEditor);
        tnsEditor.TextChanged += (_, _) => TnsEditorChanged();
        sqlNetEditor.TextChanged += (_, _) => SqlNetEditorChanged();
        visualTab.Controls.Add(CreateVisualEditor());
        tnsTab.Controls.Add(tnsEditor);
        sqlTab.Controls.Add(sqlNetEditor);
        tabs.TabPages.Add(visualTab);
        tabs.TabPages.Add(tnsTab);
        tabs.TabPages.Add(sqlTab);
        tabs.Selecting += (_, e) =>
        {
            if (tabs.SelectedIndex != 0 || e.TabPageIndex == 0 || !visualDraftDirty) return;
            if (!ConfirmDiscardVisualDraft("切换到其他编辑页")) e.Cancel = true;
            else ResetVisualDraftView();
        };
        tabs.Selected += (_, e) =>
        {
            if (e.TabPageIndex != 0 || !refreshServicesAfterValidation) return;
            validationTimer.Stop();
            refreshServicesAfterValidation = false;
            ValidateEditor();
            RefreshServiceList();
        };
        return tabs;
    }

    private Control CreateVisualEditor()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Border,
            IsSplitterFixed = false
        };
        split.SizeChanged += (_, _) =>
        {
            if (split.Width > 850 && split.SplitterDistance < 280)
                split.SplitterDistance = Math.Min(320, split.Width - 524);
        };
        split.Panel1.BackColor = Color.White;
        split.Panel2.BackColor = Color.White;

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8), BackColor = Color.White };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        left.Controls.Add(new Label { Text = "连接服务", ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 5, 3, 3) }, 0, 0);
        serviceSearch.Dock = DockStyle.Fill;
        serviceSearch.Margin = new Padding(2, 2, 2, 5);
        serviceSearch.PlaceholderText = "搜索服务名…";
        serviceSearch.TextChanged += (_, _) => SearchTextChanged();
        left.Controls.Add(serviceSearch, 0, 1);
        serviceList.Dock = DockStyle.Fill;
        serviceList.BorderStyle = BorderStyle.FixedSingle;
        serviceList.Font = new Font("Microsoft YaHei UI", 9.5F);
        serviceList.SelectedIndexChanged += (_, _) => ServiceSelectionChanged();
        left.Controls.Add(serviceList, 0, 2);
        var listActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        Button add = SmallButton("新增", (_, _) => BeginNewService());
        Button copy = SmallButton("复制", (_, _) => DuplicateSelectedService());
        deleteServiceButton.Text = "删除";
        StyleButton(deleteServiceButton, false, 78, 31);
        deleteServiceButton.Margin = new Padding(0, 0, 7, 0);
        deleteServiceButton.Click += (_, _) => DeleteSelectedService();
        listActions.Controls.Add(add);
        listActions.Controls.Add(copy);
        listActions.Controls.Add(deleteServiceButton);
        left.Controls.Add(listActions, 0, 3);
        split.Panel1.Controls.Add(left);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(26, 12, 26, 12), AutoScroll = true };
        right.Controls.Add(new Label { Text = "服务连接信息", ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), AutoSize = true, Location = new Point(25, 10) });
        right.Controls.Add(FieldLabel("服务名", 45));
        ConfigureField(aliasText, 66, 230);
        right.Controls.Add(aliasText);
        right.Controls.Add(FieldLabel("主机或 IP", 45, 280));
        ConfigureField(hostText, 66, 330, 280);
        right.Controls.Add(hostText);
        right.Controls.Add(FieldLabel("协议", 103));
        protocolCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        protocolCombo.Items.AddRange(new object[] { "TCP", "TCPS" });
        protocolCombo.SelectedIndex = 0;
        protocolCombo.Location = new Point(26, 124);
        protocolCombo.Size = new Size(130, 30);
        right.Controls.Add(protocolCombo);
        right.Controls.Add(FieldLabel("端口", 103, 180));
        portNumber.Minimum = 1;
        portNumber.Maximum = 65535;
        portNumber.Value = 1521;
        portNumber.Location = new Point(180, 124);
        portNumber.Size = new Size(125, 30);
        right.Controls.Add(portNumber);
        right.Controls.Add(FieldLabel("连接标识类型", 103, 330));
        connectTypeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        connectTypeCombo.Items.AddRange(new object[] { "SERVICE_NAME", "SID" });
        connectTypeCombo.SelectedIndex = 0;
        connectTypeCombo.Location = new Point(330, 124);
        connectTypeCombo.Size = new Size(180, 30);
        right.Controls.Add(connectTypeCombo);
        right.Controls.Add(FieldLabel("服务标识", 164));
        ConfigureField(connectNameText, 185, 350);
        right.Controls.Add(connectNameText);
        visualHint.Location = new Point(198, 12);
        visualHint.Size = new Size(560, 28);
        visualHint.ForeColor = Color.FromArgb(71, 85, 105);
        visualHint.AutoEllipsis = true;
        right.Controls.Add(visualHint);
        applyServiceButton.Text = "应用到配置";
        StyleButton(applyServiceButton, true, 126, 38);
        applyServiceButton.Location = new Point(392, 181);
        applyServiceButton.Click += (_, _) => ApplyVisualService();
        right.Controls.Add(applyServiceButton);
        aliasText.TextChanged += (_, _) => VisualFieldChanged();
        hostText.TextChanged += (_, _) => VisualFieldChanged();
        protocolCombo.SelectedIndexChanged += (_, _) => VisualFieldChanged();
        portNumber.ValueChanged += (_, _) => VisualFieldChanged();
        connectTypeCombo.SelectedIndexChanged += (_, _) => VisualFieldChanged();
        connectNameText.TextChanged += (_, _) => VisualFieldChanged();
        split.Panel2.Controls.Add(right);
        return split;
    }

    private void ConfigureIssuesGrid()
    {
        issuesGrid.Dock = DockStyle.Fill;
        issuesGrid.Margin = new Padding(22, 4, 22, 8);
        issuesGrid.BackgroundColor = Color.White;
        issuesGrid.BorderStyle = BorderStyle.FixedSingle;
        issuesGrid.AllowUserToAddRows = false;
        issuesGrid.AllowUserToDeleteRows = false;
        issuesGrid.AllowUserToResizeRows = false;
        issuesGrid.ReadOnly = true;
        issuesGrid.RowHeadersVisible = false;
        issuesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        issuesGrid.MultiSelect = false;
        issuesGrid.EnableHeadersVisualStyles = false;
        issuesGrid.ColumnHeadersHeight = 36;
        issuesGrid.RowTemplate.Height = 34;
        issuesGrid.DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Microsoft YaHei UI", 9F), SelectionBackColor = Color.FromArgb(239, 246, 255), SelectionForeColor = Navy, Padding = new Padding(5) };
        issuesGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(236, 242, 249), ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Padding = new Padding(5) };
        issuesGrid.Columns.Add("Severity", "检查结果");
        issuesGrid.Columns[0].Width = 110;
        issuesGrid.Columns.Add("Line", "行号");
        issuesGrid.Columns[1].Width = 80;
        issuesGrid.Columns.Add("Message", "说明");
        issuesGrid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        issuesGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || issuesGrid.Rows[e.RowIndex].Tag is not TnsValidationIssue issue || issue.Line <= 0) return;
            tabs.SelectedIndex = 1;
            int index = tnsEditor.GetFirstCharIndexFromLine(Math.Min(issue.Line - 1, tnsEditor.Lines.Length - 1));
            if (index >= 0) { tnsEditor.SelectionStart = index; tnsEditor.ScrollToCaret(); tnsEditor.Focus(); }
        };
    }

    private Control CreateFooter()
    {
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        statusLabel.Text = "就绪";
        statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        statusLabel.AutoEllipsis = true;
        statusLabel.Location = new Point(24, 24);
        statusLabel.Size = new Size(780, 25);
        statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        var close = new RoundedButton { Text = "关闭", DialogResult = DialogResult.OK };
        StyleButton(close, true, 94, 38);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Location = new Point(1040, 14);
        void Align()
        {
            close.Left = footer.ClientSize.Width - close.Width - 22;
            statusLabel.Width = Math.Max(200, close.Left - statusLabel.Left - 18);
        }
        footer.Resize += (_, _) => Align();
        footer.Controls.Add(statusLabel);
        footer.Controls.Add(close);
        Align();
        CancelButton = close;
        return footer;
    }

    private void LoadSettingsAndFiles()
    {
        OracleSwitcherSettings settings = TnsConfigurationManager.LoadSettings();
        loading = true;
        sharedCheck.Checked = settings.UseSharedTnsDirectory;
        sharedPath.Text = settings.SharedTnsDirectory ?? client.TnsAdmin;
        sharedPath.Enabled = sharedCheck.Checked;
        loading = false;
        currentDirectory = TnsConfigurationManager.GetEffectiveDirectory(client);
        LoadFiles();
    }

    private void LoadFiles()
    {
        validationTimer.Stop();
        refreshServicesAfterValidation = false;
        visualDraftDirty = false;
        loading = true;
        loadedTnsText = TnsConfigurationManager.LoadFile(currentDirectory, "tnsnames.ora");
        loadedSqlNetText = TnsConfigurationManager.LoadFile(currentDirectory, "sqlnet.ora");
        tnsEditor.Text = loadedTnsText;
        sqlNetEditor.Text = loadedSqlNetText;
        // RichTextBox 会统一换行符；以控件中的实际文本作为干净基线，避免刚加载就误判为已修改。
        loadedTnsText = tnsEditor.Text;
        loadedSqlNetText = sqlNetEditor.Text;
        observedTnsText = tnsEditor.Text;
        observedSqlNetText = sqlNetEditor.Text;
        loading = false;
        activeDirectoryLabel.Text = sharedCheck.Checked ? "当前：公共目录" : "当前：客户端目录";
        statusLabel.Text = currentDirectory;
        UpdateTabTitles();
        ValidateEditor();
        RefreshServiceList();
    }

    private void ReloadFiles()
    {
        if (IsDirty && MessageBox.Show(this, "重新加载会丢弃尚未保存的修改，是否继续？", "重新加载",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (!ConfirmDiscardVisualDraft("重新加载配置")) return;
        LoadFiles();
    }

    private bool SaveFiles()
    {
        if (!PrepareVisualDraftForSave()) return false;
        IReadOnlyList<TnsValidationIssue> issues = TnsConfigurationManager.ValidateTnsNames(tnsEditor.Text);
        if (issues.Any(x => x.Severity == TnsIssueSeverity.Error) &&
            MessageBox.Show(this, "tnsnames.ora 仍有格式错误，确定仍要保存吗？", "格式检查未通过",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;

        var messages = new List<string>();
        bool success = true;
        if (!string.Equals(tnsEditor.Text, loadedTnsText, StringComparison.Ordinal))
        {
            TnsOperationResult result = TnsConfigurationManager.SaveFile(currentDirectory, "tnsnames.ora", tnsEditor.Text);
            messages.Add(result.Message);
            success &= result.Success;
            if (result.Success) loadedTnsText = tnsEditor.Text;
        }
        if (!string.Equals(sqlNetEditor.Text, loadedSqlNetText, StringComparison.Ordinal))
        {
            TnsOperationResult result = TnsConfigurationManager.SaveFile(currentDirectory, "sqlnet.ora", sqlNetEditor.Text);
            messages.Add(result.Message);
            success &= result.Success;
            if (result.Success) loadedSqlNetText = sqlNetEditor.Text;
        }
        if (messages.Count == 0) messages.Add("没有需要保存的修改。");
        statusLabel.Text = string.Join("  ", messages);
        AppLog.Information("TNS", "保存网络配置", success ? "成功" : "失败", string.Join(" | ", messages));
        UpdateTabTitles();
        ValidateEditor();
        if (!success) MessageBox.Show(this, string.Join(Environment.NewLine, messages), "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return success && !IsDirty;
    }

    private void ValidateEditor()
    {
        IReadOnlyList<TnsValidationIssue> issues = TnsConfigurationManager.ValidateTnsNames(tnsEditor.Text);
        issuesGrid.Rows.Clear();
        foreach (TnsValidationIssue issue in issues)
        {
            string severity = issue.Severity switch { TnsIssueSeverity.Error => "✕ 错误", TnsIssueSeverity.Warning => "▲ 警告", _ => "● 通过" };
            int row = issuesGrid.Rows.Add(severity, issue.Line == 0 ? "—" : issue.Line.ToString(), issue.Message);
            issuesGrid.Rows[row].Tag = issue;
            issuesGrid.Rows[row].Cells[0].Style.ForeColor = issue.Severity switch
            {
                TnsIssueSeverity.Error => Color.FromArgb(185, 28, 28),
                TnsIssueSeverity.Warning => Color.FromArgb(180, 83, 9),
                _ => Green
            };
        }
        issuesGrid.ClearSelection();
    }

    private void ImportTnsNames()
    {
        using var dialog = new OpenFileDialog { Title = "选择要导入的 tnsnames.ora", Filter = "Oracle TNS 配置 (tnsnames.ora;*.ora)|tnsnames.ora;*.ora|所有文件 (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string imported;
        try { imported = File.ReadAllText(dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法读取配置", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        IReadOnlyList<TnsValidationIssue> importedIssues = TnsConfigurationManager.ValidateTnsNames(imported);
        if (importedIssues.Any(x => x.Severity == TnsIssueSeverity.Error))
        {
            MessageBox.Show(this, "导入文件存在括号、引号或重复别名错误，请先修复后再导入。", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (!ConfirmDiscardVisualDraft("导入其他 TNS 配置")) return;
        DialogResult choice = MessageBox.Show(this,
            "选择“是”将只合并当前文件中不存在的服务；选择“否”将用导入内容替换编辑器；选择“取消”放弃。",
            "导入 tnsnames.ora", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;
        if (choice == DialogResult.No)
        {
            SetTnsEditorContent(imported);
            statusLabel.Text = "已载入导入文件，尚未保存。";
        }
        else
        {
            string merged = TnsConfigurationManager.MergeTnsNames(tnsEditor.Text, imported, out int added, out int skipped);
            SetTnsEditorContent(merged);
            statusLabel.Text = $"导入合并完成：新增 {added} 个服务，跳过 {skipped} 个已存在服务；尚未保存。";
        }
        tabs.SelectedIndex = 0;
    }

    private void SyncToClients()
    {
        if (visualDraftDirty || IsDirty)
        {
            DialogResult choice = MessageBox.Show(this, "同步前需要保存当前修改。现在保存吗？", "同步 TNS", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return;
            if (!SaveFiles()) return;
        }
        using var dialog = new TnsSyncForm(client, clients);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        IReadOnlyList<TnsOperationResult> results = TnsConfigurationManager.SyncConfiguration(currentDirectory, dialog.SelectedClients, dialog.SyncTnsNames, dialog.SyncSqlNet);
        int succeeded = results.Count(x => x.Success);
        int failed = results.Count - succeeded;
        string report = string.Join(Environment.NewLine, results.Select(x => (x.Success ? "✓ " : "✕ ") + x.Message));
        MessageBox.Show(this, $"同步完成：成功 {succeeded}，失败 {failed}\n\n{report}", "同步 TNS",
            MessageBoxButtons.OK, failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        statusLabel.Text = $"同步完成：成功 {succeeded}，失败 {failed}。";
    }

    private void BrowseSharedDirectory()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择公共 TNS 配置目录", UseDescriptionForTitle = true, ShowNewFolderButton = true, InitialDirectory = Directory.Exists(sharedPath.Text) ? sharedPath.Text : client.TnsAdmin };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            sharedPath.Text = dialog.SelectedPath;
            sharedCheck.Checked = true;
        }
    }

    private void SaveSharedSetting()
    {
        if (IsDirty && MessageBox.Show(this, "切换配置目录会丢弃未保存的编辑内容，是否继续？", "保存公共目录设置", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (!ConfirmDiscardVisualDraft("切换 TNS 配置目录")) return;
        TnsOperationResult result = TnsConfigurationManager.SaveSettings(sharedCheck.Checked, sharedPath.Text);
        MessageBox.Show(this, result.Message, result.Success ? "设置已保存" : "保存失败", MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        if (!result.Success) return;
        currentDirectory = TnsConfigurationManager.GetEffectiveDirectory(client);
        LoadFiles();
    }

    private void OpenDirectory()
    {
        try
        {
            Directory.CreateDirectory(currentDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", currentDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开目录", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenAdvancedEditor()
    {
        if (!ConfirmDiscardVisualDraft("打开高级配置")) return;
        ServiceListItem? selected = serviceList.SelectedItem as ServiceListItem;
        TnsAdvancedDefinition definition = selected is not null
            ? TnsConfigurationManager.ToAdvancedDefinition(selected.Service)
            : new TnsAdvancedDefinition
            {
                Alias = aliasText.Text.Trim(),
                ConnectName = connectNameText.Text.Trim(),
                UsesSid = connectTypeCombo.Text == "SID",
                Endpoints = new List<TnsEndpoint>
                {
                    new() { Protocol = protocolCombo.Text.Length == 0 ? "TCP" : protocolCombo.Text, Host = hostText.Text.Trim(), Port = (int)portNumber.Value }
                }
            };
        using var dialog = new AdvancedTnsServiceForm(definition);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        bool duplicate = TnsConfigurationManager.ReadAliases(tnsEditor.Text).Any(x =>
            string.Equals(x, dialog.ResultAlias, StringComparison.OrdinalIgnoreCase) &&
            (selected is null || !string.Equals(x, selected.Service.Alias, StringComparison.OrdinalIgnoreCase)));
        if (duplicate)
        {
            MessageBox.Show(this, $"服务名“{dialog.ResultAlias}”已经存在。", "服务名重复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            string updated = selected is null
                ? (tnsEditor.Text.TrimEnd().Length == 0 ? dialog.ResultText + "\r\n" : tnsEditor.Text.TrimEnd() + "\r\n\r\n" + dialog.ResultText + "\r\n")
                : TnsConfigurationManager.ReplaceEntry(tnsEditor.Text, selected.Service.SourceEntry, dialog.ResultText);
            SetTnsEditorContent(updated, dialog.ResultAlias);
            statusLabel.Text = $"已应用 {dialog.ResultAlias} 的高级配置，尚未保存。";
            AppLog.Information("TNS", "应用高级配置", "成功", dialog.ResultAlias);
        }
        catch (Exception ex)
        {
            AppLog.Error("TNS", "应用高级配置", ex);
            MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunTnsPingAsync()
    {
        if (serviceList.SelectedItem is not ServiceListItem selected)
        {
            MessageBox.Show(this, "请先选择一个服务。", "TNSPING", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if ((visualDraftDirty || IsDirty) && !SaveFiles()) return;
        statusLabel.Text = $"正在执行 tnsping {selected.Service.Alias}…";
        ExternalProcessResult result = await TnsConnectivityService.RunTnsPingAsync(client, selected.Service.Alias, currentDirectory);
        bool success = result.Started && !result.TimedOut && result.ExitCode == 0;
        string report = (result.Output + Environment.NewLine + result.Error).Trim();
        if (report.Length == 0) report = success ? "tnsping 执行成功。" : "tnsping 没有返回输出。";
        AppLog.Information("TNS", "TNSPING", success ? "成功" : "失败", report);
        MessageBox.Show(this, report, $"TNSPING · {selected.Service.Alias}", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        statusLabel.Text = success ? "TNSPING 成功。" : "TNSPING 未通过，请查看返回信息。";
    }

    private async Task TestSelectedPortsAsync()
    {
        if (serviceList.SelectedItem is not ServiceListItem selected)
        {
            MessageBox.Show(this, "请先选择一个服务。", "端口测试", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        IReadOnlyList<TnsEndpoint> endpoints = selected.Service.Endpoints;
        if (endpoints.Count == 0)
        {
            MessageBox.Show(this, "该服务中没有解析到可测试的主机地址。", "端口测试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        statusLabel.Text = "正在测试主机端口…";
        IReadOnlyList<TnsOperationResult> results = await TnsConnectivityService.TestEndpointsAsync(endpoints);
        string report = string.Join(Environment.NewLine, results.Select(x => (x.Success ? "✓ " : "✕ ") + x.Message));
        AppLog.Information("TNS", "测试主机端口", $"成功 {results.Count(x => x.Success)}/{results.Count}", report);
        MessageBox.Show(this, report, "端口测试结果", MessageBoxButtons.OK, results.All(x => x.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        statusLabel.Text = $"端口测试完成：{results.Count(x => x.Success)}/{results.Count} 可达。";
    }

    private void TnsEditorChanged()
    {
        if (loading) return;
        if (string.Equals(tnsEditor.Text, observedTnsText, StringComparison.Ordinal)) return;
        observedTnsText = tnsEditor.Text;
        UpdateTabTitles();
        refreshServicesAfterValidation = true;
        validationTimer.Stop();
        validationTimer.Start();
    }

    private void SqlNetEditorChanged()
    {
        if (loading) return;
        if (string.Equals(sqlNetEditor.Text, observedSqlNetText, StringComparison.Ordinal)) return;
        observedSqlNetText = sqlNetEditor.Text;
        UpdateTabTitles();
    }

    private void UpdateTabTitles()
    {
        if (tabs.TabPages.Count < 3) return;
        tabs.TabPages[0].Text = "可视化服务";
        tabs.TabPages[1].Text = "高级源码" + (!string.Equals(tnsEditor.Text, loadedTnsText, StringComparison.Ordinal) ? "  *" : string.Empty);
        tabs.TabPages[2].Text = "sqlnet.ora" + (!string.Equals(sqlNetEditor.Text, loadedSqlNetText, StringComparison.Ordinal) ? "  *" : string.Empty);
    }

    private void SearchTextChanged()
    {
        if (suppressSearchChange) return;
        if (visualDraftDirty && !ConfirmDiscardVisualDraft("筛选其他服务"))
        {
            suppressSearchChange = true;
            serviceSearch.Text = acceptedSearchText;
            serviceSearch.SelectionStart = serviceSearch.TextLength;
            suppressSearchChange = false;
            return;
        }
        acceptedSearchText = serviceSearch.Text;
        RefreshServiceList();
    }

    private void ServiceSelectionChanged()
    {
        if (refreshingServices || revertingServiceSelection) return;
        int requestedIndex = serviceList.SelectedIndex;
        if (visualDraftDirty && !ConfirmDiscardVisualDraft("选择其他服务"))
        {
            revertingServiceSelection = true;
            serviceList.SelectedIndex = acceptedServiceIndex;
            revertingServiceSelection = false;
            return;
        }
        acceptedServiceIndex = requestedIndex;
        ShowSelectedService();
    }

    private void RefreshServiceList(string? selectAlias = null)
    {
        if (refreshingServices) return;
        refreshingServices = true;
        try
        {
            selectAlias ??= (serviceList.SelectedItem as ServiceListItem)?.Service.Alias;
            string filter = serviceSearch.Text.Trim();
            List<TnsServiceDefinition> services = TnsConfigurationManager.ParseAliasEntries(tnsEditor.Text)
                .Select(TnsConfigurationManager.ParseService)
                .Where(x => filter.Length == 0 || x.Alias.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.ConnectName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
                .ToList();

            serviceList.BeginUpdate();
            serviceList.Items.Clear();
            foreach (TnsServiceDefinition service in services) serviceList.Items.Add(new ServiceListItem(service));
            serviceList.EndUpdate();

            int selectedIndex = selectAlias is null
                ? (serviceList.Items.Count > 0 ? 0 : -1)
                : serviceList.Items.Cast<ServiceListItem>().ToList().FindIndex(x =>
                    string.Equals(x.Service.Alias, selectAlias, StringComparison.OrdinalIgnoreCase));
            if (selectedIndex < 0 && serviceList.Items.Count > 0) selectedIndex = 0;
            serviceList.SelectedIndex = selectedIndex;
            acceptedServiceIndex = selectedIndex;
        }
        finally
        {
            refreshingServices = false;
        }

        if (serviceList.SelectedItem is ServiceListItem) ShowSelectedService();
        else if (serviceList.Items.Count == 0 && string.IsNullOrWhiteSpace(serviceSearch.Text)) BeginNewService(requireConfirmation: false);
        else if (serviceList.Items.Count == 0)
        {
            PopulateVisualFields(() =>
            {
                aliasText.Clear();
                hostText.Clear();
                connectNameText.Clear();
            });
            visualDraftDirty = false;
            SetVisualFieldsEnabled(false);
            deleteServiceButton.Enabled = false;
            visualHint.Text = "没有找到匹配的服务；可清空搜索词，或点击“新增”创建连接。";
            visualHint.ForeColor = Color.FromArgb(71, 85, 105);
        }
    }

    private void ShowSelectedService()
    {
        if (refreshingServices || serviceList.SelectedItem is not ServiceListItem item) return;
        TnsServiceDefinition service = item.Service;
        PopulateVisualFields(() =>
        {
            aliasText.Text = service.Alias;
            hostText.Text = service.Host;
            protocolCombo.SelectedItem = protocolCombo.Items.Cast<string>()
                .FirstOrDefault(x => string.Equals(x, service.Protocol, StringComparison.OrdinalIgnoreCase)) ?? "TCP";
            portNumber.Value = Math.Clamp(service.Port, (int)portNumber.Minimum, (int)portNumber.Maximum);
            connectTypeCombo.SelectedItem = service.UsesSid ? "SID" : "SERVICE_NAME";
            connectNameText.Text = service.ConnectName;
        });
        visualDraftDirty = false;
        SetVisualFieldsEnabled(service.IsSimpleEditable);
        deleteServiceButton.Enabled = true;
        if (service.IsSimpleEditable)
        {
            visualHint.Text = "修改后点击“应用到配置”，确认无误后再使用顶部“保存”写入文件。";
            visualHint.ForeColor = Color.FromArgb(71, 85, 105);
        }
        else
        {
            visualHint.Text = service.Limitation;
            visualHint.ForeColor = Color.FromArgb(180, 83, 9);
        }
    }

    private void BeginNewService(bool requireConfirmation = true)
    {
        if (requireConfirmation && !ConfirmDiscardVisualDraft("新建服务")) return;
        refreshingServices = true;
        serviceList.ClearSelected();
        acceptedServiceIndex = -1;
        refreshingServices = false;
        PopulateVisualFields(() =>
        {
            aliasText.Clear();
            hostText.Clear();
            protocolCombo.SelectedItem = "TCP";
            portNumber.Value = 1521;
            connectTypeCombo.SelectedItem = "SERVICE_NAME";
            connectNameText.Clear();
        });
        visualDraftDirty = false;
        SetVisualFieldsEnabled(true);
        deleteServiceButton.Enabled = false;
        visualHint.Text = "填写连接信息后应用到配置；只有点击顶部“保存”才会写入 tnsnames.ora。";
        visualHint.ForeColor = Color.FromArgb(71, 85, 105);
        aliasText.Focus();
    }

    private void DuplicateSelectedService()
    {
        if (serviceList.SelectedItem is not ServiceListItem item) return;
        if (!ConfirmDiscardVisualDraft("复制服务")) return;
        if (!item.Service.IsSimpleEditable)
        {
            MessageBox.Show(this, item.Service.Limitation, "无法直接复制", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        TnsServiceDefinition service = item.Service;
        BeginNewService(requireConfirmation: false);
        PopulateVisualFields(() =>
        {
            aliasText.Text = service.Alias + "_COPY";
            hostText.Text = service.Host;
            protocolCombo.SelectedItem = service.Protocol;
            portNumber.Value = service.Port;
            connectTypeCombo.SelectedItem = service.UsesSid ? "SID" : "SERVICE_NAME";
            connectNameText.Text = service.ConnectName;
        });
        visualDraftDirty = true;
        visualHint.Text = "已复制连接信息，请修改服务名后应用到配置。";
        aliasText.SelectAll();
    }

    private void DeleteSelectedService()
    {
        if (serviceList.SelectedItem is not ServiceListItem item) return;
        if (!ConfirmDiscardVisualDraft("删除服务")) return;
        if (MessageBox.Show(this, $"确定从配置中删除服务“{item.Service.Alias}”吗？\n此操作在点击顶部“保存”前不会写入文件。",
                "删除 TNS 服务", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            string updated = TnsConfigurationManager.ReplaceEntry(tnsEditor.Text, item.Service.SourceEntry, null);
            SetTnsEditorContent(updated);
            statusLabel.Text = $"已从编辑内容删除 {item.Service.Alias}，尚未保存。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ApplyVisualService()
    {
        string alias = aliasText.Text.Trim();
        string host = hostText.Text.Trim();
        string connectName = connectNameText.Text.Trim();
        if (alias.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(alias, @"^[A-Za-z0-9_.-]+$"))
        {
            MessageBox.Show(this, "服务名只能包含字母、数字、下划线、点和短横线。", "服务名无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            aliasText.Focus();
            return false;
        }
        if (host.Length == 0 || connectName.Length == 0)
        {
            MessageBox.Show(this, "请填写主机或 IP，以及服务标识。", "连接信息不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        ServiceListItem? selected = serviceList.SelectedItem as ServiceListItem;
        if (selected is not null && !selected.Service.IsSimpleEditable)
        {
            MessageBox.Show(this, selected.Service.Limitation, "请使用高级源码", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        bool duplicate = TnsConfigurationManager.ReadAliases(tnsEditor.Text).Any(x =>
            string.Equals(x, alias, StringComparison.OrdinalIgnoreCase) &&
            (selected is null || !string.Equals(x, selected.Service.Alias, StringComparison.OrdinalIgnoreCase)));
        if (duplicate)
        {
            MessageBox.Show(this, $"服务名“{alias}”已经存在，请使用其他名称。", "服务名重复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            aliasText.Focus();
            return false;
        }

        string block = TnsConfigurationManager.BuildSimpleService(alias, protocolCombo.Text, host,
            (int)portNumber.Value, connectName, connectTypeCombo.Text == "SID");
        try
        {
            if (selected is null)
            {
                string current = tnsEditor.Text.TrimEnd();
                string updated = current.Length == 0 ? block + "\r\n" : current + "\r\n\r\n" + block + "\r\n";
                SetTnsEditorContent(updated, alias);
                statusLabel.Text = $"已新增服务 {alias}，尚未保存。";
            }
            else
            {
                string updated = TnsConfigurationManager.ReplaceEntry(tnsEditor.Text, selected.Service.SourceEntry, block);
                SetTnsEditorContent(updated, alias);
                statusLabel.Text = $"已更新服务 {alias}，尚未保存。";
            }
            visualDraftDirty = false;
            tabs.SelectedIndex = 0;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void VisualFieldChanged()
    {
        if (populatingVisualFields || refreshingServices || loading) return;
        visualDraftDirty = true;
        visualHint.Text = "有尚未应用的表单修改；服务列表不会刷新。点击“应用到配置”后再保存文件。";
        visualHint.ForeColor = Color.FromArgb(180, 83, 9);
    }

    private void PopulateVisualFields(Action populate)
    {
        populatingVisualFields = true;
        try { populate(); }
        finally { populatingVisualFields = false; }
    }

    private bool ConfirmDiscardVisualDraft(string action)
    {
        if (!visualDraftDirty) return true;
        DialogResult result = MessageBox.Show(this,
            $"当前服务表单有尚未应用的修改。\n\n要放弃这些修改并{action}吗？",
            "尚未应用的表单修改", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return false;
        visualDraftDirty = false;
        return true;
    }

    private bool PrepareVisualDraftForSave()
    {
        if (!visualDraftDirty) return true;
        DialogResult result = MessageBox.Show(this,
            "当前服务表单有尚未应用的修改。\n\n选择“是”先应用表单并保存；选择“否”放弃表单修改，仅保存已经应用的配置。",
            "保存 TNS 配置", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes) return ApplyVisualService();
        visualDraftDirty = false;
        ResetVisualDraftView();
        return true;
    }

    private void ResetVisualDraftView()
    {
        if (serviceList.SelectedItem is ServiceListItem) ShowSelectedService();
        else BeginNewService(requireConfirmation: false);
    }

    private void SetTnsEditorContent(string content, string? selectAlias = null)
    {
        validationTimer.Stop();
        refreshServicesAfterValidation = false;
        visualDraftDirty = false;
        loading = true;
        try { tnsEditor.Text = content; }
        finally { loading = false; }
        observedTnsText = tnsEditor.Text;
        UpdateTabTitles();
        ValidateEditor();
        RefreshServiceList(selectAlias);
    }

    private void SetVisualFieldsEnabled(bool enabled)
    {
        aliasText.Enabled = enabled;
        hostText.Enabled = enabled;
        protocolCombo.Enabled = enabled;
        portNumber.Enabled = enabled;
        connectTypeCombo.Enabled = enabled;
        connectNameText.Enabled = enabled;
        applyServiceButton.Enabled = enabled;
    }

    private Button SmallButton(string text, EventHandler click)
    {
        var button = new RoundedButton { Text = text };
        StyleButton(button, false, 78, 31);
        button.Margin = new Padding(0, 0, 7, 0);
        button.Click += click;
        return button;
    }

    private static Label FieldLabel(string text, int y, int x = 26) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.FromArgb(71, 85, 105),
        Location = new Point(x, y)
    };

    private static void ConfigureField(TextBox field, int y, int width, int x = 26)
    {
        field.Location = new Point(x, y);
        field.Size = new Size(width, 30);
    }

    private sealed class ServiceListItem
    {
        public ServiceListItem(TnsServiceDefinition service) => Service = service;
        public TnsServiceDefinition Service { get; }
        public override string ToString() => Service.IsSimpleEditable ? Service.Alias : $"{Service.Alias}   [高级]";
    }

    private Button ToolbarButton(string text, bool primary, EventHandler click)
    {
        var button = new RoundedButton { Text = text };
        StyleButton(button, primary, text.Length > 5 ? 116 : 94, 34);
        button.Margin = new Padding(0, 0, 8, 0);
        button.Click += click;
        return button;
    }

    private static void ConfigureEditor(RichTextBox editor)
    {
        editor.Dock = DockStyle.Fill;
        editor.BorderStyle = BorderStyle.None;
        editor.Font = new Font("Consolas", 10.5F);
        editor.AcceptsTab = true;
        editor.WordWrap = false;
        editor.DetectUrls = false;
        editor.BackColor = Color.White;
        editor.ForeColor = Color.FromArgb(30, 41, 59);
    }

    private static void StyleButton(Button button, bool primary, int width, int height)
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
}
