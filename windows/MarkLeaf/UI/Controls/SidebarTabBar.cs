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
    private Color _textTertiary = Color.FromArgb(0x6D, 0x6D, 0x6D);
    private Color _themeDark = Color.FromArgb(0xE0, 0xE0, 0xE0);

    private string[] _tabs = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _collapseHovered;
    private bool _leftButtonHovered;
    private bool _cancelHovered;
    private Font _font = new("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _boldFont = new("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _iconFont = new("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly Rectangle[] _tabBounds = new Rectangle[2];
    private Rectangle _collapseBounds;
    private Rectangle _leftButtonBounds;
    private Rectangle _cancelButtonBounds;

    private readonly TextBox _searchTextBox;
    private bool _searchMode;
    private bool _searchFocused;
    private string _workspaceName = string.Empty;

    private const string CollapseIcon = "";
    private const string LeftButtonIcon = "";

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string WorkspaceName
    {
        get => _workspaceName;
        set
        {
            _workspaceName = value;
            UpdateSearchPlaceholder();
        }
    }

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

        _searchTextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Visible = false,
            Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };
        _searchTextBox.Enter += (_, _) => { _searchFocused = true; Invalidate(); };
        _searchTextBox.Leave += (_, _) => { _searchFocused = false; Invalidate(); };
        _searchTextBox.TextChanged += (_, _) => SearchTextChanged?.Invoke(this, _searchTextBox.Text);
        Controls.Add(_searchTextBox);
        UpdateSearchPlaceholder();
    }

    private bool IsSearchButtonEnabled =>
        _selectedIndex != 0 || !string.IsNullOrWhiteSpace(_workspaceName);

    public void ExitSearchMode()
    {
        if (!_searchMode) return;
        _searchMode = false;
        _searchTextBox.Visible = false;
        _searchTextBox.Text = string.Empty;
        _cancelHovered = false;
        _searchFocused = false;
        SearchModeChanged?.Invoke(this, false);
        Invalidate();
    }

    private void EnterSearchMode()
    {
        if (_searchMode) return;
        _searchMode = true;
        _searchTextBox.Visible = true;
        SearchModeChanged?.Invoke(this, true);
        Invalidate();
    }

    private void UpdateSearchPlaceholder()
    {
        if (_selectedIndex == 1)
        {
            _searchTextBox.PlaceholderText = Loc.Get("sidebar.searchOutline");
            return;
        }

        var folderName = string.IsNullOrWhiteSpace(_workspaceName)
            ? Loc.Get("sidebar.workspace")
            : _workspaceName;
        _searchTextBox.PlaceholderText = Loc.Format("sidebar.searchPlaceholder", folderName);
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
        if (colors.TryGetValue("text-tertiary", out c)) _textTertiary = c;
        if (colors.TryGetValue("theme-dark", out c)) _themeDark = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        Invalidate();
    }

    public event EventHandler<int>? TabChanged;
    public event EventHandler<int>? TabReclicked;
    public event EventHandler? CollapseClicked;
    public event EventHandler<string>? SearchTextChanged;
    public event EventHandler<bool>? SearchModeChanged;

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
                UpdateSearchPlaceholder();
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
            UpdateSearchPlaceholder();
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

        // 右侧折叠按钮区域
        var iconSide = ClientSize.Height - vPad * 2;
        var iconRightMargin = this.ScaleForDpi(6);
        var iconTopOffset = this.ScaleForDpi(1);
        var collapseX = ClientSize.Width - iconSide - iconRightMargin;
        _collapseBounds = new Rectangle(collapseX, 0, iconSide + iconRightMargin, ClientSize.Height);
        var iconBounds = new Rectangle(collapseX, vPad + iconTopOffset, iconSide, iconSide);

        // 左右两侧按钮预留宽度
        var iconReserved = iconSide + iconRightMargin;

        if (_searchMode)
        {
            DrawSearchMode(e.Graphics, hPad, vPad, radius);
        }
        else
        {
            // 左侧按钮
            _leftButtonBounds = new Rectangle(0, 0, iconReserved, ClientSize.Height);
            var leftIconBounds = new Rectangle(iconRightMargin, vPad + iconTopOffset, iconSide, iconSide);
            var leftButtonEnabled = IsSearchButtonEnabled;
            if (_leftButtonHovered && leftButtonEnabled)
            {
                using var brush = new SolidBrush(_bgHover);
                SidebarGdi.FillRoundedRect(e.Graphics, leftIconBounds, radius, brush);
            }
            TextRenderer.DrawText(
                e.Graphics, LeftButtonIcon, _iconFont, leftIconBounds,
                leftButtonEnabled ? ForeColor : _textTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

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

            DrawTabs(e.Graphics, hPad, vPad, gap, radius, iconReserved);
        }
    }

    private void DrawTabs(Graphics g, int hPad, int vPad, int gap, int radius, int iconReserved)
    {
        var tabAreaWidth = ClientSize.Width - iconReserved * 2;
        var maxTabWidth = (tabAreaWidth - gap * (_tabs.Length - 1)) / _tabs.Length;

        var fontSize = 10F;
        using var measureFont = new Font("Microsoft YaHei", fontSize, FontStyle.Bold, GraphicsUnit.Point);

        var maxTextWidth = 0;
        foreach (var tab in _tabs)
        {
            var size = TextRenderer.MeasureText(g, tab, measureFont,
                Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (size.Width > maxTextWidth) maxTextWidth = size.Width;
        }

        var maxContentWidth = maxTextWidth + hPad * 2;
        var paintFont = measureFont;
        if (maxContentWidth > maxTabWidth)
        {
            fontSize = fontSize * maxTabWidth / maxContentWidth;
            paintFont = new Font("Microsoft YaHei", fontSize, FontStyle.Bold, GraphicsUnit.Point);
        }

        var tabContentWidth = Math.Min(maxTabWidth, maxTextWidth + hPad * 2);
        var totalWidth = tabContentWidth * _tabs.Length + gap * (_tabs.Length - 1);
        var startX = iconReserved + Math.Max(0, (tabAreaWidth - totalWidth) / 2);

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
                SidebarGdi.FillRoundedRect(g, bgBounds, radius, brush);
            }
            else if (isSelected)
            {
                using var brush = new SolidBrush(_bgSelected);
                SidebarGdi.FillRoundedRect(g, bgBounds, radius, brush);
            }
            else if (isHovered)
            {
                using var brush = new SolidBrush(_bgHover);
                SidebarGdi.FillRoundedRect(g, bgBounds, radius, brush);
            }

            TextRenderer.DrawText(
                g,
                _tabs[i],
                paintFont,
                bgBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        if (paintFont != measureFont) paintFont.Dispose();
    }

    private void DrawSearchMode(Graphics g, int hPad, int vPad, int radius)
    {
        // 容器背景覆盖整个栏
        using (var bgBrush = new SolidBrush(_bgHover))
            g.FillRectangle(bgBrush, 0, 0, ClientSize.Width, ClientSize.Height);

        // 取消按钮：固定宽度，文字大小自适应
        var cancelText = Loc.Get("common.cancel");
        var rowHeight = ClientSize.Height - vPad * 2;
        var cancelRightMargin = this.ScaleForDpi(6);
        var cancelWidth = this.ScaleForDpi(40);

        // 测量取消文字，必要时缩小字号
        var cancelFontSize = 10F;
        using var measureFont = new Font("Microsoft YaHei", cancelFontSize, FontStyle.Regular, GraphicsUnit.Point);
        var cancelTextWidth = TextRenderer.MeasureText(g, cancelText, measureFont,
            Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        var maxCancelTextWidth = cancelWidth;
        var cancelFont = measureFont;
        if (cancelTextWidth > maxCancelTextWidth)
        {
            cancelFontSize = cancelFontSize * maxCancelTextWidth / cancelTextWidth;
            cancelFont = new Font("Microsoft YaHei", cancelFontSize, FontStyle.Regular, GraphicsUnit.Point);
        }

        var cancelX = ClientSize.Width - cancelWidth - cancelRightMargin;
        _cancelButtonBounds = new Rectangle(cancelX, vPad - this.ScaleForDpi(1), cancelWidth, rowHeight);

        // 取消按钮背景
        var cancelBg = _cancelHovered ? _bgSelected : _bgHover;
        using (var cbBrush = new SolidBrush(cancelBg))
            SidebarGdi.FillRoundedRect(g, _cancelButtonBounds, radius, cbBrush);

        // 取消按钮文字（theme-light 色）
        using (var textBrush = new SolidBrush(_themeDark))
        {
            TextRenderer.DrawText(
                g, cancelText, cancelFont, _cancelButtonBounds, _themeDark,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        if (cancelFont != measureFont) cancelFont.Dispose();

        // 搜索框：无边框，外包圆角矩形（高度再缩减3像素）
        var searchLeftMargin = this.ScaleForDpi(10);
        var searchBgRect = new Rectangle(
            searchLeftMargin,
            vPad,
            cancelX - searchLeftMargin - this.ScaleForDpi(4),
            rowHeight - this.ScaleForDpi(3));
        using (var searchBgBrush = new SolidBrush(_bgPrimary))
            SidebarGdi.FillRoundedRect(g, searchBgRect, radius, searchBgBrush);

        // 焦点时 theme-light 描边
        if (_searchFocused)
        {
            var penWidth = (float)this.ScaleForDpi(1);
            using var focusPen = new Pen(_themeDark, penWidth);
            SidebarGdi.DrawRoundedRect(g, searchBgRect, radius, focusPen);
        }

        // 圆角矩形内部左侧搜索图标
        var searchIconSize = searchBgRect.Height - this.ScaleForDpi(4);
        var searchIconBounds = new Rectangle(
            searchBgRect.X + this.ScaleForDpi(4),
            searchBgRect.Y + this.ScaleForDpi(2),
            searchIconSize,
            searchIconSize);
        TextRenderer.DrawText(
            g, LeftButtonIcon, _iconFont, searchIconBounds, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // 输入框：位于搜索图标右侧
        var textBoxX = searchIconBounds.Right + this.ScaleForDpi(2);
        var textBoxBounds = new Rectangle(
            textBoxX,
            searchBgRect.Y + this.ScaleForDpi(3),
            searchBgRect.Right - textBoxX - this.ScaleForDpi(4),
            searchBgRect.Height - this.ScaleForDpi(4));
        if (!_searchTextBox.Visible) _searchTextBox.Visible = true;
        _searchTextBox.Bounds = textBoxBounds;
        _searchTextBox.BackColor = _bgPrimary;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_searchMode) Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_searchMode)
        {
            var cancelHov = _cancelButtonBounds.Contains(e.Location);
            if (cancelHov != _cancelHovered)
            {
                _cancelHovered = cancelHov;
                Invalidate();
            }
            return;
        }

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

        var leftHov = IsSearchButtonEnabled && _leftButtonBounds.Contains(e.X, e.Y);
        if (leftHov != _leftButtonHovered)
        {
            _leftButtonHovered = leftHov;
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
        if (_leftButtonHovered)
        {
            _leftButtonHovered = false;
            changed = true;
        }
        if (_cancelHovered)
        {
            _cancelHovered = false;
            changed = true;
        }
        if (changed) Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (_searchMode)
        {
            if (_cancelButtonBounds.Contains(e.Location))
            {
                ExitSearchMode();
            }
            return;
        }

        if (IsSearchButtonEnabled && _leftButtonBounds.Contains(e.Location))
        {
            Focus();
            EnterSearchMode();
            return;
        }

        if (_collapseBounds.Contains(e.Location))
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
                TabReclicked?.Invoke(this, index);
            else
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
            _font.Dispose();
            _boldFont.Dispose();
            _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
