using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class UnsafeEmphasisDialog : Form
{
    private readonly CheckBox _remember = new()
    {
        AutoSize = true,
        FlatStyle = FlatStyle.System,
    };
    private readonly Label _details = new()
    {
        AutoSize = true,
        Visible = false,
    };
    private readonly LinkLabel _learnMore = new()
    {
        AutoSize = true,
        LinkBehavior = LinkBehavior.HoverUnderline,
    };

    private string _action = "literal";

    public UnsafeEmphasisDialog(string kind)
    {
        var isBold = string.Equals(kind, "bold", StringComparison.Ordinal);
        Text = Loc.Get("dialog.unsafeEmphasisTitle");
        BackColor = SystemColors.ControlLightLight;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _remember.Text = Loc.Get("dialog.unsafeEmphasisRemember");
        _learnMore.Text = Loc.Get("dialog.unsafeEmphasisLearnMore");
        _learnMore.LinkClicked += (_, _) =>
        {
            _details.Visible = true;
            _learnMore.Visible = false;
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(this.ScaleForDpi(420), 0),
            Text = isBold
                ? Loc.Get("dialog.unsafeEmphasisBold")
                : Loc.Get("dialog.unsafeEmphasisItalic"),
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(8)),
        };
        _details.MaximumSize = new Size(this.ScaleForDpi(420), 0);
        _details.Text = Loc.Get("dialog.unsafeEmphasisDetails");
        _details.Padding = new Padding(0, this.ScaleForDpi(4), 0, this.ScaleForDpi(8));

        var literalButton = CreateButton(Loc.Get("dialog.unsafeEmphasisKeepLiteral"), "literal");
        var htmlButton = CreateButton(Loc.Get("dialog.unsafeEmphasisConvertHtml"), "html");

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, this.ScaleForDpi(8), 0, 0),
        };
        buttons.Controls.Add(htmlButton);
        buttons.Controls.Add(literalButton);

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(this.ScaleForDpi(12)),
            MinimumSize = new Size(this.ScaleForDpi(440), 0),
        };
        content.Controls.Add(message, 0, 0);
        content.Controls.Add(_learnMore, 0, 1);
        content.Controls.Add(_details, 0, 2);
        content.Controls.Add(_remember, 0, 3);
        content.Controls.Add(buttons, 0, 4);
        Controls.Add(content);

        AcceptButton = htmlButton;
        CancelButton = literalButton;
    }

    public string Action => _action;

    public bool RememberChoice => _remember.Checked;

    private Button CreateButton(string text, string action)
    {
        var button = new Button
        {
            AutoSize = true,
            Text = text,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.System,
        };
        button.Click += (_, _) => _action = action;
        return button;
    }
}
