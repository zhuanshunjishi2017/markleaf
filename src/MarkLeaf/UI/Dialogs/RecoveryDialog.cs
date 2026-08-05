using MarkLeaf.Services.Recovery;

namespace MarkLeaf.UI.Dialogs;

internal enum RecoveryChoice { Restore, Discard, Cancel }

internal sealed class RecoveryDialog : Form
{
    public RecoveryChoice Choice { get; private set; } = RecoveryChoice.Cancel;
    public RecoverySnapshot? SelectedSnapshot { get; private set; }

    public RecoveryDialog(IReadOnlyList<RecoverySnapshot> recoveries)
    {
        Text = "恢复未保存的文档";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Size = new Size(640, 450);
        MinimumSize = new Size(640, 450);
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(20, 20, 20, 16);
        Font = new Font("Segoe UI", 9F);

        var warningText = recoveries.Count switch
        {
            0 => "未发现需要恢复的文档。",
            1 => "发现 1 份未保存文档，该文档来自上次异常关闭前的自动快照。",
            _ => $"发现 {recoveries.Count} 份未保存文档，它们来自上次异常关闭前的自动快照。",
        };

        var warningLabel = new Label
        {
            Text = warningText,
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            Padding = new Padding(0, 0, 0, 8),
        };

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            SelectionMode = recoveries.Count > 0 ? SelectionMode.One : SelectionMode.None,
        };
        foreach (var recovery in recoveries)
        {
            var displayName = recovery.DisplayName ?? "未命名";
            var when = recovery.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var pathHint = recovery.DocumentPath is not null
                ? $"{Path.GetFileName(recovery.DocumentPath)}"
                : string.Empty;
            listBox.Items.Add($"{displayName} - 快照保存于{when}");
        }

        if (recoveries.Count > 0) listBox.SelectedIndex = 0;

        listBox.SelectedIndexChanged += (_, _) =>
        {
            SelectedSnapshot = listBox.SelectedIndex >= 0 && listBox.SelectedIndex < recoveries.Count
                ? recoveries[listBox.SelectedIndex]
                : null;
        };

        if (recoveries.Count > 0) SelectedSnapshot = recoveries[0];

        var infoLabel = new Label
        {
            Text = "选择需要恢复的文档后单击“另存为”可将快照保存到指定位置并打开。",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Padding = new Padding(0, 6, 0, 0),
            ForeColor = SystemColors.GrayText,
        };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
        };

        var discardButton = new Button
        {
            Text = "全部丢弃(&D)",
            AutoSize = true,
            MinimumSize = new Size(130, 0),
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
        };
        discardButton.Click += (_, _) =>
        {
            Choice = RecoveryChoice.Discard;
            SelectedSnapshot = null;
            Close();
        };

        var restoreButton = new Button
        {
            Text = "另存为(&S)",
            AutoSize = true,
            MinimumSize = new Size(130, 0),
            Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
        };
        restoreButton.Click += (_, _) =>
        {
            Choice = RecoveryChoice.Restore;
            Close();
        };
        restoreButton.Enabled = recoveries.Count > 0;

        buttonPanel.Controls.Add(discardButton);
        buttonPanel.Controls.Add(restoreButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(warningLabel, 0, 0);
        layout.Controls.Add(listBox, 0, 1);
        layout.Controls.Add(infoLabel, 0, 2);
        layout.Controls.Add(buttonPanel, 0, 3);
        Controls.Add(layout);
    }
}
