using MarkLeaf.Services;

namespace MarkLeaf.UI.Dialogs;

internal enum ExternalChangeChoice
{
    Cancel,
    Reload,
    Compare,
    SaveAs,
    ForceOverwrite,
}

internal sealed class ExternalChangeDialog : Form
{
    public ExternalChangeDialog(string fileName)
    {
        Text = Loc.Get("dialog.externalChangeTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(16);
        FormClosing += (_, _) => Choice = Choice == default ? ExternalChangeChoice.Cancel : Choice;

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = Loc.Format("document.externalChangeMessage", fileName),
        };
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 16, 0, 0),
        };
        actions.Controls.Add(CreateButton(Loc.Get("dialog.externalChangeReload"), ExternalChangeChoice.Reload));
        actions.Controls.Add(CreateButton(Loc.Get("dialog.externalChangeCompare"), ExternalChangeChoice.Compare));
        actions.Controls.Add(CreateButton(Loc.Get("dialog.externalChangeSaveAs"), ExternalChangeChoice.SaveAs));
        actions.Controls.Add(CreateButton(Loc.Get("dialog.externalChangeForceOverwrite"), ExternalChangeChoice.ForceOverwrite));
        actions.Controls.Add(CreateButton(Loc.Get("common.cancel"), ExternalChangeChoice.Cancel));

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
    }

    public ExternalChangeChoice Choice { get; private set; }

    private Button CreateButton(string text, ExternalChangeChoice choice)
    {
        var button = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(88, 0),
            Padding = new Padding(10, 3, 10, 3),
            FlatStyle = FlatStyle.System,
            Text = text,
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
