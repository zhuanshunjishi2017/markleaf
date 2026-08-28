using MarkLeaf.Services;
using MarkLeaf.Services.Recovery;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal enum RecoveryChoice { Restore, Discard, Cancel }

internal sealed class RecoveryDialog : Form
{
    public RecoveryChoice Choice { get; private set; } = RecoveryChoice.Cancel;
    public RecoverySnapshot? SelectedSnapshot { get; private set; }

    public RecoveryDialog(IReadOnlyList<RecoverySnapshot> recoveries)
    {
        Text = Loc.Get("dialog.recoveryTitle");
        BackColor = SystemColors.ControlLightLight;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Size = new Size(this.ScaleForDpi(366), this.ScaleForDpi(257));
        MinimumSize = new Size(this.ScaleForDpi(366), this.ScaleForDpi(257));
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(this.ScaleForDpi(11), this.ScaleForDpi(11), this.ScaleForDpi(11), this.ScaleForDpi(9));
        Font = new Font("Segoe UI", 9F);

        var warningText = recoveries.Count switch
        {
            0 => Loc.Get("dialog.recoveryNone"),
            1 => Loc.Get("dialog.recoveryOne"),
            _ => Loc.Format("dialog.recoveryMultiple", recoveries.Count),
        };

        var warningLabel = new Label
        {
            Text = warningText,
            AutoSize = true,
            MaximumSize = new Size(this.ScaleForDpi(343), 0),
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(5)),
        };

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            SelectionMode = recoveries.Count > 0 ? SelectionMode.One : SelectionMode.None,
        };
        foreach (var recovery in recoveries)
        {
            var displayName = recovery.DisplayName ?? Loc.Get("common.unnamed");
            var when = recovery.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var pathHint = recovery.DocumentPath is not null
                ? $"{Path.GetFileName(recovery.DocumentPath)}"
                : string.Empty;
            listBox.Items.Add(Loc.Format("dialog.recoverySnapshotSaved", displayName, when));
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
            Text = Loc.Get("dialog.recoveryInstruction"),
            AutoSize = true,
            MaximumSize = new Size(this.ScaleForDpi(320), 0),
            Padding = new Padding(0, this.ScaleForDpi(3), 0, 0),
            ForeColor = SystemColors.GrayText,
        };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Margin = new Padding(0, this.ScaleForDpi(6), 0, 0),
        };

        var discardButton = new Button
        {
            Text = Loc.Get("dialog.recoveryDiscardAll"),
            AutoSize = true,
            MinimumSize = new Size(this.ScaleForDpi(74), 0),
            Padding = new Padding(this.ScaleForDpi(7), this.ScaleForDpi(2), this.ScaleForDpi(7), this.ScaleForDpi(2)),
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
            Text = Loc.Get("dialog.recoverySaveAs"),
            AutoSize = true,
            MinimumSize = new Size(this.ScaleForDpi(74), 0),
            Padding = new Padding(this.ScaleForDpi(7), this.ScaleForDpi(2), this.ScaleForDpi(7), this.ScaleForDpi(2)),
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
