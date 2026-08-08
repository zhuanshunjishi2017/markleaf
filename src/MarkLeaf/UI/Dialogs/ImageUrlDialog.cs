using MarkLeaf.Services;

namespace MarkLeaf.UI.Dialogs;

internal sealed class ImageUrlDialog : Form
{
    private readonly TextBox _url = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "https://example.com/image.png",
    };

    private readonly TextBox _alt = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = Loc.Get("dialog.imageAltPlaceholder"),
    };

    public ImageUrlDialog()
    {
        Text = Loc.Get("dialog.insertImageUrlTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var urlLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = Loc.Get("dialog.imageUrl"),
            Padding = new Padding(0, 0, 0, 6),
        };
        urlLabel.UseMnemonic = true;

        var altLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = Loc.Get("dialog.imageAlt"),
            Padding = new Padding(0, 8, 0, 6),
        };
        altLabel.UseMnemonic = true;

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
        _url.TextChanged += (_, _) => okButton.Enabled = IsAllowedImageUrl(_url.Text);

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
            RowCount = 4,
            Padding = new Padding(14),
            MinimumSize = new Size(460, 0),
        };
        content.Controls.Add(urlLabel, 0, 0);
        content.Controls.Add(_url, 0, 1);
        content.Controls.Add(altLabel, 0, 2);
        content.Controls.Add(_alt, 0, 3);
        content.Controls.Add(buttons, 0, 4);
        Controls.Add(content);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string ImageUrl => _url.Text.Trim();

    public string AltText => _alt.Text.Trim();

    private static bool IsAllowedImageUrl(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp
                || uri.Scheme == Uri.UriSchemeHttps);
    }
}
