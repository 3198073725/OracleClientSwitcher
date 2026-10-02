using System.Text;

namespace OracleClientSwitcher;

internal sealed class EnvironmentChangeDetailsForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly EnvironmentChangeItem item;
    private readonly DataGridView grid = new();

    public EnvironmentChangeDetailsForm(EnvironmentChangeItem item)
    {
        this.item = item;
        Text = $"{item.Item} 变更明细";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 590);
        MinimumSize = new Size(780, 500);
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
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);

        var header = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            StartColor = Navy,
            EndColor = Color.FromArgb(24, 61, 105)
        };
        header.Controls.Add(new Label
        {
            Text = $"{item.Item} 变更明细",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 12)
        });
        header.Controls.Add(new Label
        {
            Text = item.Item == "PATH" ? "逐项查看目录的新增、移除和顺序变化" : "查看变量修改前后的完整值",
            ForeColor = Color.FromArgb(211, 225, 243),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(33, 55)
        });
        root.Controls.Add(header, 0, 0);

        ConfigureGrid();
        root.Controls.Add(grid, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(20, 14, 22, 12),
            BackColor = Color.White
        };
        var close = new RoundedButton { Text = "关闭", DialogResult = DialogResult.OK };
        StyleButton(close, true, 94);
        var copy = new RoundedButton { Text = "复制明细" };
        StyleButton(copy, false, 108);
        copy.Click += (_, _) => Clipboard.SetText(BuildTextReport());
        actions.Controls.Add(close);
        actions.Controls.Add(copy);
        root.Controls.Add(actions, 0, 2);
        AcceptButton = close;
        CancelButton = close;
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.Margin = new Padding(22, 18, 22, 12);
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
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
        grid.ColumnHeadersHeight = 40;
        grid.RowTemplate.Height = 42;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(30, 41, 59),
            SelectionBackColor = Color.FromArgb(239, 246, 255),
            SelectionForeColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(7),
            WrapMode = DataGridViewTriState.True
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(236, 242, 249),
            ForeColor = Navy,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Padding = new Padding(7)
        };

        if (item.Item == "PATH") LoadPathRows();
        else LoadValueRows();
        grid.ClearSelection();
    }

    private void LoadPathRows()
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "变化", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "当前顺序", Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "修改后顺序", Width = 132 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "目录", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 500 });

        string[] current = SplitPath(item.CurrentValue);
        string[] changed = SplitPath(item.NewValue);
        var all = changed.Concat(current).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string path in all)
        {
            int oldIndex = Array.FindIndex(current, x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            int newIndex = Array.FindIndex(changed, x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            string state = oldIndex < 0 ? "新增" : newIndex < 0 ? "移除" : oldIndex == newIndex ? "保留" : "顺序调整";
            int row = grid.Rows.Add(state, oldIndex < 0 ? "—" : (oldIndex + 1).ToString(), newIndex < 0 ? "—" : (newIndex + 1).ToString(), path);
            Color color = state switch
            {
                "新增" => Color.FromArgb(4, 120, 87),
                "移除" => Color.FromArgb(185, 28, 28),
                "顺序调整" => Color.FromArgb(180, 83, 9),
                _ => Color.FromArgb(71, 85, 105)
            };
            grid.Rows[row].Cells[0].Style.ForeColor = color;
            grid.Rows[row].Cells[0].Style.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        }
    }

    private void LoadValueRows()
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "阶段", Width = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "完整值", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Rows.Add("当前值", item.CurrentValue);
        grid.Rows.Add("修改后", item.NewValue);
        grid.Rows[0].Cells[0].Style.ForeColor = Color.FromArgb(71, 85, 105);
        grid.Rows[1].Cells[0].Style.ForeColor = Blue;
        grid.Rows[1].Cells[0].Style.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
    }

    private string BuildTextReport()
    {
        var text = new StringBuilder();
        text.AppendLine($"{item.Item} 变更明细");
        text.AppendLine(new string('-', 60));
        foreach (DataGridViewRow row in grid.Rows)
        {
            string[] values = row.Cells.Cast<DataGridViewCell>().Select(x => x.Value?.ToString() ?? string.Empty).ToArray();
            text.AppendLine(string.Join("\t", values));
        }
        return text.ToString();
    }

    private static string[] SplitPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "（未设置）") return Array.Empty<string>();
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
