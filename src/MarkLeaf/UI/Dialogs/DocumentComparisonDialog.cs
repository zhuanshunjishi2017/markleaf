namespace MarkLeaf.UI.Dialogs;

internal sealed class DocumentComparisonDialog : Form
{
    public DocumentComparisonDialog(string editorMarkdown, string diskMarkdown)
    {
        Text = "比较当前编辑内容与磁盘版本";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 600);
        Size = new Size(1100, 720);
        ShowInTaskbar = false;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 540,
        };
        split.Panel1.Controls.Add(CreateTextView("当前编辑内容", editorMarkdown));
        split.Panel2.Controls.Add(CreateTextView("磁盘版本", diskMarkdown));

        var close = new Button
        {
            AutoSize = true,
            MinimumSize = new Size(88, 0),
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.System,
            Text = "关闭",
            DialogResult = DialogResult.OK,
            UseVisualStyleBackColor = true,
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
        };
        footer.Controls.Add(close);
        Controls.Add(split);
        Controls.Add(footer);
        AcceptButton = close;
        CancelButton = close;
    }

    private static Control CreateTextView(string title, string text)
    {
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(8, 0, 0, 0),
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
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        panel.Controls.Add(editor);
        panel.Controls.Add(label);
        return panel;
    }
}
