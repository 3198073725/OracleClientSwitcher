namespace OracleClientSwitcher;

internal sealed class TnsSyncForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly CheckedListBox targets = new();
    private readonly CheckBox tnsNamesCheck = new() { Text = "tnsnames.ora", Checked = true, AutoSize = true };
    private readonly CheckBox sqlNetCheck = new() { Text = "sqlnet.ora", Checked = true, AutoSize = true };

    public IReadOnlyList<OracleClientInfo> SelectedClients { get; private set; } = Array.Empty<OracleClientInfo>();
    public bool SyncTnsNames => tnsNamesCheck.Checked;
    public bool SyncSqlNet => sqlNetCheck.Checked;

    public TnsSyncForm(OracleClientInfo source, IEnumerable<OracleClientInfo> clients)
    {
        Text = "同步 TNS 配置";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 590);
        MinimumSize = new Size(680, 520);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi(source, clients);
    }

    private void BuildUi(OracleClientInfo source, IEnumerable<OracleClientInfo> clients)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Page };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        var header = new GradientPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105) };
        header.Controls.Add(new Label { Text = "同步 TNS 配置", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 12) });
        header.Controls.Add(new Label { Text = "选择目标客户端；覆盖前会自动备份原文件", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(31, 55) });
        root.Controls.Add(header, 0, 0);

        var options = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(22, 14, 22, 6), BackColor = Color.White, BorderColor = Border };
        options.Controls.Add(new Label { Text = $"源：Oracle {source.Version} · {source.Architecture}", ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), AutoSize = true, Location = new Point(16, 9) });
        tnsNamesCheck.Location = new Point(18, 39);
        sqlNetCheck.Location = new Point(160, 39);
        options.Controls.Add(tnsNamesCheck);
        options.Controls.Add(sqlNetCheck);
        root.Controls.Add(options, 0, 1);

        targets.Dock = DockStyle.Fill;
        targets.Margin = new Padding(22, 8, 22, 10);
        targets.CheckOnClick = true;
        targets.HorizontalScrollbar = true;
        targets.BackColor = Color.White;
        targets.BorderStyle = BorderStyle.FixedSingle;
        targets.Font = new Font("Microsoft YaHei UI", 9.5F);
        foreach (OracleClientInfo client in clients
                     .Where(x => !string.Equals(OracleScanner.Normalize(x.PathDirectory), OracleScanner.Normalize(source.PathDirectory), StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(x => x.Version).ThenBy(x => x.Architecture))
            targets.Items.Add(new ClientItem(client));
        root.Controls.Add(targets, 0, 2);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(20, 14, 22, 10), BackColor = Color.White };
        var sync = new RoundedButton { Text = "开始同步" };
        StyleButton(sync, true, 112);
        sync.Click += (_, _) => Confirm();
        var cancel = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel };
        StyleButton(cancel, false, 88);
        var all = new RoundedButton { Text = "全选" };
        StyleButton(all, false, 80);
        all.Click += (_, _) =>
        {
            for (int i = 0; i < targets.Items.Count; i++) targets.SetItemChecked(i, true);
        };
        footer.Controls.Add(sync);
        footer.Controls.Add(cancel);
        footer.Controls.Add(all);
        root.Controls.Add(footer, 0, 3);
        AcceptButton = sync;
        CancelButton = cancel;
    }

    private void Confirm()
    {
        if (!tnsNamesCheck.Checked && !sqlNetCheck.Checked)
        {
            MessageBox.Show(this, "请至少选择一个配置文件。", "同步 TNS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SelectedClients = targets.CheckedItems.Cast<ClientItem>().Select(x => x.Client).ToList();
        if (SelectedClients.Count == 0)
        {
            MessageBox.Show(this, "请选择至少一个目标客户端。", "同步 TNS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
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

    private sealed class ClientItem
    {
        public OracleClientInfo Client { get; }
        public ClientItem(OracleClientInfo client) => Client = client;
        public override string ToString() => $"Oracle {Client.Version}  ·  {Client.Architecture}  ·  {Client.ClientType}    {Client.TnsAdmin}";
    }
}
