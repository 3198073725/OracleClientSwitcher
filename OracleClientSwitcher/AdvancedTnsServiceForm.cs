namespace OracleClientSwitcher;

internal sealed class AdvancedTnsServiceForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private readonly TextBox aliasText = new();
    private readonly DataGridView endpointsGrid = new();
    private readonly ComboBox connectType = new();
    private readonly TextBox connectName = new();
    private readonly CheckBox failoverCheck = new();
    private readonly CheckBox loadBalanceCheck = new();
    private readonly NumericUpDown timeoutNumber = new();
    private readonly NumericUpDown retryNumber = new();
    private readonly TextBox walletText = new();
    private readonly Label statusLabel = new();
    private readonly TnsAdvancedDefinition definition;

    public string ResultText { get; private set; } = string.Empty;
    public string ResultAlias => aliasText.Text.Trim();

    public AdvancedTnsServiceForm(TnsAdvancedDefinition definition)
    {
        this.definition = definition;
        Text = "高级 TNS 可视化配置";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 800);
        MinimumSize = new Size(860, 720);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadDefinition();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Page };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        Controls.Add(root);
        var header = new GradientPanel { Dock = DockStyle.Fill, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105), Margin = Padding.Empty };
        header.Controls.Add(new Label { Text = "高级 TNS 可视化配置", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 10) });
        header.Controls.Add(new Label { Text = "RAC 多地址、故障转移、负载均衡、超时重试与 TCPS Wallet", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(31, 54) });
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = new Padding(22, 14, 22, 8), Padding = new Padding(18), BackColor = Color.White, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var aliasRow = FlowRow();
        aliasRow.Controls.Add(FieldLabel("服务名"));
        aliasText.Size = new Size(280, 30);
        aliasText.Margin = new Padding(8, 5, 24, 4);
        aliasRow.Controls.Add(aliasText);
        aliasRow.Controls.Add(new Label { Text = "每个 RAC 节点单独添加一行地址。", AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Margin = new Padding(0, 10, 0, 0) });
        body.Controls.Add(aliasRow, 0, 0);
        body.Controls.Add(new Label { Text = "连接地址", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Navy, Font = new Font(Font, FontStyle.Bold) }, 0, 1);
        ConfigureEndpointsGrid();
        body.Controls.Add(endpointsGrid, 0, 2);
        var addressActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 3) };
        addressActions.Controls.Add(ButtonFor("添加地址", (_, _) => endpointsGrid.Rows.Add("TCP", "", 1521), false, 104));
        addressActions.Controls.Add(ButtonFor("删除地址", (_, _) => { foreach (DataGridViewRow row in endpointsGrid.SelectedRows) if (!row.IsNewRow) endpointsGrid.Rows.Remove(row); }, false, 104));
        addressActions.Controls.Add(ButtonFor("测试全部端口", async (_, _) => await TestPortsAsync(), false, 132));
        body.Controls.Add(addressActions, 0, 3);

        var connectRow = FlowRow();
        connectRow.Controls.Add(FieldLabel("连接标识"));
        connectType.DropDownStyle = ComboBoxStyle.DropDownList;
        connectType.Items.AddRange(new object[] { "SERVICE_NAME", "SID" });
        connectType.Size = new Size(190, 30);
        connectType.Margin = new Padding(8, 5, 12, 4);
        connectRow.Controls.Add(connectType);
        connectName.Size = new Size(270, 30);
        connectName.Margin = new Padding(0, 5, 0, 4);
        connectRow.Controls.Add(connectName);
        body.Controls.Add(connectRow, 0, 4);

        var policyRow = FlowRow();
        failoverCheck.Text = "启用 FAILOVER";
        failoverCheck.AutoSize = true;
        failoverCheck.Margin = new Padding(0, 9, 18, 0);
        loadBalanceCheck.Text = "启用 LOAD_BALANCE";
        loadBalanceCheck.AutoSize = true;
        loadBalanceCheck.Margin = new Padding(0, 9, 22, 0);
        policyRow.Controls.Add(failoverCheck);
        policyRow.Controls.Add(loadBalanceCheck);
        policyRow.Controls.Add(FieldLabel("连接超时(秒)"));
        timeoutNumber.Minimum = 0;
        timeoutNumber.Maximum = 300;
        timeoutNumber.Size = new Size(80, 30);
        timeoutNumber.Margin = new Padding(6, 5, 20, 4);
        policyRow.Controls.Add(timeoutNumber);
        policyRow.Controls.Add(FieldLabel("重试"));
        retryNumber.Minimum = 0;
        retryNumber.Maximum = 20;
        retryNumber.Size = new Size(70, 30);
        retryNumber.Margin = new Padding(6, 5, 0, 4);
        policyRow.Controls.Add(retryNumber);
        body.Controls.Add(policyRow, 0, 5);

        var walletRow = FlowRow();
        walletRow.Controls.Add(FieldLabel("TCPS Wallet"));
        walletText.Size = new Size(500, 30);
        walletText.Margin = new Padding(8, 5, 10, 4);
        walletRow.Controls.Add(walletText);
        var browse = ButtonFor("选择目录", (_, _) => BrowseWallet(), false, 96);
        browse.Margin = new Padding(0, 3, 0, 3);
        walletRow.Controls.Add(browse);
        body.Controls.Add(walletRow, 0, 6);
        root.Controls.Add(body, 0, 1);

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Margin = new Padding(24, 4, 24, 4);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.ForeColor = Color.FromArgb(71, 85, 105);
        statusLabel.AutoEllipsis = true;
        root.Controls.Add(statusLabel, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var cancel = ButtonFor("取消", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }, false, 88);
        var apply = ButtonFor("应用到配置", (_, _) => Apply(), true, 132);
        void Align()
        {
            apply.Location = new Point(footer.ClientSize.Width - apply.Width - 22, 15);
            cancel.Location = new Point(apply.Left - cancel.Width - 10, 15);
        }
        footer.Resize += (_, _) => Align();
        footer.Controls.Add(cancel);
        footer.Controls.Add(apply);
        Align();
        root.Controls.Add(footer, 0, 3);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    private void ConfigureEndpointsGrid()
    {
        endpointsGrid.Dock = DockStyle.Fill;
        endpointsGrid.BackgroundColor = Color.White;
        endpointsGrid.BorderStyle = BorderStyle.FixedSingle;
        endpointsGrid.AllowUserToAddRows = false;
        endpointsGrid.AllowUserToDeleteRows = true;
        endpointsGrid.RowHeadersVisible = false;
        endpointsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        endpointsGrid.MultiSelect = true;
        endpointsGrid.AutoGenerateColumns = false;
        endpointsGrid.RowTemplate.Height = 34;
        endpointsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Protocol", HeaderText = "协议", Width = 120, DataSource = new[] { "TCP", "TCPS" } });
        endpointsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Host", HeaderText = "主机或 IP", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        endpointsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Port", HeaderText = "端口", Width = 120 });
    }

    private void LoadDefinition()
    {
        aliasText.Text = definition.Alias;
        foreach (TnsEndpoint endpoint in definition.Endpoints) endpointsGrid.Rows.Add(endpoint.Protocol, endpoint.Host, endpoint.Port);
        if (endpointsGrid.Rows.Count == 0) endpointsGrid.Rows.Add("TCP", "", 1521);
        connectType.SelectedItem = definition.UsesSid ? "SID" : "SERVICE_NAME";
        connectName.Text = definition.ConnectName;
        failoverCheck.Checked = definition.Failover;
        loadBalanceCheck.Checked = definition.LoadBalance;
        timeoutNumber.Value = Math.Clamp(definition.ConnectTimeoutSeconds, 0, 300);
        retryNumber.Value = Math.Clamp(definition.RetryCount, 0, 20);
        walletText.Text = definition.WalletDirectory;
        statusLabel.Text = "端口测试只检查网络可达性；TCPS 证书和服务名请再使用 tnsping 验证。";
    }

    private TnsAdvancedDefinition ReadDefinition()
    {
        var endpoints = new List<TnsEndpoint>();
        foreach (DataGridViewRow row in endpointsGrid.Rows)
        {
            string protocol = Convert.ToString(row.Cells[0].Value)?.Trim().ToUpperInvariant() ?? "TCP";
            string host = Convert.ToString(row.Cells[1].Value)?.Trim() ?? string.Empty;
            if (!int.TryParse(Convert.ToString(row.Cells[2].Value), out int port)) port = 0;
            if (host.Length > 0 || port > 0) endpoints.Add(new TnsEndpoint { Protocol = protocol, Host = host, Port = port });
        }
        return new TnsAdvancedDefinition
        {
            Alias = aliasText.Text.Trim(),
            Endpoints = endpoints,
            ConnectName = connectName.Text.Trim(),
            UsesSid = connectType.Text == "SID",
            Failover = failoverCheck.Checked,
            LoadBalance = loadBalanceCheck.Checked,
            ConnectTimeoutSeconds = (int)timeoutNumber.Value,
            RetryCount = (int)retryNumber.Value,
            WalletDirectory = walletText.Text.Trim()
        };
    }

    private async Task TestPortsAsync()
    {
        TnsAdvancedDefinition value = ReadDefinition();
        if (value.Endpoints.Count == 0) { statusLabel.Text = "请先添加至少一个地址。"; return; }
        statusLabel.Text = "正在测试主机端口…";
        IReadOnlyList<TnsOperationResult> results = await TnsConnectivityService.TestEndpointsAsync(value.Endpoints);
        string report = string.Join(Environment.NewLine, results.Select(x => (x.Success ? "✓ " : "✕ ") + x.Message));
        AppLog.Information("TNS", "测试主机端口", $"成功 {results.Count(x => x.Success)}/{results.Count}", report);
        MessageBox.Show(this, report, "端口测试结果", MessageBoxButtons.OK, results.All(x => x.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        statusLabel.Text = $"端口测试完成：{results.Count(x => x.Success)}/{results.Count} 可达。";
    }

    private void BrowseWallet()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择 Oracle Wallet 目录", UseDescriptionForTitle = true, ShowNewFolderButton = false, InitialDirectory = Directory.Exists(walletText.Text) ? walletText.Text : string.Empty };
        if (dialog.ShowDialog(this) == DialogResult.OK) walletText.Text = dialog.SelectedPath;
    }

    private void Apply()
    {
        try
        {
            TnsAdvancedDefinition value = ReadDefinition();
            if (!System.Text.RegularExpressions.Regex.IsMatch(value.Alias, @"^[A-Za-z0-9_.-]+$")) throw new InvalidOperationException("服务名只能包含字母、数字、下划线、点和短横线。");
            ResultText = TnsConfigurationManager.BuildAdvancedService(value);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "配置不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static FlowLayoutPanel FlowRow() => new() { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.FromArgb(71, 85, 105), Margin = new Padding(0, 10, 0, 0) };

    private static Button ButtonFor(string text, EventHandler click, bool primary, int width)
    {
        var button = new RoundedButton { Text = text, Size = new Size(width, 34), Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Navy, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
        button.Click += click;
        return button;
    }
}
