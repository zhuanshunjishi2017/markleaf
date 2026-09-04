using MarkLeaf.Commands;
using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

/// <summary>
/// 快捷键设置窗口（对应 macOS ShortcutWindowController）：列出可自定义命令，
/// 支持录制新快捷键、清除、恢复默认、全部恢复默认。
/// 变更直接写入 ShortcutManager（其 Changed 由 MainForm 负责重建菜单并保存设置）。
/// </summary>
internal sealed class ShortcutDialog : Form
{
    private readonly ShortcutManager _shortcutManager;
    private readonly DataGridView _grid;
    private readonly Label _statusLabel;
    private AppCommand? _recordingCommand;
    private Keys _heldModifiers;
    private Keys _heldKey;

    public ShortcutDialog(ShortcutManager shortcutManager)
    {
        _shortcutManager = shortcutManager;

        Text = Loc.Get("dialog.shortcutsTitle");
        BackColor = SystemColors.ControlLightLight;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(this.ScaleForDpi(440), this.ScaleForDpi(480));
        MinimumSize = new Size(this.ScaleForDpi(340), this.ScaleForDpi(360));

        _grid = BuildGrid();
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Empty,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var changeButton = CreateButton(Loc.Get("dialog.shortcuts.change"), StartRecording);
        var clearButton = CreateButton(Loc.Get("dialog.shortcuts.clear"), ClearShortcut);
        var restoreButton = CreateButton(Loc.Get("dialog.shortcuts.restore"), RestoreDefault);
        var resetAllButton = CreateButton(Loc.Get("dialog.shortcuts.resetAll"), ResetAll);
        var closeButton = CreateButton(Loc.Get("common.close"), Close);
        var buttonHeight = changeButton.PreferredSize.Height;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        buttons.Controls.Add(changeButton);
        buttons.Controls.Add(clearButton);
        buttons.Controls.Add(restoreButton);
        buttons.Controls.Add(resetAllButton);

        var bottomBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomBar.Controls.Add(buttons, 0, 0);
        bottomBar.Controls.Add(closeButton, 1, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(this.ScaleForDpi(9)),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonHeight + this.ScaleForDpi(10)));
        layout.Controls.Add(_grid, 0, 0);
        layout.Controls.Add(_statusLabel, 0, 1);
        layout.Controls.Add(bottomBar, 0, 2);
        Controls.Add(layout);

        _grid.CellDoubleClick += (_, _) => StartRecording();
        ReloadGrid();
    }

    private DataGridView BuildGrid()
    {
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
        shortcutColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        shortcutColumn.DefaultCellStyle.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

        var descriptionColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = Loc.Get("dialog.shortcutsColumnDesc"),
            Name = "Description",
            FillWeight = 60,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

        grid.Columns.Add(shortcutColumn);
        grid.Columns.Add(descriptionColumn);
        return grid;
    }

    private static Button CreateButton(string text, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void ReloadGrid()
    {
        _grid.Rows.Clear();
        foreach (var entry in ShortcutCatalog.Entries)
        {
            var shortcut = _shortcutManager.GetShortcutText(entry.Command);
            var index = _grid.Rows.Add(shortcut ?? "—", Loc.Get(entry.DescriptionKey));
            _grid.Rows[index].Tag = entry;
        }

        _grid.ClearSelection();
    }

    private ShortcutCatalog.Entry? SelectedEntry =>
        _grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is ShortcutCatalog.Entry entry
            ? entry
            : null;

    private void StartRecording()
    {
        if (SelectedEntry is not { } entry)
        {
            SetStatus(Loc.Get("dialog.shortcuts.selectPrompt"));
            return;
        }

        _recordingCommand = entry.Command;
        _heldModifiers = Keys.None;
        _heldKey = Keys.None;
        SetStatus(Loc.Get("dialog.shortcuts.recordPrompt"));
    }

    private void ClearShortcut()
    {
        if (SelectedEntry is not { } entry)
        {
            SetStatus(Loc.Get("dialog.shortcuts.selectPrompt"));
            return;
        }

        _shortcutManager.Clear(entry.Command);
        SetStatus(string.Empty);
        ReloadGrid();
    }

    private void RestoreDefault()
    {
        if (SelectedEntry is not { } entry)
        {
            SetStatus(Loc.Get("dialog.shortcuts.selectPrompt"));
            return;
        }

        _shortcutManager.RestoreDefault(entry.Command);
        SetStatus(string.Empty);
        ReloadGrid();
    }

    private void ResetAll()
    {
        _shortcutManager.ResetAll();
        SetStatus(string.Empty);
        ReloadGrid();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_recordingCommand is not AppCommand command)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        var keyCode = e.KeyCode & Keys.KeyCode;
        if (keyCode == Keys.Escape)
        {
            CancelRecording();
            return;
        }

        if (IsModifierKey(keyCode))
        {
            _heldModifiers = e.Modifiers;
        }
        else
        {
            _heldKey = keyCode;
            _heldModifiers = e.Modifiers;
        }

        UpdateRecordingStatus();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_recordingCommand is not AppCommand command)
        {
            base.OnKeyUp(e);
            return;
        }

        var keyCode = e.KeyCode & Keys.KeyCode;
        if (IsModifierKey(keyCode))
        {
            e.Handled = true;
            if (_heldKey == Keys.None)
            {
                _heldModifiers = Keys.None;
                UpdateRecordingStatus();
            }

            return;
        }

        if (keyCode == _heldKey)
        {
            e.Handled = true;
            FinishRecording(command, e.Modifiers | _heldKey);
        }
    }

    private static bool IsModifierKey(Keys keyCode) =>
        keyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu;

    private void FinishRecording(AppCommand command, Keys keys)
    {
        _recordingCommand = null;
        _heldModifiers = Keys.None;
        _heldKey = Keys.None;

        var normalized = keys & (Keys.KeyCode | Keys.Modifiers);
        var conflict = _shortcutManager.Validate(normalized, command);
        switch (conflict.Kind)
        {
            case ShortcutConflictKind.None:
                _shortcutManager.Set(command, normalized);
                SetStatus(string.Empty);
                break;
            case ShortcutConflictKind.Invalid:
                SetStatus(Loc.Get("dialog.shortcuts.invalid"));
                break;
            case ShortcutConflictKind.Duplicate:
                var otherDescription = ShortcutCatalog.Find(conflict.OtherCommand) is { } other
                    ? Loc.Get(other.DescriptionKey)
                    : conflict.OtherCommand.ToString();
                SetStatus(Loc.Format("dialog.shortcuts.duplicate", otherDescription));
                break;
        }

        ReloadGrid();
    }

    private void CancelRecording()
    {
        _recordingCommand = null;
        _heldModifiers = Keys.None;
        _heldKey = Keys.None;
        SetStatus(string.Empty);
        ReloadGrid();
    }

    private void UpdateRecordingStatus()
    {
        var display = BuildHeldDisplay();
        _statusLabel.Text = string.IsNullOrEmpty(display)
            ? Loc.Get("dialog.shortcuts.recordPrompt")
            : display;
    }

    private string BuildHeldDisplay()
    {
        var parts = new List<string>();
        if ((_heldModifiers & Keys.Control) != 0) parts.Add("Ctrl");
        if ((_heldModifiers & Keys.Shift) != 0) parts.Add("Shift");
        if ((_heldModifiers & Keys.Alt) != 0) parts.Add("Alt");
        if (_heldKey != Keys.None) parts.Add(ShortcutTextFormatter.Format(_heldKey));
        return string.Join("+", parts);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;
}
