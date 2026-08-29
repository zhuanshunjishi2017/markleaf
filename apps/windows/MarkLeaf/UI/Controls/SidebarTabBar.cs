using System.ComponentModel;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal enum SidebarTabBarMode
{
    Combined,
    WorkspaceOnly,
    OutlineOnly,
}

internal sealed class SidebarTabBar : Control
{
    private const int SelectionAnimationDurationMs = 180;

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
    private Font _iconFont = new(SystemIconProvider.IconFontName, 10F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Rectangle[] _tabBounds = new Rectangle[2];
    private readonly Rectangle[] _tabVisualBounds = new Rectangle[2];
    private readonly System.Windows.Forms.Timer _selectionAnimationTimer = new() { Interval = 15 };
    private RectangleF _selectionBounds;
    private RectangleF _selectionAnimationStartBounds;
    private RectangleF _selectionAnimationTargetBounds;
    private long _selectionAnimationStartedAt;
    private bool _selectionBoundsInitialized;
    private bool _selectionAnimationActive;
    private Rectangle _openFolderBounds;
    private SidebarTabBarMode _mode;

    private static string NewMarkdownButtonIcon => SystemIconProvider.NewFileIcon;
    private static string MergeButtonIcon => SystemIconProvider.MergeIcon;

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
        _selectionAnimationTimer.Tick += SelectionAnimationTimer_Tick;
    }

    public event EventHandler<int>? TabChanged;
    public event EventHandler<int>? TabReclicked;
    public event EventHandler? NewMarkdownClicked;
    public event EventHandler? DetachClicked;
    public event EventHandler? MergeClicked;

    private bool NewMarkdownButtonVisible => _mode != SidebarTabBarMode.OutlineOnly && _selectedIndex == 0;

    private bool MergeButtonVisible => _mode == SidebarTabBarMode.OutlineOnly;

    private bool DetachButtonVisible => _mode == SidebarTabBarMode.Combined && _selectedIndex == 1;

    private bool ActionButtonVisible => NewMarkdownButtonVisible || DetachButtonVisible || MergeButtonVisible;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SidebarTabBarMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            ConfigureTabs();
            ResetSelectionAnimation();
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex != value && value >= 0 && value < _tabs.Length)
            {
                SetSelectedIndex(value, raiseChangedEvent: true);
            }
        }
    }

    public void SetSelectedIndexSilently(int index)
    {
        if (_selectedIndex != index && index >= 0 && index < _tabs.Length)
            SetSelectedIndex(index, raiseChangedEvent: false);
    }

    public void ReloadTexts()
    {
        ConfigureTabs();
        ResetSelectionAnimation();
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
        _iconFont = new Font(SystemIconProvider.IconFontName, 10F, FontStyle.Regular, GraphicsUnit.Point);
        Height = this.ScaleForDpi(39);
        ResetSelectionAnimation();
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
        var gap = 0;
        var radius = this.ScaleForDpi(6);
        var iconSide = ClientSize.Height - topPad - bottomPad;
        var rightMargin = this.ScaleForDpi(8);
        var folderHPad = this.ScaleForDpi(8);
        var folderWidth = iconSide + folderHPad * 2;
        var folderX = ClientSize.Width - folderWidth - rightMargin;
        var folderRightLimit = ActionButtonVisible ? folderX : ClientSize.Width;
        _openFolderBounds = new Rectangle(folderX, 0, folderWidth + rightMargin, ClientSize.Height);
        var folderIconBounds = new Rectangle(folderX, topPad, folderWidth, iconSide);

        DrawTabs(e.Graphics, hPad, topPad, bottomPad, gap, radius, folderRightLimit);

        if (ActionButtonVisible)
        {
            using (var brush = new SolidBrush(_openFolderHovered ? _bgSelectedHover : _bgHover))
                SidebarGdi.FillRoundedRect(e.Graphics, folderIconBounds, radius, brush);

            TextRenderer.DrawText(
                e.Graphics,
                MergeButtonVisible
                    ? MergeButtonIcon
                    : DetachButtonVisible ? SystemIconProvider.DetachIcon : NewMarkdownButtonIcon,
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
        using (var groupBrush = new SolidBrush(
            _mode == SidebarTabBarMode.OutlineOnly ? BackColor : _bgHover))
            SidebarGdi.FillRoundedRect(g, groupBounds, radius, groupBrush);

        Array.Clear(_tabBounds);
        Array.Clear(_tabVisualBounds);
        var x = left;
        for (var i = 0; i < _tabs.Length; i++)
        {
            var tabWidth = tabWidths[i];
            var tabBounds = new Rectangle(x, topPad, tabWidth, groupBounds.Height);
            _tabBounds[i] = new Rectangle(x, 0, tabWidth, ClientSize.Height);
            _tabVisualBounds[i] = tabBounds;
            x += tabWidth + gap;
        }

        DrawSelectionSlider(g, radius);

        for (var i = 0; i < _tabs.Length; i++)
        {
            var isSelected = _mode != SidebarTabBarMode.OutlineOnly && i == _selectedIndex;
            var textColor = isSelected ? _textSelected : _textPrimary;

            TextRenderer.DrawText(
                g,
                _tabs[i],
                isSelected ? _selectedFont : _font,
                _tabVisualBounds[i],
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawSelectionSlider(Graphics graphics, int radius)
    {
        if (_mode == SidebarTabBarMode.OutlineOnly || _tabs.Length == 0)
            return;

        var targetBounds = (RectangleF)_tabVisualBounds[_selectedIndex];
        if (!_selectionBoundsInitialized || !_selectionAnimationActive)
        {
            _selectionBounds = targetBounds;
            _selectionBoundsInitialized = true;
        }
        else
        {
            _selectionAnimationTargetBounds = targetBounds;
        }

        using var brush = new SolidBrush(_themeLight);
        SidebarGdi.FillRoundedRect(graphics, Rectangle.Round(_selectionBounds), radius, brush);
    }

    private void SetSelectedIndex(int index, bool raiseChangedEvent)
    {
        var animate = _mode == SidebarTabBarMode.Combined
            && _selectionBoundsInitialized
            && _tabVisualBounds[index].Width > 0;

        _selectedIndex = index;
        if (animate)
            StartSelectionAnimation(_tabVisualBounds[index]);
        else
            ResetSelectionAnimation();

        Invalidate();
        if (raiseChangedEvent)
            TabChanged?.Invoke(this, index);
    }

    private void StartSelectionAnimation(Rectangle targetBounds)
    {
        _selectionAnimationStartBounds = _selectionBounds;
        _selectionAnimationTargetBounds = targetBounds;
        _selectionAnimationStartedAt = Environment.TickCount64;
        _selectionAnimationActive = true;
        _selectionAnimationTimer.Start();
    }

    private void SelectionAnimationTimer_Tick(object? sender, EventArgs e)
    {
        var progress = Math.Clamp(
            (Environment.TickCount64 - _selectionAnimationStartedAt) / (double)SelectionAnimationDurationMs,
            0.0,
            1.0);
        var eased = progress < 0.5
            ? 4.0 * progress * progress * progress
            : 1.0 - Math.Pow(-2.0 * progress + 2.0, 3.0) / 2.0;

        _selectionBounds = InterpolateBounds(
            _selectionAnimationStartBounds,
            _selectionAnimationTargetBounds,
            (float)eased);

        if (progress >= 1.0)
        {
            _selectionBounds = _selectionAnimationTargetBounds;
            _selectionAnimationActive = false;
            _selectionAnimationTimer.Stop();
        }

        Invalidate();
    }

    private static RectangleF InterpolateBounds(RectangleF start, RectangleF end, float progress)
    {
        return new RectangleF(
            start.X + (end.X - start.X) * progress,
            start.Y + (end.Y - start.Y) * progress,
            start.Width + (end.Width - start.Width) * progress,
            start.Height + (end.Height - start.Height) * progress);
    }

    private void ResetSelectionAnimation()
    {
        _selectionAnimationTimer.Stop();
        _selectionAnimationActive = false;
        _selectionBoundsInitialized = false;
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

        var folderHov = ActionButtonVisible && _openFolderBounds.Contains(e.Location);
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

        if (ActionButtonVisible && _openFolderBounds.Contains(e.Location))
        {
            Focus();
            if (MergeButtonVisible)
                MergeClicked?.Invoke(this, EventArgs.Empty);
            else if (DetachButtonVisible)
                DetachClicked?.Invoke(this, EventArgs.Empty);
            else
                NewMarkdownClicked?.Invoke(this, EventArgs.Empty);
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

    private void ConfigureTabs()
    {
        _tabs = _mode switch
        {
            SidebarTabBarMode.WorkspaceOnly => [Loc.Get("sidebar.workspace")],
            SidebarTabBarMode.OutlineOnly => [Loc.Get("sidebar.outline")],
            _ => [Loc.Get("sidebar.workspace"), Loc.Get("sidebar.outline")],
        };
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _tabs.Length - 1);
        _hoveredIndex = -1;
        _openFolderHovered = false;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        ResetSelectionAnimation();
        base.OnSizeChanged(e);
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
            _selectionAnimationTimer.Dispose();
            _font.Dispose();
            _selectedFont.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
