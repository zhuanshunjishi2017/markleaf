using System.ComponentModel;

namespace MarkLeaf.UI.Controls;

internal sealed class PreferencesTabBar : Control
{
    private Color _bgHover = SystemColors.ControlLight;
    private Color _bgSelected = SystemColors.ControlLight;
    private Color _bgSelectedHover = SystemColors.Control;
    private Color _bgPrimary = SystemColors.ControlLightLight;
    private Color _textPrimary = SystemColors.ControlText;
    private Color _textSecondary = SystemColors.GrayText;
    private Color _textSelected = SystemColors.Highlight;
    private Color _textSelectedHover = SystemColors.HotTrack;

    private readonly string[] _tabs;
    private readonly string[] _icons;

    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private Font _iconFont = new("Segoe Fluent Icons", 16F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _textFont = new("Microsoft YaHei", 8F, FontStyle.Regular, GraphicsUnit.Point);
    private Rectangle[] _tabBounds;

    public PreferencesTabBar()
        : this(["文件", "外观", "编辑", "图片", "通用"], ["", "", "", "", ""]) { }

    public PreferencesTabBar(string[] tabs, string[] icons)
    {
        _tabs = tabs;
        _icons = icons;
        _tabBounds = new Rectangle[_tabs.Length];

        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Dock = DockStyle.Top;
        Height = this.ScaleForDpi(68);
        TabStop = true;
        BackColor = _bgHover;
        ForeColor = _textPrimary;
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("bg-selected", out c)) _bgSelected = c;
        if (colors.TryGetValue("bg-selected-hover", out c)) _bgSelectedHover = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("text-secondary", out c)) _textSecondary = c;
        if (colors.TryGetValue("theme-light", out c)) _textSelected = c;
        if (colors.TryGetValue("theme-dark", out c)) _textSelectedHover = c;
        BackColor = _bgHover;
        ForeColor = _textPrimary;
        Invalidate();
    }

    public event EventHandler<int>? TabChanged;

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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var hPad = this.ScaleForDpi(10);
        var vPad = this.ScaleForDpi(5);
        var gap = this.ScaleForDpi(4);
        var radius = this.ScaleForDpi(8);
        var iconTextGap = this.ScaleForDpi(3);

        // 测量所有图标和文字，确定最大宽度
        var maxIconWidth = 0;
        var iconHeight = 0;
        foreach (var icon in _icons)
        {
            var size = TextRenderer.MeasureText(e.Graphics, icon, _iconFont,
                Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (size.Width > maxIconWidth) maxIconWidth = size.Width;
            if (size.Height > iconHeight) iconHeight = size.Height;
        }

        var maxTextWidth = 0;
        var textHeight = 0;
        foreach (var tab in _tabs)
        {
            var size = TextRenderer.MeasureText(e.Graphics, tab, _textFont,
                Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (size.Width > maxTextWidth) maxTextWidth = size.Width;
            if (size.Height > textHeight) textHeight = size.Height;
        }

        var contentWidth = Math.Max(maxIconWidth, maxTextWidth);
        var side = contentWidth + hPad * 2;
        var contentHeight = iconHeight + iconTextGap + textHeight;
        // 确保按钮高度 ≥ 内容高度 + 上下内边距
        var totalButtonHeight = contentHeight + vPad * 2;

        var totalWidth = side * _tabs.Length + gap * (_tabs.Length - 1);
        var startX = Math.Max(0, (ClientSize.Width - totalWidth) / 2);
        var startY = Math.Max(0, (ClientSize.Height - totalButtonHeight) / 2);

        for (var i = 0; i < _tabs.Length; i++)
        {
            var slotWidth = side + (i < _tabs.Length - 1 ? gap : 0);
            _tabBounds[i] = new Rectangle(startX, 0, slotWidth, ClientSize.Height);

            var bgBounds = new Rectangle(startX, startY, side, totalButtonHeight);
            startX += slotWidth;

            var isSelected = i == _selectedIndex;
            var isHovered = i == _hoveredIndex;

            // 仅选中时绘制背景，悬停不改变背景色
            if (isSelected)
            {
                using var brush = new SolidBrush(_bgSelected);
                SidebarGdi.FillRoundedRect(e.Graphics, bgBounds, radius, brush);
            }

            // 文字颜色：四态
            var textColor = (isSelected, isHovered) switch
            {
                (true, true) => _textSelectedHover,
                (true, false) => _textSelected,
                (false, true) => _textPrimary,
                (false, false) => _textSecondary,
            };

            // 图标区域：按钮上半部分，水平居中
            var iconRect = new Rectangle(
                bgBounds.Left,
                bgBounds.Top + vPad,
                bgBounds.Width,
                iconHeight);
            TextRenderer.DrawText(
                e.Graphics, _icons[i], _iconFont, iconRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            // 文字区域：紧接图标下方
            var textRect = new Rectangle(
                bgBounds.Left,
                iconRect.Bottom + iconTextGap,
                bgBounds.Width,
                textHeight);
            TextRenderer.DrawText(
                e.Graphics, _tabs[i], _textFont, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        // 底部细线
        using var linePen = new Pen(_bgSelectedHover, 1);
        e.Graphics.DrawLine(linePen, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
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
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredIndex != -1)
        {
            _hoveredIndex = -1;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var index = HitTest(e.X);
        if (index >= 0 && index != _selectedIndex)
        {
            Focus();
            SelectedIndex = index;
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
            _iconFont.Dispose();
            _textFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
