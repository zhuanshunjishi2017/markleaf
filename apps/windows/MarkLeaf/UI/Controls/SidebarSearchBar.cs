using System.ComponentModel;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class SidebarSearchBar : Control, IMessageFilter
{
    private readonly TextBox _textBox;
    private Color _bgPrimary = Color.White;
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _textPrimary = Color.Black;
    private Color _themeLight = Color.FromArgb(0x66, 0x99, 0xFF);
    private bool _focused;
    private bool _outlineMode;
    private Rectangle _clearIconBounds;
    private string _workspaceName = string.Empty;
    private Font _textFont = new("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _iconFont = new(SystemIconProvider.IconFontName, 10F, FontStyle.Regular, GraphicsUnit.Point);

    private static string SearchIcon => SystemIconProvider.SearchIcon;
    private static string ClearIcon => SystemIconProvider.ClearIcon;

    public SidebarSearchBar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        Dock = DockStyle.Top;
        Height = this.ScaleForDpi(38);
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;

        _textBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = _textFont,
        };
        _textBox.TextChanged += (_, _) =>
        {
            Invalidate();
            SearchTextChanged?.Invoke(this, _textBox.Text);
        };
        _textBox.Enter += (_, _) => { _focused = true; Invalidate(); };
        _textBox.Leave += (_, _) => { _focused = false; Invalidate(); };
        Controls.Add(_textBox);
        UpdatePlaceholder();
    }

    public event EventHandler<string>? SearchTextChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SearchText
    {
        get => _textBox.Text;
        set => _textBox.Text = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string WorkspaceName
    {
        get => _workspaceName;
        set
        {
            _workspaceName = value;
            UpdatePlaceholder();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool OutlineMode
    {
        get => _outlineMode;
        set
        {
            _outlineMode = value;
            UpdatePlaceholder();
        }
    }

    public void ClearSearch()
    {
        _textBox.Text = string.Empty;
    }

    public void DismissFocusVisual()
    {
        if (!_focused)
        {
            return;
        }

        _focused = false;
        Invalidate();
    }

    public void ReloadTexts()
    {
        UpdatePlaceholder();
        Invalidate();
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("theme-light", out c)) _themeLight = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        _textBox.BackColor = _bgHover;
        _textBox.ForeColor = _textPrimary;
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        _textFont.Dispose();
        _iconFont.Dispose();
        _textFont = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _iconFont = new Font(SystemIconProvider.IconFontName, 10F, FontStyle.Regular, GraphicsUnit.Point);
        _textBox.Font = _textFont;
        Height = this.ScaleForDpi(38);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var marginX = this.ScaleForDpi(8);
        var marginTop = this.ScaleForDpi(3);
        var marginBottom = this.ScaleForDpi(6);
        var radius = this.ScaleForDpi(6);
        var fieldBounds = new Rectangle(
            marginX,
            marginTop,
            Math.Max(0, ClientSize.Width - marginX * 2),
            Math.Max(0, ClientSize.Height - marginTop - marginBottom));

        using (var brush = new SolidBrush(_bgHover))
            SidebarGdi.FillRoundedRect(e.Graphics, fieldBounds, radius, brush);

        if (Enabled && _focused)
        {
            using var pen = new Pen(_themeLight, Math.Max(1, this.ScaleForDpi(2)));
            SidebarGdi.DrawRoundedRect(e.Graphics, fieldBounds, radius, pen);
        }

        var iconSize = fieldBounds.Height - this.ScaleForDpi(6);
        var iconTop = fieldBounds.Y + this.ScaleForDpi(3);
        var iconBounds = new Rectangle(
            fieldBounds.X + this.ScaleForDpi(7),
            iconTop,
            iconSize,
            iconSize);
        TextRenderer.DrawText(
            e.Graphics,
            SearchIcon,
            _iconFont,
            iconBounds,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        var hasText = !string.IsNullOrEmpty(_textBox.Text);
        var textBoxX = iconBounds.Right + this.ScaleForDpi(4);
        if (hasText)
        {
            _clearIconBounds = new Rectangle(
                fieldBounds.Right - this.ScaleForDpi(5) - iconSize,
                iconTop + this.ScaleForDpi(1),
                iconSize,
                iconSize);
            TextRenderer.DrawText(
                e.Graphics,
                ClearIcon,
                _iconFont,
                _clearIconBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
        else
        {
            _clearIconBounds = Rectangle.Empty;
        }

        var textBoxRight = hasText
            ? _clearIconBounds.Left - this.ScaleForDpi(4)
            : fieldBounds.Right - this.ScaleForDpi(8);
        _textBox.Bounds = new Rectangle(
            textBoxX,
            fieldBounds.Y + this.ScaleForDpi(4),
            Math.Max(0, textBoxRight - textBoxX),
            Math.Max(0, fieldBounds.Height - this.ScaleForDpi(8)));
        _textBox.BackColor = _bgHover;
        _textBox.ForeColor = _textPrimary;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _clearIconBounds.Contains(e.Location))
        {
            _textBox.Text = string.Empty;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Application.AddMessageFilter(this);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Application.RemoveMessageFilter(this);
        base.OnHandleDestroyed(e);
    }

    public bool PreFilterMessage(ref Message m)
    {
        const int WmLButtonDown = 0x0201;
        const int WmRButtonDown = 0x0204;
        const int WmMButtonDown = 0x0207;
        if (m.Msg is not (WmLButtonDown or WmRButtonDown or WmMButtonDown)
            || (!_focused && !_textBox.Focused))
        {
            return false;
        }

        if (RectangleToScreen(ClientRectangle).Contains(Cursor.Position))
        {
            if (!_focused)
            {
                _focused = true;
                Invalidate();
            }
        }
        else
        {
            DismissFocusVisual();
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textFont.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void UpdatePlaceholder()
    {
        if (_outlineMode)
        {
            _textBox.PlaceholderText = Loc.Get("sidebar.searchOutline");
            return;
        }

        var folderName = string.IsNullOrWhiteSpace(_workspaceName)
            ? Loc.Get("sidebar.workspace")
            : _workspaceName;
        _textBox.PlaceholderText = Loc.Format("sidebar.searchPlaceholder", folderName);
    }
}
