using System.ComponentModel;
using System.Drawing.Drawing2D;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class SidebarTabBar : Control
{
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _bgSelected = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private Color _bgSelectedHover = Color.FromArgb(0xD0, 0xD0, 0xD0);
    private Color _bgPrimary = Color.White;
    private Color _textPrimary = Color.Black;

    private string[] _tabs = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _collapseHovered;
    private Font _font = new("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _boldFont = new("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _iconFont = new("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Rectangle[] _tabBounds = new Rectangle[2];
    private Rectangle _collapseBounds;

    private const string CollapseIcon = "";

    public SidebarTabBar()
    {
        _tabs = [Loc.Get("sidebar.workspace"), Loc.Get("sidebar.outline")];
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Dock = DockStyle.Top;
        Height = this.ScaleForDpi(43);
        TabStop = true;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
    }

    public void ReloadTexts()
    {
        _tabs = [Loc.Get("sidebar.workspace"), Loc.Get("sidebar.outline")];
        Invalidate();
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("bg-selected", out c)) _bgSelected = c;
        if (colors.TryGetValue("bg-selected-hover", out c)) _bgSelectedHover = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        Invalidate();
    }

    public event EventHandler<int>? TabChanged;
    public event EventHandler<int>? TabReclicked;
    public event EventHandler? CollapseClicked;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex != value && value >= 0 && value < _tabs.Length)
            {
                _selectedIndex = value;
                Invalidate();
                TabChanged?.Invoke(this, value);
            }
        }
    }

    public void SetSelectedIndexSilently(int index)
    {
        if (index >= 0 && index < _tabs.Length)
        {
            _selectedIndex = index;
            Invalidate();
        }
    }

    public void ConfigureTypography(int dpi)
    {
        _font.Dispose();
        _boldFont.Dispose();
        _iconFont.Dispose();
        _font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _boldFont = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point);
        _iconFont = new Font("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point);
        Height = this.ScaleForDpi(52);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var hPad = this.ScaleForDpi(8);
        var vPad = this.ScaleForDpi(8);
        var gap = this.ScaleForDpi(4);
        var radius = this.ScaleForDpi(8);

        // 测量文本宽度，所有标签统一使用最宽者的尺寸
        var maxTextWidth = 0;
        var textHeight = 0;
        foreach (var tab in _tabs)
        {
            var size = TextRenderer.MeasureText(e.Graphics, tab, _boldFont,
                Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (size.Width > maxTextWidth) maxTextWidth = size.Width;
            if (size.Height > textHeight) textHeight = size.Height;
        }

        var tabContentWidth = maxTextWidth + hPad * 2;
        var totalWidth = tabContentWidth * _tabs.Length + gap * (_tabs.Length - 1);

        // 右侧折叠按钮区域
        var iconSide = ClientSize.Height - vPad * 2;
        var iconRightMargin = this.ScaleForDpi(6);
        var iconTopOffset = this.ScaleForDpi(1);
        var collapseX = ClientSize.Width - iconSide - iconRightMargin;
        _collapseBounds = new Rectangle(collapseX, 0, iconSide + iconRightMargin, ClientSize.Height);
        var iconBounds = new Rectangle(collapseX, vPad + iconTopOffset, iconSide, iconSide);

        // 标签居中区域：左右对称留出折叠按钮的宽度
        var iconReserved = iconSide + iconRightMargin;
        var tabAreaStart = iconReserved;
        var tabAreaWidth = ClientSize.Width - iconReserved * 2;
        if (tabAreaWidth < totalWidth) { tabAreaStart = 0; tabAreaWidth = ClientSize.Width - iconReserved; }
        var startX = tabAreaStart + Math.Max(0, (tabAreaWidth - totalWidth) / 2);

        for (var i = 0; i < _tabs.Length; i++)
        {
            var tabSlotWidth = tabContentWidth + (i < _tabs.Length - 1 ? gap : 0);
            var bgBounds = new Rectangle(
                startX,
                vPad,
                tabContentWidth,
                ClientSize.Height - vPad * 2);
            _tabBounds[i] = new Rectangle(startX, 0, tabSlotWidth, ClientSize.Height);
            startX += tabSlotWidth;

            var isSelected = i == _selectedIndex;
            var isHovered = i == _hoveredIndex;

            if (isSelected && isHovered)
            {
                using var brush = new SolidBrush(_bgSelectedHover);
                SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
            }
            else if (isSelected)
            {
                using var brush = new SolidBrush(_bgSelected);
                SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
            }
            else if (isHovered)
            {
                using var brush = new SolidBrush(_bgHover);
                SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
            }

            var font = isSelected ? _boldFont : _font;
            TextRenderer.DrawText(
                e.Graphics,
                _tabs[i],
                font,
                bgBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        // 折叠按钮背景
        if (_collapseHovered)
        {
            using var brush = new SolidBrush(_bgHover);
            SidebarGdi.FillRoundedRect(e.Graphics, iconBounds, radius, brush);
        }

        // 折叠按钮图标
        TextRenderer.DrawText(
            e.Graphics, CollapseIcon, _iconFont, iconBounds, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = HitTest(e.X);
        if (index != _hoveredIndex)
        {
            _hoveredIndex = index;
            Invalidate();
        }

        var collapseHov = _collapseBounds.Contains(e.X, e.Y);
        if (collapseHov != _collapseHovered)
        {
            _collapseHovered = collapseHov;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        var changed = false;
        if (_hoveredIndex != -1)
        {
            _hoveredIndex = -1;
            changed = true;
        }

        if (_collapseHovered)
        {
            _collapseHovered = false;
            changed = true;
        }

        if (changed) Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (_collapseBounds.Contains(e.X, e.Y))
        {
            Focus();
            CollapseClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        var index = HitTest(e.X);
        if (index >= 0)
        {
            Focus();
            if (index == _selectedIndex)
            {
                TabReclicked?.Invoke(this, index);
            }
            else
            {
                SelectedIndex = index;
            }
        }
    }

    private int HitTest(int x)
    {
        for (var i = 0; i < _tabBounds.Length; i++)
        {
            if (_tabBounds[i].Contains(x, _tabBounds[i].Height / 2))
                return i;
        }
        return -1;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Left or Keys.Right or Keys.Enter or Keys.Space
            || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left:
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                break;
            case Keys.Right:
                SelectedIndex = Math.Min(_tabs.Length - 1, _selectedIndex + 1);
                break;
            case Keys.Enter or Keys.Space:
                TabChanged?.Invoke(this, _selectedIndex);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _boldFont.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }

}
