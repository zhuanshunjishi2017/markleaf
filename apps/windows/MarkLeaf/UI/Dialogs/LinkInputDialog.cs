using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class LinkInputDialog : Form
{
    private readonly TextBox _address = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "https://example.com",
    };

    public LinkInputDialog(string? initialAddress = null)
    {
        Text = Loc.Get("dialog.insertLinkTitle");
        BackColor = DialogColors.Secondary;
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
            Text = Loc.Get("dialog.linkAddress"),
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(3)),
        };
        label.UseMnemonic = true;

        var okButton = new Button
        {
            AutoSize = true,
            Text = Loc.Get("common.ok"),
            DialogResult = DialogResult.OK,
            Enabled = false,
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            Text = Loc.Get("common.cancel"),
            DialogResult = DialogResult.Cancel,
        };
        _address.TextChanged += (_, _) => okButton.Enabled = IsAllowedLink(_address.Text);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, this.ScaleForDpi(7), 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(this.ScaleForDpi(8)),
            MinimumSize = new Size(this.ScaleForDpi(240), 0),
        };
        content.Controls.Add(label, 0, 0);
        content.Controls.Add(_address, 0, 1);
        content.Controls.Add(buttons, 0, 2);
        Controls.Add(content);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        _address.Text = initialAddress ?? string.Empty;
    }

    public string LinkAddress => _address.Text.Trim();

    private static bool IsAllowedLink(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Any(char.IsControl)) return false;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "mailto" or "file";
        return !text.Contains(':');
    }
}
