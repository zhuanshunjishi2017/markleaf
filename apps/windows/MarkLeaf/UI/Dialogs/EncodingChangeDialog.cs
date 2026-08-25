using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal enum EncodingChangeChoice
{
    Cancel,
    DirectRead,
    ConvertEncoding,
}

internal sealed class EncodingChangeDialog : Form
{
    public EncodingChangeDialog(string currentEncoding, string targetEncoding)
    {
        Text = Loc.Get("encoding.warningTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(this.ScaleForDpi(9));
        FormClosing += (_, _) => Choice = Choice == default ? EncodingChangeChoice.Cancel : Choice;

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(this.ScaleForDpi(380), 0),
            Text = Loc.Format("encoding.changePrompt", currentEncoding, targetEncoding),
        };
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, this.ScaleForDpi(9), 0, 0),
        };
        var directReadButton = CreateButton(
            Loc.Get("encoding.directRead"),
            EncodingChangeChoice.DirectRead,
            DialogResult.OK);
        var convertButton = CreateButton(
            Loc.Get("encoding.convertEncoding"),
            EncodingChangeChoice.ConvertEncoding,
            DialogResult.OK);
        var cancelButton = CreateButton(
            Loc.Get("common.cancel"),
            EncodingChangeChoice.Cancel,
            DialogResult.Cancel);
        actions.Controls.Add(directReadButton);
        actions.Controls.Add(convertButton);
        actions.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
        };
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        Controls.Add(layout);
        AcceptButton = directReadButton;
        CancelButton = cancelButton;
    }

    public EncodingChangeChoice Choice { get; private set; }

    private Button CreateButton(string text, EncodingChangeChoice choice, DialogResult result)
    {
        var button = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(this.ScaleForDpi(64), 0),
            Padding = new Padding(this.ScaleForDpi(6), this.ScaleForDpi(2), this.ScaleForDpi(6), this.ScaleForDpi(2)),
            FlatStyle = FlatStyle.System,
            Text = text,
            DialogResult = result,
            UseVisualStyleBackColor = true,
        };
        button.Click += (_, _) =>
        {
            Choice = choice;
            Close();
        };
        return button;
    }
}
