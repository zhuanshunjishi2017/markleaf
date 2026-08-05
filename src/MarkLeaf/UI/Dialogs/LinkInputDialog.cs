namespace MarkLeaf.UI.Dialogs;

internal sealed class LinkInputDialog : Form
{
    private readonly TextBox _address = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "https://example.com",
    };

    public LinkInputDialog()
    {
        Text = "插入链接";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var label = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = "链接地址(&A)：",
            Padding = new Padding(0, 0, 0, 6),
        };
        label.UseMnemonic = true;

        var okButton = new Button
        {
            AutoSize = true,
            Text = "确定",
            DialogResult = DialogResult.OK,
            Enabled = false,
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            Text = "取消",
            DialogResult = DialogResult.Cancel,
        };
        _address.TextChanged += (_, _) => okButton.Enabled = IsAllowedLink(_address.Text);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            MinimumSize = new Size(420, 0),
        };
        content.Controls.Add(label, 0, 0);
        content.Controls.Add(_address, 0, 1);
        content.Controls.Add(buttons, 0, 2);
        Controls.Add(content);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string LinkAddress => _address.Text.Trim();

    private static bool IsAllowedLink(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps
                || uri.Scheme == Uri.UriSchemeMailto);
    }
}
