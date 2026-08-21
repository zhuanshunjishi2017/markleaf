using System.ComponentModel;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class SidebarTabBar : Control
{
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _bgSelectedHover = Color.FromArgb(0xD0, 0xD0, 0xD0);
    private Color _themeLight = Color.FromArgb(0x66, 0x99, 0xFF);
    private Color _textPrimary = Color.Black;
    private Color _textSelected = Color.White;

    private string[] _tabs = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _openFolderHovered;
    private Font _font = new("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _selectedFont = new("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _iconFont = new("Segoe Fluent Icons", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Rectangle[] _tabBounds = new Rectangle[2];
    private Rectangle _openFolderBounds;

    private const string OpenFolderButtonIcon = "";

    public SidebarTabBar()
    {
        _tabs = [Loc.Get("sidebar.workspace"), Loc.Get("sidebar.outline")];
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Dock = DockStyle.Top;
        Height = this.ScaleForDpi(39);
        TabStop = true;
        BackColor = Color.White;
        ForeColor = _textPrimary;
    }

    public event EventHandler<int>? TabChanged;
    public event EventHandler<int>? TabReclicked;
    public event EventHandler? OpenFolderClicked;

    private bool OpenFolderButtonVisible => _selectedIndex == 0;

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

    public void ReloadTexts()
    {
        _tabs = [Loc.Get("sidebar.workspace"), Loc.Get("sidebar.outline")];
        Invalidate();
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) BackColor = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("bg-selected-hover", out c)) _bgSelectedHover = c;
        if (colors.TryGetValue("theme-light", out c)) _themeLight = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("text-selected", out c)) _textSelected = c;
        ForeColor = _textPrimary;
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        _font.Dispose();
        _selectedFont.Dispose();
        _iconFont.Dispose();
        _font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
        _selectedFont = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
        _iconFont = new Font("Segoe Fluent Icons", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Height = this.ScaleForDpi(39);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var hPad = this.ScaleForDpi(8);
        var topPad = this.ScaleForDpi(8);
        var bottomPad = this.ScaleForDpi(4);
        var gap = this.ScaleForDpi(2);
        var radius = this.ScaleForDpi(6);
        var iconSide = ClientSize.Height - topPad - bottomPad;
        var rightMargin = this.ScaleForDpi(8);
        var folderHPad = this.ScaleForDpi(8);
        var folderWidth = iconSide + folderHPad * 2;
        var folderX = ClientSize.Width - folderWidth - rightMargin;
        var folderRightLimit = OpenFolderButtonVisible ? folderX : ClientSize.Width;
        _openFolderBounds = new Rectangle(folderX, 0, folderWidth + rightMargin, ClientSize.Height);
        var folderIconBounds = new Rectangle(folderX, topPad, folderWidth, iconSide);

        DrawTabs(e.Graphics, hPad, topPad, bottomPad, gap, radius, folderRightLimit);

        if (OpenFolderButtonVisible)
        {
            using (var brush = new SolidBrush(_openFolderHovered ? _bgSelectedHover : _bgHover))
                SidebarGdi.FillRoundedRect(e.Graphics, folderIconBounds, radius, brush);

            TextRenderer.DrawText(
                e.Graphics,
                OpenFolderButtonIcon,
                _iconFont,
                folderIconBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    private void DrawTabs(Graphics g, int hPad, int topPad, int bottomPad, int gap, int radius, int rightLimit)
    {
        var left = this.ScaleForDpi(8);
        var availableWidth = Math.Max(0, rightLimit - left - this.ScaleForDpi(8));
        var tabWidths = new int[_tabs.Length];
        var totalTabsWidth = gap * Math.Max(0, _tabs.Length - 1);

        for (var i = 0; i < _tabs.Length; i++)
        {
            var size = TextRenderer.MeasureText(g, _tabs[i], _selectedFont,
                Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            tabWidths[i] = size.Width + hPad * 2;
            totalTabsWidth += tabWidths[i];
        }

        if (totalTabsWidth > availableWidth && _tabs.Length > 0)
        {
            var scaledWidth = Math.Max(this.ScaleForDpi(44), (availableWidth - gap * (_tabs.Length - 1)) / _tabs.Length);
            for (var i = 0; i < _tabs.Length; i++) tabWidths[i] = scaledWidth;
            totalTabsWidth = scaledWidth * _tabs.Length + gap * (_tabs.Length - 1);
        }

        var groupBounds = new Rectangle(
            left,
            topPad,
            totalTabsWidth,
            ClientSize.Height - topPad - bottomPad);
        using (var groupBrush = new SolidBrush(_bgHover))
            SidebarGdi.FillRoundedRect(g, groupBounds, radius, groupBrush);

        var x = left;
        for (var i = 0; i < _tabs.Length; i++)
        {
            var tabWidth = tabWidths[i];
            var tabBounds = new Rectangle(x, topPad, tabWidth, groupBounds.Height);
            _tabBounds[i] = new Rectangle(x, 0, tabWidth, ClientSize.Height);
            var isSelected = i == _selectedIndex;

            if (isSelected)
            {
                using var brush = new SolidBrush(_themeLight);
                SidebarGdi.FillRoundedRect(g, tabBounds, radius, brush);
            }

            TextRenderer.DrawText(
                g,
                _tabs[i],
                isSelected ? _selectedFont : _font,
                tabBounds,
                isSelected ? _textSelected : ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            x += tabWidth + gap;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = HitTest(e.Location);
        if (index != _hoveredIndex)
        {
            _hoveredIndex = index;
            Invalidate();
        }

        var folderHov = OpenFolderButtonVisible && _openFolderBounds.Contains(e.Location);
        if (folderHov != _openFolderHovered)
        {
            _openFolderHovered = folderHov;
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
        if (_openFolderHovered)
        {
            _openFolderHovered = false;
            changed = true;
        }
        if (changed) Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (OpenFolderButtonVisible && _openFolderBounds.Contains(e.Location))
        {
            Focus();
            OpenFolderClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        var index = HitTest(e.Location);
        if (index >= 0)
        {
            Focus();
            if (index == _selectedIndex)
                TabReclicked?.Invoke(this, index);
            else
                SelectedIndex = index;
        }
    }

    private int HitTest(Point point)
    {
        for (var i = 0; i < _tabBounds.Length; i++)
        {
            if (_tabBounds[i].Contains(point))
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
            _selectedFont.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
