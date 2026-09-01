using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

/// <summary>
/// 原生查找/替换窗口（Word 风格，与 macOS 原生 NSPanel 一致），
/// 通过命令驱动前端查找逻辑，结果经 findResult 回显。
/// 复用同一实例：关闭时隐藏而非销毁，保留上次查询词。
/// </summary>
internal sealed class FindReplaceDialog : Form
{
    private readonly Action<string, string?> _sendCommand;
    private readonly TableLayoutPanel _outer;
    private readonly TableLayoutPanel _replaceRow;

    private readonly TextBox _findBox = new();
    private readonly TextBox _replaceBox = new();
    private readonly CheckBox _caseCheck = new() { AutoSize = true };
    private readonly CheckBox _wholeCheck = new() { AutoSize = true };
    private readonly Label _resultLabel = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;
    private readonly Button _closeButton;

    private readonly int _findRowHeight;
    private readonly int _replaceRowHeight;
    private readonly int _optionsRowHeight;
    private readonly int _replaceExtraHeight;
    private readonly int _padding;

    public FindReplaceDialog(Action<string, string?> sendCommand)
    {
        _sendCommand = sendCommand;
        _findRowHeight = this.ScaleForDpi(30);
        _replaceRowHeight = this.ScaleForDpi(30);
        _optionsRowHeight = this.ScaleForDpi(28);
        _replaceExtraHeight = this.ScaleForDpi(2);
        _padding = this.ScaleForDpi(12);

        Text = Loc.Get("findBar.title");
        BackColor = SystemColors.ControlLightLight;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _prevButton = CreateButton();
        _nextButton = CreateButton();
        _replaceButton = CreateButton();
        _replaceAllButton = CreateButton();
        _closeButton = CreateButton();

        _prevButton.Text = Loc.Get("findBar.previous");
        _nextButton.Text = Loc.Get("findBar.next");
        _replaceButton.Text = Loc.Get("findBar.replace");
        _replaceAllButton.Text = Loc.Get("findBar.replaceAll");
        _closeButton.Text = Loc.Get("findBar.close");

        var findRow = CreateFieldRow(
            Loc.Get("findBar.findLabel"),
            _findBox,
            _prevButton,
            _nextButton,
            _closeButton);
        _replaceRow = CreateFieldRow(
            Loc.Get("findBar.replaceWith"),
            _replaceBox,
            _replaceButton,
            _replaceAllButton);

        _caseCheck.Text = Loc.Get("findBar.caseSensitive");
        _wholeCheck.Text = Loc.Get("findBar.wholeWord");
        _resultLabel.Text = Loc.Get("findBar.noResults");

        var optionsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 4,
            RowCount = 1,
        };
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        optionsRow.Controls.Add(_caseCheck, 0, 0);
        optionsRow.Controls.Add(_wholeCheck, 1, 0);
        optionsRow.Controls.Add(new Label { Dock = DockStyle.Fill, Margin = Padding.Empty }, 2, 0);
        optionsRow.Controls.Add(_resultLabel, 3, 0);
        _resultLabel.Margin = new Padding(0, this.ScaleForDpi(3), 0, 0);

        _outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(_padding),
        };
        _outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outer.RowStyles.Add(new RowStyle(SizeType.Absolute, _findRowHeight));
        _outer.RowStyles.Add(new RowStyle(SizeType.Absolute, _replaceRowHeight));
        _outer.RowStyles.Add(new RowStyle(SizeType.Absolute, _optionsRowHeight));
        _outer.Controls.Add(findRow, 0, 0);
        _outer.Controls.Add(_replaceRow, 0, 1);
        _outer.Controls.Add(optionsRow, 0, 2);
        Controls.Add(_outer);

        _findBox.TextChanged += (_, _) => RunFind(backwards: false);
        _caseCheck.CheckedChanged += (_, _) => RunFind(backwards: false);
        _wholeCheck.CheckedChanged += (_, _) => RunFind(backwards: false);
        _prevButton.Click += (_, _) => RunFind(backwards: true);
        _nextButton.Click += (_, _) => RunFind(backwards: false);
        _replaceButton.Click += (_, _) => ReplaceOne();
        _replaceAllButton.Click += (_, _) => ReplaceAll();
        _closeButton.Click += (_, _) => CloseFind();
        _findBox.KeyDown += OnFindBoxKeyDown;
        _replaceBox.KeyDown += OnReplaceBoxKeyDown;
        CancelButton = _closeButton;
        FormClosing += OnFormClosing;

        SetReplaceMode(replace: false);
    }

    private TableLayoutPanel CreateFieldRow(
        string labelText,
        TextBox box,
        params Button[] buttons)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2 + buttons.Length,
            RowCount = 1,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < buttons.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, this.ScaleForDpi(5), this.ScaleForDpi(8), 0),
        };
        box.Margin = new Padding(0, this.ScaleForDpi(3), this.ScaleForDpi(8), this.ScaleForDpi(3));
        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Margin = new Padding(0, this.ScaleForDpi(3), this.ScaleForDpi(4), this.ScaleForDpi(3));
            buttons[i].Anchor = AnchorStyles.Left;
        }

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(box, 1, 0);
        for (var i = 0; i < buttons.Length; i++)
        {
            row.Controls.Add(buttons[i], 2 + i, 0);
        }
        return row;
    }

    private static Button CreateButton() => new()
    {
        AutoSize = true,
        FlatStyle = FlatStyle.System,
        UseVisualStyleBackColor = true,
    };

    public void Open(Form owner, bool replace, string? query = null)
    {
        if (Owner != owner)
        {
            Owner = owner;
        }

        SetReplaceMode(replace);
        if (query is not null)
        {
            _findBox.Text = query;
        }
        var x = owner.Left + Math.Max(this.ScaleForDpi(8), (owner.Width - Width) / 2);
        var y = owner.Top + Math.Max(this.ScaleForDpi(24), this.ScaleForDpi(40));
        Location = new Point(x, y);

        if (!Visible)
        {
            Show(owner);
        }
        else
        {
            BringToFront();
            Activate();
        }

        _findBox.Focus();
        _findBox.SelectAll();
        if (!string.IsNullOrEmpty(_findBox.Text))
        {
            RunFind(backwards: false);
        }
    }

    public void ApplyResult(EditorFindResult result)
    {
        _resultLabel.Text = result.Replaced is int count
            ? Loc.Format("findBar.replaced", count)
            : $"{result.Current}/{result.Total}";
    }

    private void SetReplaceMode(bool replace)
    {
        _replaceRow.Visible = replace;
        _outer.RowStyles[1].Height = replace ? _replaceRowHeight : 0;
        UpdateSize(replace);
    }

    /// <summary>
    /// 手动高度切换：替换栏模式比查找模式在原有基础上再增加 20 像素，避免替换栏内容被裁切。
    /// </summary>
    private void UpdateSize(bool replace)
    {
        var baseHeight = _padding * 2 + _findRowHeight + _optionsRowHeight;
        var replaceExtra = replace ? _replaceRowHeight + _replaceExtraHeight : 0;
        ClientSize = new Size(
            this.ScaleForDpi(430),
            baseHeight + replaceExtra);
    }

    private string CaseValue => _caseCheck.Checked ? "1" : "0";

    private string WholeValue => _wholeCheck.Checked ? "1" : "0";

    private void RunFind(bool backwards)
    {
        _sendCommand(backwards ? "findPrev" : "findNext", $"{_findBox.Text}\t{CaseValue}\t{WholeValue}");
    }

    private void ReplaceOne()
    {
        _sendCommand("replaceOne", $"{_findBox.Text}\t{_replaceBox.Text}\t{CaseValue}\t{WholeValue}");
    }

    private void ReplaceAll()
    {
        _sendCommand("replaceAll", $"{_findBox.Text}\t{_replaceBox.Text}\t{CaseValue}\t{WholeValue}");
    }

    private void CloseFind()
    {
        _sendCommand("findClose", null);
        Hide();
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            RunFind(backwards: false);
        }
    }

    private void OnReplaceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            ReplaceOne();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseFind();
        }
    }
}
