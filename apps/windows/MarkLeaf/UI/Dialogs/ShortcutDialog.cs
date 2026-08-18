using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class ShortcutDialog : Form
{
    private static readonly string[] ShortcutKeys =
    [
        "Ctrl+N", "Ctrl+O", "Ctrl+S", "Ctrl+Shift+S",
        "Ctrl+Z", "Ctrl+Y", "Ctrl+X", "Ctrl+C", "Ctrl+V", "Ctrl+Shift+V",
        "Ctrl+F", "Ctrl+H", "Ctrl+B", "Ctrl+I", "Ctrl+K",
        "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+5", "Ctrl+6",
    ];

    private static readonly string[] ShortcutDescKeys =
    [
        "shortcut.new", "shortcut.open", "shortcut.save", "shortcut.saveAs",
        "shortcut.undo", "shortcut.redo", "shortcut.cut", "shortcut.copy", "shortcut.paste", "shortcut.pastePlainText",
        "shortcut.find", "shortcut.replace", "shortcut.toggleBold", "shortcut.toggleItalic", "shortcut.insertLink",
        "shortcut.heading1", "shortcut.heading2", "shortcut.heading3",
        "shortcut.heading4", "shortcut.heading5", "shortcut.heading6",
    ];

    public ShortcutDialog()
    {
        Text = Loc.Get("dialog.shortcutsTitle");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(this.ScaleForDpi(343), this.ScaleForDpi(354));
        MinimumSize = new Size(this.ScaleForDpi(274), this.ScaleForDpi(251));
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(this.ScaleForDpi(9), this.ScaleForDpi(9), this.ScaleForDpi(9), this.ScaleForDpi(7));

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
            HeaderText = Loc.Get("dialog.shortcutsColumnKey"),
            Name = "Shortcut",
            FillWeight = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        var descriptionColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = Loc.Get("dialog.shortcutsColumnDesc"),
            Name = "Description",
            FillWeight = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        grid.Columns.Add(shortcutColumn);
        grid.Columns.Add(descriptionColumn);

        for (int i = 0; i < ShortcutKeys.Length; i++)
        {
            grid.Rows.Add(ShortcutKeys[i], Loc.Get(ShortcutDescKeys[i]));
        }

        grid.ClearSelection();

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Margin = new Padding(0, this.ScaleForDpi(6), 0, 0),
        };

        var closeButton = new Button
        {
            Text = Loc.Get("common.close"),
            AutoSize = true,
            MinimumSize = new Size(this.ScaleForDpi(50), 0),
            Padding = new Padding(this.ScaleForDpi(7), this.ScaleForDpi(2), this.ScaleForDpi(7), this.ScaleForDpi(2)),
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
