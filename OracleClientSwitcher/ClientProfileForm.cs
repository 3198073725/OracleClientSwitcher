namespace OracleClientSwitcher;

internal sealed class ClientProfileForm : Form
{
    private static readonly Color Navy = Color.FromArgb(20, 42, 74);
    private static readonly Color Blue = Color.FromArgb(37, 99, 235);
    private static readonly Color Page = Color.FromArgb(244, 247, 251);
    private readonly OracleClientInfo client;
    private readonly ClientProfileSettings settings;
    private readonly OracleClientProfile profile;
    private readonly TextBox nameText = new();
    private readonly CheckBox favoriteCheck = new();
    private readonly CheckBox hiddenCheck = new();
    private readonly TextBox notesText = new();
    private readonly ComboBox sortCombo = new();
    private readonly CheckBox showHiddenCheck = new();

    public ClientProfileForm(OracleClientInfo client, ClientProfileSettings settings)
    {
        this.client = client;
        this.settings = settings;
        profile = ClientProfileManager.GetProfile(settings, client.PathDirectory);
        Text = "客户端收藏与备注";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(680, 620);
        MinimumSize = new Size(600, 560);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 9F);
        ShowInTaskbar = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        LoadValues();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Page };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        Controls.Add(root);

        var header = new GradientPanel { Dock = DockStyle.Fill, StartColor = Navy, EndColor = Color.FromArgb(24, 61, 105), Margin = Padding.Empty };
        header.Controls.Add(new Label { Text = "客户端收藏与备注", ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Location = new Point(28, 10) });
        header.Controls.Add(new Label { Text = $"Oracle {client.Version} · {client.Architecture}", ForeColor = Color.FromArgb(211, 225, 243), BackColor = Color.Transparent, AutoSize = true, Location = new Point(31, 54) });
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = new Padding(24, 18, 24, 10), Padding = new Padding(18), BackColor = Color.White, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        body.Controls.Add(LabelFor("显示名称（例如：Navicat 11g 64位）"), 0, 0);
        nameText.Dock = DockStyle.Fill;
        nameText.Margin = new Padding(0, 4, 0, 6);
        body.Controls.Add(nameText, 0, 1);
        var flags = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        favoriteCheck.Text = "★ 收藏并置顶";
        favoriteCheck.AutoSize = true;
        favoriteCheck.Margin = new Padding(0, 9, 28, 0);
        hiddenCheck.Text = "隐藏该扫描结果";
        hiddenCheck.AutoSize = true;
        hiddenCheck.Margin = new Padding(0, 9, 0, 0);
        flags.Controls.Add(favoriteCheck);
        flags.Controls.Add(hiddenCheck);
        body.Controls.Add(flags, 0, 2);
        body.Controls.Add(LabelFor("用途和备注"), 0, 3);
        notesText.Dock = DockStyle.Fill;
        notesText.Multiline = true;
        notesText.ScrollBars = ScrollBars.Vertical;
        notesText.MinimumSize = new Size(0, 72);
        body.Controls.Add(notesText, 0, 4);
        body.Controls.Add(LabelFor("列表偏好"), 0, 5);
        var preferences = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        sortCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        sortCombo.Items.AddRange(new object[] { "收藏优先", "版本优先", "名称排序", "最近选择" });
        sortCombo.Width = 180;
        sortCombo.Margin = new Padding(0, 4, 24, 4);
        showHiddenCheck.Text = "显示已隐藏的客户端";
        showHiddenCheck.AutoSize = true;
        showHiddenCheck.Margin = new Padding(0, 9, 0, 0);
        preferences.Controls.Add(sortCombo);
        preferences.Controls.Add(showHiddenCheck);
        body.Controls.Add(preferences, 0, 6);
        body.Controls.Add(new Label { Text = client.PathDirectory, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(100, 116, 139), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
        root.Controls.Add(body, 0, 1);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var cancel = ButtonFor("取消", false, 88);
        cancel.DialogResult = DialogResult.Cancel;
        var save = ButtonFor("保存", true, 104);
        save.Click += (_, _) => Save();
        void Align()
        {
            save.Location = new Point(footer.ClientSize.Width - save.Width - 22, 15);
            cancel.Location = new Point(save.Left - cancel.Width - 10, 15);
        }
        footer.Resize += (_, _) => Align();
        footer.Controls.Add(cancel);
        footer.Controls.Add(save);
        Align();
        root.Controls.Add(footer, 0, 2);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void LoadValues()
    {
        nameText.Text = profile.DisplayName;
        favoriteCheck.Checked = profile.Favorite;
        hiddenCheck.Checked = profile.Hidden;
        notesText.Text = profile.Notes;
        sortCombo.SelectedIndex = (int)settings.SortMode;
        showHiddenCheck.Checked = settings.ShowHidden;
    }

    private void Save()
    {
        profile.Path = OracleScanner.Normalize(client.PathDirectory);
        profile.DisplayName = nameText.Text.Trim();
        profile.Favorite = favoriteCheck.Checked;
        profile.Hidden = hiddenCheck.Checked;
        profile.Notes = notesText.Text.Trim();
        settings.SortMode = (ClientSortMode)Math.Max(0, sortCombo.SelectedIndex);
        settings.ShowHidden = showHiddenCheck.Checked;
        TnsOperationResult result = ClientProfileManager.Save(settings);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        AppLog.Information("客户端", "更新收藏与备注", "成功", client.PathDirectory);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label LabelFor(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Navy, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };

    private static Button ButtonFor(string text, bool primary, int width)
    {
        var button = new RoundedButton { Text = text, Size = new Size(width, 38), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Navy, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Color.FromArgb(239, 246, 255);
        return button;
    }
}
