using System.Text;

namespace OracleClientSwitcher;

internal sealed class EnvironmentDiagnosticsForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private static readonly Color Border = Color.FromArgb(218, 225, 235);
    private readonly OracleEnvironmentStatus status;

    public EnvironmentDiagnosticsForm(OracleEnvironmentStatus status)
    {
        this.status = status;
        Text = "Oracle 环境诊断";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1020, 620);
        MinimumSize = new Size(820, 520);
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
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Page
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        Controls.Add(root);

        var header = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            StartColor = Navy,
            EndColor = Color.FromArgb(24, 61, 105)
        };
        var title = new Label
        {
            Text = "Oracle 环境诊断",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(440, 40),
            Location = new Point(26, 10),
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true
        };
        var subtitle = new Label
        {
            Text = "逐项说明当前配置、发现的问题，以及建议修改方式",
            ForeColor = Color.FromArgb(205, 219, 238),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(29, 53)
        };
        header.Controls.AddRange(new Control[] { title, subtitle });
        root.Controls.Add(header, 0, 0);

        var summaryCard = new BorderPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(24, 12, 24, 10),
            Padding = new Padding(16, 9, 16, 8),
            BackColor = status.HasWarnings ? Color.FromArgb(255, 247, 237) : Color.FromArgb(236, 253, 245),
            BorderColor = status.HasWarnings ? Color.FromArgb(253, 186, 116) : Color.FromArgb(110, 231, 183)
        };
        var summaryTitle = new Label
        {
            Text = status.HasWarnings ? $"发现 {status.Issues.Count(x => x.Severity != EnvironmentIssueSeverity.Info)} 项需要处理" : "当前环境检查正常",
            Dock = DockStyle.Top,
            Height = 25,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = status.HasWarnings ? Color.FromArgb(154, 52, 18) : Color.FromArgb(4, 120, 87)
        };
        var summaryDetail = new Label
        {
            Text = status.Detail.Replace(Environment.NewLine, "  ·  "),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        summaryCard.Controls.Add(summaryDetail);
        summaryCard.Controls.Add(summaryTitle);
        root.Controls.Add(summaryCard, 0, 1);

        DataGridView grid = CreateIssuesGrid();
        var gridCard = new BorderPanel { Dock = DockStyle.Fill, Margin = new Padding(24, 0, 24, 0), Padding = new Padding(1), BackColor = Color.White, BorderColor = Border };
        gridCard.Controls.Add(grid);
        root.Controls.Add(gridCard, 0, 2);

        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(24, 12, 24, 12), BackColor = Page };
        var closeButton = new RoundedButton { Text = "关闭", Anchor = AnchorStyles.Top | AnchorStyles.Right };
        StylePrimaryButton(closeButton, 104);
        closeButton.Location = new Point(footer.ClientSize.Width - 128, 12);
        closeButton.Click += (_, _) => Close();

        var copyButton = new RoundedButton { Text = "复制诊断报告", Anchor = AnchorStyles.Top | AnchorStyles.Right };
        StyleSecondaryButton(copyButton, 132);
        copyButton.Location = new Point(footer.ClientSize.Width - 270, 12);
        copyButton.Click += (_, _) =>
        {
            Clipboard.SetText(BuildReport());
            copyButton.Text = "已复制";
        };
        footer.Controls.AddRange(new Control[] { copyButton, closeButton });
        footer.Resize += (_, _) =>
        {
            closeButton.Left = footer.ClientSize.Width - closeButton.Width - 24;
            copyButton.Left = closeButton.Left - copyButton.Width - 10;
        };
        root.Controls.Add(footer, 0, 3);
    }

    private DataGridView CreateIssuesGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Color.FromArgb(231, 235, 241),
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersHeight = 42;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 243, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(7, 0, 0, 0);
        grid.DefaultCellStyle.Padding = new Padding(7, 7, 5, 7);
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 41, 59);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 254);

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "级别", Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "配置项", Width = 112 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "作用域", Width = 94 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "当前值", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "具体问题", Width = 230 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "建议修改", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 210 });

        IReadOnlyList<EnvironmentIssue> rows = status.Issues;
        if (rows.Count == 0)
        {
            int index = grid.Rows.Add("正常", "Oracle 环境", "系统", status.ConfiguredPath ?? "已配置", "未发现冲突或失效路径。", "无需修改。");
            grid.Rows[index].Cells[0].Style.ForeColor = Color.FromArgb(4, 120, 87);
        }
        else
        {
            foreach (EnvironmentIssue issue in rows)
            {
                string level = issue.Severity switch
                {
                    EnvironmentIssueSeverity.Error => "错误",
                    EnvironmentIssueSeverity.Warning => "警告",
                    _ => "提示"
                };
                int index = grid.Rows.Add(level, issue.Item, issue.Scope, issue.CurrentValue, issue.Problem, issue.Recommendation);
                grid.Rows[index].Cells[0].Style.ForeColor = issue.Severity switch
                {
                    EnvironmentIssueSeverity.Error => Color.FromArgb(185, 28, 28),
                    EnvironmentIssueSeverity.Warning => Color.FromArgb(180, 83, 9),
                    _ => Color.FromArgb(37, 99, 235)
                };
                grid.Rows[index].Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
            }
        }
        return grid;
    }

    private string BuildReport()
    {
        var text = new StringBuilder();
        text.AppendLine("Oracle 环境诊断报告");
        text.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"状态：{status.Summary}");
        text.AppendLine(status.Detail);
        text.AppendLine();
        if (status.Issues.Count == 0)
        {
            text.AppendLine("未发现需要修改的配置。");
        }
        else
        {
            foreach (EnvironmentIssue issue in status.Issues)
            {
                text.AppendLine($"[{issue.Severity}] {issue.Item}（{issue.Scope}）");
                text.AppendLine($"当前值：{issue.CurrentValue}");
                text.AppendLine($"问题：{issue.Problem}");
                text.AppendLine($"建议：{issue.Recommendation}");
                text.AppendLine();
            }
        }
        return text.ToString();
    }

    private static void StylePrimaryButton(Button button, int width)
    {
        button.Size = new Size(width, 38);
        button.BackColor = Color.FromArgb(37, 99, 235);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font(button.Font, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static void StyleSecondaryButton(Button button, int width)
    {
        button.Size = new Size(width, 38);
        button.BackColor = Color.White;
        button.ForeColor = Navy;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
    }
}
