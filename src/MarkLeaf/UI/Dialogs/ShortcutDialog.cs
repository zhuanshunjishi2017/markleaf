namespace MarkLeaf.UI.Dialogs;

internal sealed class ShortcutDialog : Form
{
    private static readonly IReadOnlyList<(string Shortcut, string Description)> Shortcuts =
    [
        ("Ctrl+N", "新建文档"),
        ("Ctrl+O", "打开文档"),
        ("Ctrl+S", "保存文档"),
        ("Ctrl+Shift+S", "另存为"),
        ("Ctrl+Z", "撤销"),
        ("Ctrl+Y", "重做"),
        ("Ctrl+X", "剪切"),
        ("Ctrl+C", "复制"),
        ("Ctrl+V", "粘贴"),
        ("Ctrl+F", "查找"),
        ("Ctrl+H", "替换"),
        ("Ctrl+B", "切换粗体"),
        ("Ctrl+I", "切换斜体"),
        ("Ctrl+K", "插入链接"),
        ("Ctrl+1", "标题 1"),
        ("Ctrl+2", "标题 2"),
        ("Ctrl+3", "标题 3"),
        ("Ctrl+4", "标题 4"),
        ("Ctrl+5", "标题 5"),
        ("Ctrl+6", "标题 6"),
    ];

    public ShortcutDialog()
    {
        Text = "MarkLeaf 快捷键";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(600, 620);
        MinimumSize = new Size(480, 440);
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(16, 16, 16, 12);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font(Font, FontStyle.Bold),
                BackColor = SystemColors.Control,
                ForeColor = SystemColors.ControlText,
                SelectionBackColor = SystemColors.Control,
                SelectionForeColor = SystemColors.ControlText,
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                SelectionBackColor = SystemColors.Highlight,
                SelectionForeColor = SystemColors.HighlightText,
            },
        };

        var shortcutColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "快捷键",
            Name = "Shortcut",
            FillWeight = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        var descriptionColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "描述",
            Name = "Description",
            FillWeight = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        grid.Columns.Add(shortcutColumn);
        grid.Columns.Add(descriptionColumn);

        foreach (var (shortcut, description) in Shortcuts)
        {
            grid.Rows.Add(shortcut, description);
        }

        grid.ClearSelection();

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
        };

        var closeButton = new Button
        {
            Text = "关闭",
            AutoSize = true,
            MinimumSize = new Size(88, 0),
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
        };
        closeButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(closeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(grid, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(layout);
    }
}
