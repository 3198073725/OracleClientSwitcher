namespace OracleClientSwitcher;

internal sealed class EnvironmentChangePreviewForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly EnvironmentChangePreview preview;

    public EnvironmentChangePreviewForm(EnvironmentChangePreview preview)
    {
        this.preview = preview;
        Text = "确认 Oracle 环境变更";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1020, 650);
        MinimumSize = new Size(900, 600);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, preview.Warnings.Count > 0 ? 106 : 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateSummaryCard(), 0, 1);
        root.Controls.Add(CreateChangesGrid(), 0, 2);
        root.Controls.Add(CreateNotice(), 0, 3);
        root.Controls.Add(CreateActions(), 0, 4);
    }

    private Control CreateHeader()
    {
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
            Text = "确认环境变更",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(28, 11)
        });
        header.Controls.Add(new Label
        {
            Text = "检查本次切换内容，确认后才会写入 Windows 环境变量",
            ForeColor = Color.FromArgb(211, 225, 243),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            AutoSize = true,
            Location = new Point(31, 57)
        });
        return header;
    }

    private Control CreateSummaryCard()
    {
        var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Page, Padding = new Padding(22, 14, 22, 6) };
        var card = new BorderPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Border, Padding = new Padding(18, 9, 18, 8) };
        string scope = preview.Target == EnvironmentVariableTarget.Machine ? "整个系统（需要管理员权限）" : "当前用户";
        var client = new Label
        {
            Text = $"Oracle {preview.Client.Version}  ·  {preview.Client.Architecture}",
            ForeColor = Navy,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 10)
        };
        var target = new Label
        {
            Text = $"生效范围：{scope}",
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true,
            Location = new Point(18, 37)
        };
        var count = new Label
        {
            Text = $"{preview.Changes.Count(x => !string.Equals(x.CurrentValue, x.NewValue, StringComparison.Ordinal))} 项配置将发生变化",
            ForeColor = Color.FromArgb(29, 78, 216),
            BackColor = Color.FromArgb(239, 246, 255),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 6),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(780, 15)
        };
        void AlignCount() => count.Left = Math.Max(420, card.ClientSize.Width - count.Width - 18);
        card.Resize += (_, _) => AlignCount();
        card.Controls.Add(client);
        card.Controls.Add(target);
        card.Controls.Add(count);
        AlignCount();
        host.Controls.Add(card);
        return host;
    }

    private Control CreateChangesGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(22, 8, 22, 8),
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            EnableHeadersVisualStyles = false,
            ShowCellToolTips = false,
            ColumnHeadersHeight = 40,
            RowTemplate = { Height = 56 },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(30, 41, 59),
                SelectionBackColor = Color.FromArgb(239, 246, 255),
                SelectionForeColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(8, 4, 8, 4),
                WrapMode = DataGridViewTriState.True
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(236, 242, 249),
                ForeColor = Navy,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Padding = new Padding(8, 5, 8, 5)
            }
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "配置项", Width = 145 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "当前", Width = 205 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "修改后", Width = 205 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "变更说明", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            HeaderText = "明细",
            Text = "查看列表",
            UseColumnTextForButtonValue = true,
            Width = 112,
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Blue,
                SelectionBackColor = Color.FromArgb(239, 246, 255),
                SelectionForeColor = Blue,
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                Padding = new Padding(7)
            }
        });

        foreach (EnvironmentChangeItem item in preview.Changes)
        {
            int row = grid.Rows.Add(item.Item, Summarize(item.Item, item.CurrentValue), Summarize(item.Item, item.NewValue), item.Action);
            grid.Rows[row].Tag = item;
            if (string.Equals(item.CurrentValue, item.NewValue, StringComparison.Ordinal))
                grid.Rows[row].DefaultCellStyle.ForeColor = Color.FromArgb(100, 116, 139);
        }
        grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 4) return;
            if (grid.Rows[e.RowIndex].Tag is not EnvironmentChangeItem item) return;
            using var dialog = new EnvironmentChangeDetailsForm(item);
            dialog.ShowDialog(this);
        };
        grid.ClearSelection();
        return grid;
    }

    private Control CreateNotice()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(28, 4, 28, 4),
            Padding = new Padding(13, 8, 13, 8),
            BackColor = preview.Warnings.Count > 0 ? Color.FromArgb(255, 247, 237) : Color.FromArgb(236, 253, 245),
            ForeColor = preview.Warnings.Count > 0 ? Color.FromArgb(154, 52, 18) : Color.FromArgb(4, 120, 87),
            Text = preview.Warnings.Count > 0
                ? "注意：\r\n• " + string.Join("\r\n• ", preview.Warnings)
                : "✓ 未发现额外风险。切换前会自动备份，写入失败时会自动回滚。",
            AutoEllipsis = true
        };
    }

    private Control CreateActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(20, 14, 22, 12),
            BackColor = Color.White
        };
        var confirm = new RoundedButton { Text = preview.HasChanges ? "确认并切换" : "无需修改", DialogResult = DialogResult.OK, Enabled = preview.HasChanges };
        StyleButton(confirm, true, 142);
        var cancel = new RoundedButton { Text = "取消", DialogResult = DialogResult.Cancel };
        StyleButton(cancel, false, 92);
        actions.Controls.Add(confirm);
        actions.Controls.Add(cancel);
        AcceptButton = confirm;
        CancelButton = cancel;
        return actions;
    }

    private static string Summarize(string item, string value)
    {
        if (value == "（未设置）") return value;
        if (item == "PATH")
        {
            int count = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            return $"{count} 个目录项";
        }
        if (value.Length <= 34) return value;
        string leaf = Path.GetFileName(value.TrimEnd('\\', '/'));
        return string.IsNullOrWhiteSpace(leaf) ? "…" + value[^30..] : "…\\" + leaf;
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
