using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class DocumentComparisonDialog : Form
{
    public DocumentComparisonDialog(string editorMarkdown, string diskMarkdown)
    {
        Text = Loc.Get("dialog.compareTitle");
        BackColor = SystemColors.ControlLightLight;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(this.ScaleForDpi(514), this.ScaleForDpi(343));
        Size = new Size(this.ScaleForDpi(629), this.ScaleForDpi(411));
        ShowInTaskbar = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = this.ScaleForDpi(309),
        };
        split.Panel1.Controls.Add(CreateTextView(Loc.Get("dialog.compareEditor"), editorMarkdown));
        split.Panel2.Controls.Add(CreateTextView(Loc.Get("dialog.compareDisk"), diskMarkdown));

        var close = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(this.ScaleForDpi(50), 0),
            Padding = new Padding(this.ScaleForDpi(7), this.ScaleForDpi(2), this.ScaleForDpi(7), this.ScaleForDpi(2)),
            FlatStyle = FlatStyle.System,
            Text = Loc.Get("common.close"),
            DialogResult = DialogResult.OK,
            UseVisualStyleBackColor = true,
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = this.ScaleForDpi(27),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(this.ScaleForDpi(5)),
        };
        footer.Controls.Add(close);
        Controls.Add(split);
        Controls.Add(footer);
        AcceptButton = close;
        CancelButton = close;
    }

    private Control CreateTextView(string title, string text)
    {
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = this.ScaleForDpi(18),
            Padding = new Padding(this.ScaleForDpi(5), 0, 0, 0),
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
        var editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10F),
            Text = text,
        };
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(this.ScaleForDpi(5)) };
        panel.Controls.Add(editor);
        panel.Controls.Add(label);
        return panel;
    }
}
