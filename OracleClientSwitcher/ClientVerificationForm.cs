using System.Diagnostics;
using System.Text;

namespace OracleClientSwitcher;

internal sealed class ClientVerificationForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Green = Color.FromArgb(4, 120, 87);
    private static readonly Color Orange = Color.FromArgb(180, 83, 9);
    private static readonly Color Red = Color.FromArgb(185, 28, 28);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly OracleClientInfo client;
    private readonly bool autoRun;
    private readonly DataGridView grid = new();
    private readonly TextBox detailBox = new();
    private readonly ComboBox aliasCombo = new();
    private readonly Button runButton = new RoundedButton();
    private readonly Button targetButton = new RoundedButton();
    private readonly Button testAliasButton = new RoundedButton();
    private readonly Label progressLabel = new();
    private readonly Label summaryLabel = new();
    private readonly List<ClientVerificationItem> results = new();
    private CancellationTokenSource? cancellation;

    public ClientVerificationForm(OracleClientInfo client, bool autoRun = true)
    {
        this.client = client;
        this.autoRun = autoRun;
        Text = "Oracle 客户端验证中心";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1120, 780);
        MinimumSize = new Size(980, 680);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadAliases();
        Shown += async (_, _) =>
        {
            if (this.autoRun) await RunVerificationAsync();
        };
        FormClosing += (_, _) => cancellation?.Cancel();
    }

    internal void SetPreviewResults(IEnumerable<ClientVerificationItem> items)
    {
        results.Clear();
        results.AddRange(items);
        RefreshResults();
        progressLabel.Text = "验证完成";
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        Controls.Add(root);

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateClientBar(), 0, 1);
        ConfigureGrid();
        root.Controls.Add(grid, 0, 2);

        detailBox.Dock = DockStyle.Fill;
        detailBox.Margin = new Padding(22, 6, 22, 8);
        detailBox.Multiline = true;
        detailBox.ReadOnly = true;
        detailBox.ScrollBars = ScrollBars.Vertical;
        detailBox.BackColor = Color.White;
        detailBox.ForeColor = Color.FromArgb(51, 65, 85);
        detailBox.BorderStyle = BorderStyle.FixedSingle;
        detailBox.Font = new Font("Microsoft YaHei UI", 9F);
        detailBox.Text = "选择一项验证结果查看详细信息。";
        root.Controls.Add(detailBox, 0, 3);

        root.Controls.Add(CreateTnsPanel(), 0, 4);
        root.Controls.Add(CreateFooter(), 0, 5);
    }

    private Control CreateHeader()
    {
        var header = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            StartColor = Navy,
            EndColor = Color.FromArgb(24, 61, 105)
        };
        header.Controls.Add(new Label
        {
            Text = "客户端验证中心",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 12)
        });
        header.Controls.Add(new Label
        {
            Text = "隔离验证 OCI、依赖组件、SQL*Plus、TNS 服务和程序位数",
            ForeColor = Color.FromArgb(211, 225, 243),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(33, 57)
        });
        progressLabel.AutoSize = true;
        progressLabel.ForeColor = Color.White;
        progressLabel.BackColor = Color.FromArgb(43, 78, 124);
        progressLabel.Padding = new Padding(12, 7, 12, 7);
        progressLabel.Text = "等待验证";
        progressLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        progressLabel.Location = new Point(940, 27);
        void Align() => progressLabel.Left = Math.Max(700, header.ClientSize.Width - progressLabel.Width - 28);
        header.Resize += (_, _) => Align();
        progressLabel.SizeChanged += (_, _) => Align();
        header.Controls.Add(progressLabel);
        Align();
        return header;
    }

    private Control CreateClientBar()
    {
        var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 14, 22, 6), BackColor = Page };
        var card = new BorderPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Border };
        card.Controls.Add(new Label
        {
            Text = $"Oracle {client.Version}  ·  {client.Architecture}  ·  {client.ClientType}",
            ForeColor = Navy,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 9)
        });
        card.Controls.Add(new Label
        {
            Text = client.PathDirectory,
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoEllipsis = true,
            Location = new Point(19, 38),
            Size = new Size(620, 24),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        });
        runButton.Text = "重新验证";
        StyleButton(runButton, true, 108, 36);
        runButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        runButton.Location = new Point(910, 15);
        runButton.Click += async (_, _) => await RunVerificationAsync();
        targetButton.Text = "选择目标程序";
        StyleButton(targetButton, false, 126, 36);
        targetButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        targetButton.Location = new Point(770, 15);
        targetButton.Click += (_, _) => SelectTargetExecutable();
        void Align()
        {
            runButton.Left = card.ClientSize.Width - runButton.Width - 16;
            targetButton.Left = runButton.Left - targetButton.Width - 10;
        }
        card.Resize += (_, _) => Align();
        card.Controls.Add(runButton);
        card.Controls.Add(targetButton);
        Align();
        host.Controls.Add(card);
        return host;
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.Margin = new Padding(22, 8, 22, 4);
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
        grid.ShowCellToolTips = false;
        grid.ColumnHeadersHeight = 40;
        grid.RowTemplate.Height = 42;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(30, 41, 59),
            SelectionBackColor = Color.FromArgb(219, 234, 254),
            SelectionForeColor = Navy,
            Padding = new Padding(7)
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(236, 242, 249),
            ForeColor = Navy,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Padding = new Padding(7)
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类别", Width = 94 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "检查项", Width = 170 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "结果", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.SelectionChanged += (_, _) => ShowSelectedDetail();
    }

    private Control CreateTnsPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(22, 8, 22, 8), BackColor = Page };
        var card = new BorderPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Border };
        card.Controls.Add(new Label
        {
            Text = "TNS 服务测试",
            ForeColor = Navy,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(17, 12)
        });
        card.Controls.Add(new Label
        {
            Text = "选择 tnsnames.ora 中的服务别名，通过 TNSPING 测试解析和网络可达性（不需要账号密码）",
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true,
            Location = new Point(18, 40)
        });
        aliasCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        aliasCombo.Size = new Size(230, 32);
        aliasCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        aliasCombo.Location = new Point(720, 20);
        testAliasButton.Text = "测试服务";
        StyleButton(testAliasButton, false, 104, 34);
        testAliasButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        testAliasButton.Location = new Point(960, 18);
        testAliasButton.Click += async (_, _) => await TestSelectedAliasAsync();
        void Align()
        {
            testAliasButton.Left = card.ClientSize.Width - testAliasButton.Width - 16;
            aliasCombo.Left = testAliasButton.Left - aliasCombo.Width - 10;
        }
        card.Resize += (_, _) => Align();
        card.Controls.Add(aliasCombo);
        card.Controls.Add(testAliasButton);
        Align();
        host.Controls.Add(card);
        return host;
    }

    private Control CreateFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(20, 14, 22, 10),
            BackColor = Color.White
        };
        var close = new RoundedButton { Text = "关闭", DialogResult = DialogResult.OK };
        StyleButton(close, true, 94, 38);
        var copy = new RoundedButton { Text = "复制验证报告" };
        StyleButton(copy, false, 128, 38);
        copy.Click += (_, _) => Clipboard.SetText(BuildReport());
        summaryLabel.AutoSize = true;
        summaryLabel.ForeColor = Color.FromArgb(71, 85, 105);
        summaryLabel.Padding = new Padding(0, 9, 18, 0);
        footer.Controls.Add(close);
        footer.Controls.Add(copy);
        footer.Controls.Add(summaryLabel);
        AcceptButton = close;
        CancelButton = close;
        return footer;
    }

    private async Task RunVerificationAsync()
    {
        cancellation?.Cancel();
        cancellation = new CancellationTokenSource();
        SetBusy(true);
        results.Clear();
        RefreshResults();
        try
        {
            var progress = new Progress<string>(message => progressLabel.Text = message);
            results.AddRange(await ClientVerificationService.RunAllAsync(client, progress, cancellation.Token));
            progressLabel.Text = "验证完成";
            RefreshResults();
        }
        catch (OperationCanceledException) { progressLabel.Text = "验证已取消"; }
        catch (Exception ex)
        {
            results.Add(new ClientVerificationItem
            {
                Category = "验证",
                Name = "执行过程",
                Severity = VerificationSeverity.Error,
                Summary = "验证过程发生异常",
                Detail = ex.Message
            });
            RefreshResults();
            progressLabel.Text = "验证失败";
        }
        finally { SetBusy(false); }
    }

    private async Task TestSelectedAliasAsync()
    {
        if (aliasCombo.SelectedItem is not string alias) return;
        cancellation?.Cancel();
        cancellation = new CancellationTokenSource();
        SetBusy(true);
        progressLabel.Text = $"正在测试 {alias}…";
        try
        {
            ClientVerificationItem result = await ClientVerificationService.TestTnsAliasAsync(client, alias, cancellation.Token);
            results.RemoveAll(x => x.Category == "TNS" && x.Name == result.Name);
            results.Add(result);
            RefreshResults(result);
            progressLabel.Text = result.Severity == VerificationSeverity.Success ? "TNS 测试通过" : "TNS 测试失败";
        }
        catch (OperationCanceledException) { progressLabel.Text = "TNS 测试已取消"; }
        finally { SetBusy(false); }
    }

    private void SelectTargetExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择需要使用 Oracle 客户端的程序",
            Filter = "Windows 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ClientVerificationItem result = ClientVerificationService.CheckTargetExecutable(client, dialog.FileName);
        results.RemoveAll(x => x.Category == "兼容性" && x.Name == result.Name);
        results.Add(result);
        RefreshResults(result);
    }

    private void LoadAliases()
    {
        aliasCombo.Items.Clear();
        foreach (string alias in ClientVerificationService.ReadTnsAliases(client)) aliasCombo.Items.Add(alias);
        if (aliasCombo.Items.Count > 0) aliasCombo.SelectedIndex = 0;
        testAliasButton.Enabled = aliasCombo.Items.Count > 0;
    }

    private void RefreshResults(ClientVerificationItem? select = null)
    {
        grid.Rows.Clear();
        foreach (ClientVerificationItem item in results)
        {
            string status = item.Severity switch
            {
                VerificationSeverity.Success => "● 通过",
                VerificationSeverity.Info => "● 信息",
                VerificationSeverity.Warning => "▲ 警告",
                _ => "✕ 失败"
            };
            int index = grid.Rows.Add(status, item.Category, item.Name, item.Summary);
            grid.Rows[index].Tag = item;
            Color color = item.Severity switch
            {
                VerificationSeverity.Success => Green,
                VerificationSeverity.Info => Color.FromArgb(37, 99, 235),
                VerificationSeverity.Warning => Orange,
                _ => Red
            };
            grid.Rows[index].Cells[0].Style.ForeColor = color;
            grid.Rows[index].Cells[0].Style.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            if (ReferenceEquals(item, select)) grid.Rows[index].Selected = true;
        }
        if (select is null) grid.ClearSelection();
        int passed = results.Count(x => x.Severity == VerificationSeverity.Success);
        int warnings = results.Count(x => x.Severity == VerificationSeverity.Warning);
        int errors = results.Count(x => x.Severity == VerificationSeverity.Error);
        summaryLabel.Text = results.Count == 0 ? string.Empty : $"通过 {passed}   警告 {warnings}   失败 {errors}";
        if (select is null) detailBox.Text = results.Count == 0 ? "正在准备验证…" : "选择一项验证结果查看详细信息。";
        else ShowSelectedDetail();
    }

    private void ShowSelectedDetail()
    {
        if (grid.SelectedRows.Count != 1 || grid.SelectedRows[0].Tag is not ClientVerificationItem item) return;
        detailBox.Text = $"{item.Category} / {item.Name}\r\n{item.Summary}\r\n\r\n{item.Detail}";
    }

    private string BuildReport()
    {
        var text = new StringBuilder();
        text.AppendLine("Oracle 客户端验证报告");
        text.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"客户端：Oracle {client.Version} {client.Architecture}");
        text.AppendLine($"目录：{client.PathDirectory}");
        text.AppendLine(new string('=', 72));
        foreach (ClientVerificationItem item in results)
        {
            text.AppendLine($"[{item.Severity}] {item.Category} / {item.Name}：{item.Summary}");
            if (!string.IsNullOrWhiteSpace(item.Detail)) text.AppendLine(item.Detail);
            text.AppendLine();
        }
        return text.ToString();
    }

    private void SetBusy(bool busy)
    {
        runButton.Enabled = targetButton.Enabled = !busy;
        testAliasButton.Enabled = !busy && aliasCombo.Items.Count > 0;
        UseWaitCursor = busy;
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
