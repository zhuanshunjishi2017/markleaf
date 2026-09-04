using MarkLeaf.Documents;
using MarkLeaf.Native;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class DocumentTabBar : Control
{
    private const int AnimationDurationMs = 180;
    private Color _bgSecondary = Color.FromArgb(245, 245, 245);
    private Color _bgHover = Color.FromArgb(232, 232, 232);
    private Color _textPrimary = Color.Black;
    private Color _textSecondary = Color.FromArgb(90, 90, 90);
    private Color _textTertiary = Color.FromArgb(128, 128, 128);
    private Color _bgSelected = Color.FromArgb(220, 220, 220);
    private Font _font = new("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _selectedFont = new("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _markerFont = new("Microsoft YaHei", 8F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _iconFont = new(SystemIconProvider.IconFontName, 9F, FontStyle.Regular, GraphicsUnit.Point);
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 15 };
    private IReadOnlyList<MarkdownDocument> _documents = [];
    private IReadOnlyList<MarkdownDocument> _layoutDocuments = [];
    private Dictionary<MarkdownDocument, Rectangle> _layoutBounds = [];
    private string? _workspaceRoot;
    private int _selectedIndex = -1;
    private int _hoveredIndex = -1;
    private int _hoveredCloseIndex = -1;
    private int _mouseDownTabIndex = -1;
    private int _mouseDownCloseIndex = -1;
    private int _scrollOffset;
    private int _heldTabWidth;
    private bool _holdTabWidth;
    private bool _menuLayoutLocked;
    private bool _lockedUseExpandedMenu;
    private bool _pointerInside;
    private bool _dragging;
    private int _dragIndex = -1;
    private Point _dragStartPoint;
    private int _dragPointerOffset;
    private int _dragPointerOffsetY;
    private IReadOnlyList<MarkdownDocument> _dragDocuments = [];
    private List<Rectangle> _tabBounds = [];
    private List<Rectangle> _closeBounds = [];

    private IReadOnlyList<MarkdownDocument> _animationDocuments = [];
    private Dictionary<MarkdownDocument, Rectangle> _animationFromBounds = [];
    private Dictionary<MarkdownDocument, Rectangle> _animationToBounds = [];
    private long _animationStartedAt;
    private bool _animationActive;
    private bool _displaySuppressed;
    private bool _fullScreenMenuVisible;
    private bool _fullScreenMenuHovered;
    private bool _fullScreenMenuPressed;
    private Rectangle _fullScreenMenuBounds;
    private readonly List<Rectangle> _expandedMenuBounds = [];
    private int _hoveredExpandedMenuIndex = -1;
    private int _pressedExpandedMenuIndex = -1;
    private bool _newTabHovered;
    private bool _newTabPressed;
    private Rectangle _newTabBounds;
    private bool _showMenuKeyboardShortcuts = true;
    private bool _showMenuMnemonics = true;
    private string _uiLanguage = string.Empty;
    private bool _keyboardMenuActive;

    public void SetDisplaySuppressed(bool suppressed)
    {
        if (_displaySuppressed == suppressed) return;
        _displaySuppressed = suppressed;
        Visible = !suppressed && (_documents.Count > 0 || _animationActive || _fullScreenMenuVisible);
    }

    public DocumentTabBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = this.ScaleForDpi(35);
        Visible = false;
        TabStop = false;
        _animationTimer.Tick += OnAnimationTimerTick;
    }

    public event EventHandler<int>? TabSelected;
    public event EventHandler<int>? TabCloseRequested;
    public event EventHandler<(int Index, Point ScreenLocation)>? TabContextRequested;
    public event EventHandler<IReadOnlyList<MarkdownDocument>>? TabsReordered;
    public event EventHandler<(int Index, Point ScreenLocation)>? TabDetached;
    public event EventHandler<Point>? FullScreenMenuRequested;
    public event EventHandler<(int MenuIndex, Point ScreenLocation)>? TopLevelMenuRequested;
    public event EventHandler? NewDocumentRequested;

    public IReadOnlyList<Rectangle> GetTopLevelMenuScreenBounds()
        => _expandedMenuBounds.Select(rectangle => new Rectangle(PointToScreen(rectangle.Location), rectangle.Size)).ToArray();

    public Point GetTopLevelMenuScreenLocation(int menuIndex)
    {
        if (menuIndex < 0 || menuIndex >= _expandedMenuBounds.Count) return Point.Empty;
        var bounds = _expandedMenuBounds[menuIndex];
        return PointToScreen(new Point(bounds.Left, bounds.Bottom));
    }

    public void SetOpenTopLevelMenuIndex(int menuIndex)
    {
        _pressedExpandedMenuIndex = menuIndex;
        Invalidate();
        Update();
    }

    public void SetMenuTextOptions(
        bool showKeyboardShortcuts,
        bool showMnemonics,
        string? uiLanguage)
    {
        var language = uiLanguage ?? string.Empty;
        if (_showMenuKeyboardShortcuts == showKeyboardShortcuts
            && _showMenuMnemonics == showMnemonics
            && string.Equals(_uiLanguage, language, StringComparison.Ordinal))
        {
            return;
        }

        _showMenuKeyboardShortcuts = showKeyboardShortcuts;
        _showMenuMnemonics = showMnemonics;
        _uiLanguage = language;
        ReflowWithAnimation();
        Invalidate();
    }

    public void SelectFirstTopLevelMenu()
    {
        if (!_fullScreenMenuVisible)
        {
            return;
        }

        UpdateMenuButtonBounds();
        _keyboardMenuActive = true;
        if (UseExpandedMenu && _expandedMenuBounds.Count > 0)
        {
            _fullScreenMenuHovered = false;
            _hoveredExpandedMenuIndex = 0;
        }
        else
        {
            _hoveredExpandedMenuIndex = -1;
            _fullScreenMenuHovered = true;
        }
        Invalidate();
        Update();
    }

    public void ToggleKeyboardMenuMode()
    {
        if (!_fullScreenMenuVisible)
            return;

        if (_keyboardMenuActive)
        {
            ClearKeyboardMenuSelection();
            return;
        }

        SelectFirstTopLevelMenu();
    }

    public bool TryGetMnemonicMenuIndex(Keys keyCode, out int menuIndex)
    {
        menuIndex = -1;
        if (!_fullScreenMenuVisible || !_showMenuMnemonics)
            return false;

        var pressed = keyCode.ToString();
        if (string.IsNullOrEmpty(pressed) || pressed.Length != 1)
            return false;

        var labels = GetRawExpandedMenuLabels();
        for (var index = 0; index < labels.Length; index++)
        {
            var mnemonicIndex = labels[index].IndexOf('&');
            while (mnemonicIndex >= 0 && mnemonicIndex + 1 < labels[index].Length
                && labels[index][mnemonicIndex + 1] == '&')
            {
                mnemonicIndex = labels[index].IndexOf('&', mnemonicIndex + 2);
            }

            if (mnemonicIndex >= 0
                && mnemonicIndex + 1 < labels[index].Length
                && char.ToUpperInvariant(labels[index][mnemonicIndex + 1])
                    == char.ToUpperInvariant(pressed[0]))
            {
                menuIndex = index;
                return true;
            }
        }

        return false;
    }

    public bool ActivateTopLevelMenu(int menuIndex)
    {
        if (!_fullScreenMenuVisible || !UseExpandedMenu
            || menuIndex < 0 || menuIndex >= _expandedMenuBounds.Count)
            return false;

        _keyboardMenuActive = true;
        _fullScreenMenuHovered = false;
        _hoveredExpandedMenuIndex = menuIndex;
        Invalidate();
        Update();
        return true;
    }

    public bool HandleKeyboardMenuKey(Keys keyCode)
    {
        if (!_keyboardMenuActive || !_fullScreenMenuVisible)
        {
            return false;
        }

        UpdateMenuButtonBounds();
        if (keyCode == Keys.Escape)
        {
            ClearKeyboardMenuSelection();
            return true;
        }

        if (keyCode is Keys.Left or Keys.Right)
        {
            if (UseExpandedMenu && _expandedMenuBounds.Count > 0)
            {
                var direction = keyCode == Keys.Right ? 1 : -1;
                var current = Math.Max(0, _hoveredExpandedMenuIndex);
                _hoveredExpandedMenuIndex =
                    (current + direction + _expandedMenuBounds.Count) % _expandedMenuBounds.Count;
                _fullScreenMenuHovered = false;
                Invalidate();
                Update();
            }
            return true;
        }

        if (keyCode != Keys.Enter)
        {
            return false;
        }

        if (UseExpandedMenu && _expandedMenuBounds.Count > 0)
        {
            var index = Math.Clamp(_hoveredExpandedMenuIndex, 0, _expandedMenuBounds.Count - 1);
            var bounds = _expandedMenuBounds[index];
            TopLevelMenuRequested?.Invoke(
                this,
                (index, PointToScreen(new Point(bounds.Left, bounds.Bottom))));
        }
        else
        {
            var bounds = _fullScreenMenuBounds;
            FullScreenMenuRequested?.Invoke(
                this,
                PointToScreen(new Point(bounds.Left, bounds.Bottom)));
        }
        return true;
    }

    private void ClearKeyboardMenuSelection()
    {
        _keyboardMenuActive = false;
        _hoveredExpandedMenuIndex = -1;
        _fullScreenMenuHovered = false;
        Invalidate();
    }

    public void SetFullScreenMenuVisible(bool visible)
    {
        if (_fullScreenMenuVisible == visible) return;
        _fullScreenMenuVisible = visible;
        _fullScreenMenuHovered = false;
        _fullScreenMenuPressed = false;
        _hoveredExpandedMenuIndex = -1;
        _pressedExpandedMenuIndex = -1;
        _newTabHovered = false;
        _newTabPressed = false;
        _keyboardMenuActive = false;
        Visible = !_displaySuppressed && (_documents.Count > 0 || _animationActive || visible);
        ReflowWithAnimation();
        Invalidate();
    }

    public void SetDocuments(IReadOnlyList<MarkdownDocument> documents, int selectedIndex)
    {
        var nextDocuments = documents.ToArray();

        // Editor state notifications can arrive immediately after a tab was
        // closed. The target layout has already been installed at that point;
        // refreshing it must not restart the close animation.
        if (SameDocuments(_layoutDocuments, nextDocuments))
        {
            _documents = nextDocuments;
            _selectedIndex = selectedIndex;
            Visible = !_displaySuppressed
                && (nextDocuments.Length > 0 || _animationActive || _fullScreenMenuVisible);
            UpdateInteractionBounds(GetCurrentRenderBounds());
            Invalidate();
            return;
        }

        // A document collection change invalidates the close-button width
        // hold and all old mouse-down state. Otherwise removing a tab while
        // the pointer is over its close button can make the new layout keep
        // the old width until another mouse event happens.
        _hoveredIndex = -1;
        _hoveredCloseIndex = -1;
        _mouseDownTabIndex = -1;
        _mouseDownCloseIndex = -1;

        var oldDocuments = _layoutDocuments.ToArray();
        var oldBounds = new Dictionary<MarkdownDocument, Rectangle>(_layoutBounds);
        var tabWasAdded = nextDocuments.Length > oldDocuments.Length;

        // Adding a tab must be allowed to recalculate the available width even
        // when the pointer is still inside the tab bar. The width hold is only
        // retained for removals, so closing tabs can keep its existing behavior.
        if (tabWasAdded)
        {
            _holdTabWidth = false;
            _heldTabWidth = 0;
            _menuLayoutLocked = false;
        }
        else if (_pointerInside && !_menuLayoutLocked)
        {
            _lockedUseExpandedMenu = CalculateUseExpandedMenu();
            _menuLayoutLocked = true;
        }

        if (_animationActive)
        {
            var progress = Ease(GetAnimationProgress());
            oldDocuments = _animationDocuments.ToArray();
            oldBounds = _animationDocuments.ToDictionary(
                document => document,
                document => Interpolate(
                    _animationFromBounds[document], _animationToBounds[document], progress));
        }

        _documents = nextDocuments;
        _selectedIndex = selectedIndex;
        Visible = !_displaySuppressed
            && (nextDocuments.Length > 0 || oldDocuments.Length > 0 || _fullScreenMenuVisible);

        if (nextDocuments.Length == 0)
        {
            var closingFromBounds = new Dictionary<MarkdownDocument, Rectangle>(oldBounds);
            var closingToBounds = oldDocuments.ToDictionary(document => document,
                document => Collapse(oldBounds[document]));
            _layoutDocuments = [];
            _layoutBounds = [];
            _tabBounds = [];
            _closeBounds = [];
            _scrollOffset = 0;
            _mouseDownTabIndex = -1;
            _mouseDownCloseIndex = -1;
            _holdTabWidth = false;
            _animationDocuments = oldDocuments;
            _animationFromBounds = closingFromBounds;
            _animationToBounds = closingToBounds;
            StartAnimation();
            Invalidate();
            return;
        }

        var targetBounds = CalculateLayout(nextDocuments, _scrollOffset, out var maximumScrollOffset);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maximumScrollOffset);
        targetBounds = CalculateLayout(nextDocuments, _scrollOffset, out _);
        _layoutDocuments = nextDocuments;
        _layoutBounds = targetBounds;
        UpdateInteractionBounds(targetBounds);

        var renderDocuments = BuildRenderOrder(oldDocuments, nextDocuments);
        var fromBounds = new Dictionary<MarkdownDocument, Rectangle>();
        var toBounds = new Dictionary<MarkdownDocument, Rectangle>();
        foreach (var document in renderDocuments)
        {
            var hasOld = oldBounds.TryGetValue(document, out var from);
            var hasNew = targetBounds.TryGetValue(document, out var to);
            if (!hasOld) from = Collapse(to);
            if (!hasNew)
            {
                // Keep a removed tab at its former location and animate only
                // its width to zero. The remaining tabs animate to their new
                // locations at the same time.
                to = Collapse(from);
            }
            fromBounds[document] = from;
            toBounds[document] = to;
        }

        if (!SameLayout(renderDocuments, fromBounds, toBounds))
        {
            _animationDocuments = renderDocuments;
            _animationFromBounds = fromBounds;
            _animationToBounds = toBounds;
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
        UpdateInteractionBounds(GetCurrentRenderBounds());
        Invalidate();
    }

    public void SetWorkspaceRoot(string? workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
        Invalidate();
    }

    public bool IsExternalDocument(int index)
        => index >= 0 && index < _documents.Count && IsExternalDocument(_documents[index]);

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-secondary", out var c)) _bgSecondary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("bg-selected", out c)) _bgSelected = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("text-secondary", out c)) _textSecondary = c;
        if (colors.TryGetValue("text-tertiary", out c)) _textTertiary = c;
        BackColor = _bgSecondary;
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        _font.Dispose();
        _selectedFont.Dispose();
        _markerFont.Dispose();
        _iconFont.Dispose();
        _font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _selectedFont = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
        _markerFont = new Font("Microsoft YaHei", 8F, FontStyle.Regular, GraphicsUnit.Point);
        _iconFont = new Font(SystemIconProvider.IconFontName, 9F, FontStyle.Regular, GraphicsUnit.Point);
        Height = this.ScaleForDpi(35);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_documents.Count == 0)
        {
            UpdateMenuButtonBounds();
            Invalidate();
            return;
        }
        var bounds = CalculateLayout(_documents, _scrollOffset, out var maximumScrollOffset);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maximumScrollOffset);
        _layoutBounds = CalculateLayout(_documents, _scrollOffset, out _);
        UpdateInteractionBounds(_layoutBounds);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        UpdateMenuButtonBounds();
        UpdateInteractionBounds(GetCurrentRenderBounds());
        e.Graphics.Clear(_bgSecondary);
        using var border = new SolidBrush(_bgSelected);
        e.Graphics.FillRectangle(border, 0, ClientSize.Height - 1, ClientSize.Width, 1);
        if (_fullScreenMenuVisible && UseExpandedMenu)
        {
            var labels = GetExpandedMenuLabels();
            for (var index = 0; index < _expandedMenuBounds.Count; index++)
            {
                var bounds = _expandedMenuBounds[index];
                using var buttonBrush = new SolidBrush(index == _pressedExpandedMenuIndex
                    ? _bgSelected
                    : index == _hoveredExpandedMenuIndex ? _bgHover : _bgSecondary);
                SidebarGdi.FillRoundedRect(
                    e.Graphics,
                    bounds,
                    this.ScaleForDpi(6),
                    buttonBrush);
                TextRenderer.DrawText(
                    e.Graphics,
                    labels[index],
                    _font,
                    bounds,
                    _textPrimary,
                    TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
        else if (_fullScreenMenuVisible)
        {
            using var buttonBrush = new SolidBrush(_fullScreenMenuPressed
                ? _bgSelected
                : _fullScreenMenuHovered ? _bgHover : _bgSecondary);
            SidebarGdi.FillRoundedRect(
                e.Graphics,
                _fullScreenMenuBounds,
                this.ScaleForDpi(6),
                buttonBrush);
            TextRenderer.DrawText(
                e.Graphics,
                SystemIconProvider.FullScreenMenuIcon,
                _iconFont,
                _fullScreenMenuBounds,
                _textPrimary,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        if (_documents.Count > 0 || _animationActive)
        {
            var clipLeft = _fullScreenMenuVisible
                ? (UseExpandedMenu ? _expandedMenuBounds[^1].Right : _fullScreenMenuBounds.Right)
                    + this.ScaleForDpi(4)
                : this.ScaleForDpi(4);
            var clipRight = _newTabBounds.Left - this.ScaleForDpi(4);
            var graphicsState = e.Graphics.Save();
            e.Graphics.SetClip(new Rectangle(
                clipLeft,
                0,
                Math.Max(0, clipRight - clipLeft),
                ClientSize.Height));
            try
            {
                var progress = _animationActive ? Ease(GetAnimationProgress()) : 1F;
                var renderDocuments = _animationActive ? _animationDocuments : _layoutDocuments;
                if (_dragging)
                    renderDocuments = _dragDocuments
                        .Where(document => !IsDraggedDocument(document))
                        .ToArray();
                foreach (var document in renderDocuments)
                {
                    var bounds = _dragging
                        ? GetDragBounds(document)
                        : _animationActive
                        ? Interpolate(_animationFromBounds[document], _animationToBounds[document], progress)
                        : _layoutBounds.GetValueOrDefault(document);
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                    DrawTab(e.Graphics, document, bounds);
                }
            }
            finally
            {
                e.Graphics.Restore(graphicsState);
            }

            // Keep the dragged tab above the menu/new-tab controls as well as the
            // other tabs, so its rounded rectangle is never clipped at an edge.
            if (_dragging && _dragIndex >= 0 && _dragIndex < _dragDocuments.Count)
                DrawTab(e.Graphics, _dragDocuments[_dragIndex],
                    GetDragBounds(_dragDocuments[_dragIndex]), isDragged: true);
        }

        using (var newTabBrush = new SolidBrush(_newTabPressed
            ? _bgSelected
            : _newTabHovered ? _bgHover : _bgSecondary))
            SidebarGdi.FillRoundedRect(
                e.Graphics,
                _newTabBounds,
                this.ScaleForDpi(6),
                newTabBrush);
        TextRenderer.DrawText(
            e.Graphics,
            SystemIconProvider.NewTabIcon,
            _iconFont,
            _newTabBounds,
            _textPrimary,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void DrawTab(Graphics graphics, MarkdownDocument document, Rectangle bounds,
        bool isDragged = false)
    {
        var actualIndex = IndexOfDocument(document);
        var selected = actualIndex == _selectedIndex;
        var hovered = actualIndex == _hoveredIndex;
        using (var brush = new SolidBrush(isDragged
            ? _bgSelected : selected || hovered ? _bgHover : _bgSecondary))
            SidebarGdi.FillRoundedRect(graphics, bounds, this.ScaleForDpi(6), brush);

        var closeSize = this.ScaleForDpi(18);
        var close = new Rectangle(bounds.Right - closeSize - this.ScaleForDpi(5),
            bounds.Top + (bounds.Height - closeSize) / 2, closeSize, closeSize);
        var markerText = GetMarkerText(document);
        var markerWidth = string.IsNullOrEmpty(markerText) ? 0
            : TextRenderer.MeasureText(graphics, markerText, _markerFont, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        var markerGap = markerWidth == 0 ? 0 : this.ScaleForDpi(5);
        var markerBounds = new Rectangle(close.Left - markerGap - markerWidth, bounds.Top,
            markerWidth, bounds.Height);
        var textBounds = new Rectangle(bounds.Left + this.ScaleForDpi(8), bounds.Top,
            Math.Max(1, markerBounds.Left - bounds.Left - this.ScaleForDpi(4)), bounds.Height);
        var color = selected ? _textPrimary : _textSecondary;
        TextRenderer.DrawText(graphics, document.DisplayName,
            selected ? _selectedFont : _font, textBounds, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (markerWidth > 0)
            TextRenderer.DrawText(graphics, markerText, _markerFont, markerBounds,
                document.IsReadOnly || selected ? _textSecondary : _textTertiary,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        var closeHovered = actualIndex == _hoveredCloseIndex;
        var closeIcon = document.IsDirty && !closeHovered ? "●" : "";
        TextRenderer.DrawText(graphics, closeIcon,
            document.IsDirty && !closeHovered ? _font : _iconFont, close, color,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _pointerInside = true;
        if (_mouseDownTabIndex >= 0 && _mouseDownCloseIndex < 0
            && (Control.MouseButtons & MouseButtons.Left) != 0)
        {
            UpdateDragPosition(e.Location);
            if (_dragging) return;
        }
        // During an animation the visible rectangles are between the old and
        // new layouts. Hit testing must use that same frame, but pointer
        // movement must not alter the animation geometry.
        UpdateInteractionBounds(GetCurrentRenderBounds());
        var newTabHovered = _newTabBounds.Contains(e.Location);
        if (newTabHovered != _newTabHovered)
        {
            _newTabHovered = newTabHovered;
            Invalidate(_newTabBounds);
        }
        if (newTabHovered) return;
        var expandedMenuIndex = _expandedMenuBounds.FindIndex(r => r.Contains(e.Location));
        if (expandedMenuIndex != _hoveredExpandedMenuIndex)
        {
            _hoveredExpandedMenuIndex = expandedMenuIndex;
            Invalidate();
        }
        if (expandedMenuIndex >= 0) return;
        var menuHovered = _fullScreenMenuVisible && _fullScreenMenuBounds.Contains(e.Location);
        if (menuHovered != _fullScreenMenuHovered)
        {
            _fullScreenMenuHovered = menuHovered;
            Invalidate();
        }
        if (menuHovered) return;
        var index = _tabBounds.FindIndex(r => r.Contains(e.Location));
        var closeIndex = _closeBounds.FindIndex(r => r.Contains(e.Location));
        if (_animationActive)
        {
            // Animation geometry is self-contained. Pointer movement must
            // not lock a width or start another reflow until it finishes.
            if (index != _hoveredIndex || closeIndex != _hoveredCloseIndex)
            {
                _hoveredIndex = index;
                _hoveredCloseIndex = closeIndex;
                Invalidate();
            }
            return;
        }
        // The entire tab bar owns the width lock. Empty space inside it is
        // intentionally included; leaving the control is the only unlock.
        if (!_holdTabWidth)
        {
            var normalWidth = _layoutBounds.Values.FirstOrDefault(rectangle => rectangle.Width > 0).Width;
            if (normalWidth > 0)
            {
                _heldTabWidth = normalWidth;
                _holdTabWidth = true;
            }
        }
        if (index != _hoveredIndex || closeIndex != _hoveredCloseIndex)
        {
            _hoveredIndex = index;
            _hoveredCloseIndex = closeIndex;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging)
        {
            // Keep mouse capture until button-up so moving through the editor
            // area does not detach a tab. Detachment is decided against the
            // owner window bounds when the drag ends.
            return;
        }
        _pointerInside = false;
        _hoveredIndex = -1;
        _hoveredCloseIndex = -1;
        _fullScreenMenuHovered = false;
        _hoveredExpandedMenuIndex = -1;
        _newTabHovered = false;
        _menuLayoutLocked = false;
        if (_holdTabWidth)
        {
            _holdTabWidth = false;
            _heldTabWidth = 0;
            ReflowWithAnimation();
        }
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _pointerInside = true;
        _lockedUseExpandedMenu = CalculateUseExpandedMenu();
        _menuLayoutLocked = true;
        LockWidthForPointer();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var maximum = GetMaximumScrollOffset();
        if (maximum == 0) return;
        var next = Math.Clamp(_scrollOffset + (e.Delta > 0 ? -1 : 1) * this.ScaleForDpi(48), 0, maximum);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        _layoutBounds = CalculateLayout(_documents, _scrollOffset, out _);
        UpdateInteractionBounds(_layoutBounds);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        UpdateInteractionBounds(GetCurrentRenderBounds());
        if (e.Button == MouseButtons.Left)
        {
            if (_newTabBounds.Contains(e.Location))
            {
                _newTabPressed = true;
                Invalidate(_newTabBounds);
                return;
            }
            _pressedExpandedMenuIndex = _expandedMenuBounds.FindIndex(r => r.Contains(e.Location));
            if (_pressedExpandedMenuIndex >= 0) return;
            if (_fullScreenMenuVisible && _fullScreenMenuBounds.Contains(e.Location))
            {
                _fullScreenMenuPressed = true;
                return;
            }
            _mouseDownTabIndex = _tabBounds.FindIndex(r => r.Contains(e.Location));
            _mouseDownCloseIndex = _closeBounds.FindIndex(r => r.Contains(e.Location));
            if (_mouseDownTabIndex >= 0 && _mouseDownCloseIndex < 0)
            {
                _dragStartPoint = e.Location;
                _dragPointerOffset = e.Location.X - _tabBounds[_mouseDownTabIndex].Left;
                _dragPointerOffsetY = e.Location.Y - _tabBounds[_mouseDownTabIndex].Top;
                Capture = true;
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            var index = _tabBounds.FindIndex(r => r.Contains(e.Location));
            if (index >= 0 && index < _documents.Count)
                TabContextRequested?.Invoke(this, (index, PointToScreen(e.Location)));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        UpdateInteractionBounds(GetCurrentRenderBounds());
        if (e.Button != MouseButtons.Left) return;
        if (_newTabPressed)
        {
            var invoke = _newTabBounds.Contains(e.Location);
            _newTabPressed = false;
            Invalidate(_newTabBounds);
            if (invoke) NewDocumentRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_pressedExpandedMenuIndex >= 0)
        {
            var index = _pressedExpandedMenuIndex;
            if (index < _expandedMenuBounds.Count && _expandedMenuBounds[index].Contains(e.Location))
            {
                var bounds = _expandedMenuBounds[index];
                Invalidate(bounds);
                Update();
                TopLevelMenuRequested?.Invoke(
                    this,
                    (index, PointToScreen(new Point(bounds.Left, bounds.Bottom))));
            }
            _pressedExpandedMenuIndex = -1;
            Invalidate();
            return;
        }
        if (_fullScreenMenuPressed)
        {
            if (_fullScreenMenuVisible && _fullScreenMenuBounds.Contains(e.Location))
            {
                Invalidate(_fullScreenMenuBounds);
                Update();
                FullScreenMenuRequested?.Invoke(
                    this,
                    PointToScreen(new Point(_fullScreenMenuBounds.Left, _fullScreenMenuBounds.Bottom)));
            }
            _fullScreenMenuPressed = false;
            Invalidate(_fullScreenMenuBounds);
            return;
        }
        if (_dragging)
        {
            Capture = false;
            Cursor = Cursors.Default;
            var draggedDocument = _dragDocuments[_dragIndex];
            var ownerBounds = FindForm()?.Bounds ?? Rectangle.Empty;
            var screenLocation = PointToScreen(e.Location);
            if (!ownerBounds.Contains(screenLocation))
            {
                // The detached window always starts with its sidebar
                // collapsed. The original tab bar is inside the editor area;
                // when the source sidebar is expanded, this offset includes
                // that sidebar and splitter, while the collapsed case has no
                // such horizontal offset. Subtracting it places the new
                // window's tab at the same mouse-relative position.
                var owner = FindForm();
                var ownerClientOrigin = owner?.PointToScreen(Point.Empty) ?? ownerBounds.Location;
                var tabBarScreenOrigin = PointToScreen(Point.Empty);
                var sourceSidebarOffset = Math.Max(0,
                    tabBarScreenOrigin.X - ownerClientOrigin.X);
                var originalTabBarOffset = new Point(
                    tabBarScreenOrigin.X - ownerBounds.Left,
                    tabBarScreenOrigin.Y - ownerBounds.Top);
                var detachedTabBarOffset = new Point(
                    originalTabBarOffset.X - sourceSidebarOffset,
                    originalTabBarOffset.Y);
                var detachedWindowLocation = new Point(
                    screenLocation.X - _dragPointerOffset
                        - detachedTabBarOffset.X
                        - this.ScaleForDpi(4),
                    screenLocation.Y - _dragPointerOffsetY
                        - detachedTabBarOffset.Y
                        - this.ScaleForDpi(5));
                _dragging = false;
                _mouseDownTabIndex = -1;
                _mouseDownCloseIndex = -1;
                _dragDocuments = [];
                _dragIndex = -1;
                TabDetached?.Invoke(this, (IndexOfDocument(draggedDocument), detachedWindowLocation));
            }
            else
            {
                FinishDragAt(e.Location);
                _mouseDownTabIndex = -1;
                _mouseDownCloseIndex = -1;
            }
            Invalidate();
            return;
        }
        var tabIndex = _tabBounds.FindIndex(r => r.Contains(e.Location));
        var closeIndex = _closeBounds.FindIndex(r => r.Contains(e.Location));
        if (tabIndex == _mouseDownTabIndex && tabIndex >= 0 && tabIndex < _documents.Count)
        {
            if (_mouseDownCloseIndex >= 0 && closeIndex == _mouseDownCloseIndex)
                TabCloseRequested?.Invoke(this, tabIndex);
            else if (_mouseDownCloseIndex < 0 && closeIndex < 0)
                TabSelected?.Invoke(this, tabIndex);
        }
        _mouseDownTabIndex = -1;
        _mouseDownCloseIndex = -1;
        Capture = false;
        Cursor = Cursors.Default;
    }

    private void UpdateDragPosition(Point location)
    {
        if (!_dragging)
        {
            if (_mouseDownTabIndex < 0 || Math.Abs(location.X - _dragStartPoint.X) < this.ScaleForDpi(5)) return;
            _dragging = true;
            Capture = true;
            _dragIndex = _mouseDownTabIndex;
            _dragDocuments = _documents.ToArray();
        }

        var ownerBounds = FindForm()?.Bounds ?? Rectangle.Empty;
        if (!ownerBounds.Contains(PointToScreen(location)))
        {
            Cursor = Cursors.Hand;
            return;
        }

        var dragged = _dragDocuments[_dragIndex];
        var dragLayout = CalculateLayout(_dragDocuments, _scrollOffset, out _);
        var draggedBounds = dragLayout[dragged];
        var desiredLeft = location.X - _dragPointerOffset;
        var draggedCenter = desiredLeft + draggedBounds.Width / 2;
        var targetIndex = 0;
        for (var index = 0; index < _dragDocuments.Count; index++)
        {
            if (index == _dragIndex) continue;
            var center = dragLayout[_dragDocuments[index]].Left
                + dragLayout[_dragDocuments[index]].Width / 2;
            if (draggedCenter > center) targetIndex++;
        }
        if (targetIndex != _dragIndex)
        {
            var oldBounds = GetCurrentDragBounds();
            var list = _dragDocuments.ToList();
            list.RemoveAt(_dragIndex);
            list.Insert(targetIndex, dragged);
            _dragDocuments = list;
            _dragIndex = targetIndex;
            StartDragSwapAnimation(oldBounds);
        }
        Invalidate();
    }

    private Rectangle GetDragBounds(MarkdownDocument document)
    {
        var index = FindDocumentIndex(_dragDocuments, document);
        if (index < 0) return _layoutBounds.GetValueOrDefault(document);

        if (!IsDraggedDocument(document) && _animationActive
            && _animationFromBounds.TryGetValue(document, out var animatedFrom)
            && _animationToBounds.TryGetValue(document, out var animatedTo))
        {
            return Interpolate(animatedFrom, animatedTo, Ease(GetAnimationProgress()));
        }

        var dragLayout = CalculateLayout(_dragDocuments, _scrollOffset, out _);
        var normal = dragLayout.GetValueOrDefault(document);
        if (index == _dragIndex)
        {
            var location = PointToClient(Cursor.Position);
            return new Rectangle(location.X - _dragPointerOffset, normal.Top, normal.Width, normal.Height);
        }
        return normal;
    }

    private bool IsDraggedDocument(MarkdownDocument document)
        => _dragging && _dragIndex >= 0 && _dragIndex < _dragDocuments.Count
            && ReferenceEquals(document, _dragDocuments[_dragIndex]);

    private Dictionary<MarkdownDocument, Rectangle> GetCurrentDragBounds()
    {
        var result = new Dictionary<MarkdownDocument, Rectangle>();
        var progress = _animationActive ? Ease(GetAnimationProgress()) : 1F;
        foreach (var document in _dragDocuments)
        {
            if (IsDraggedDocument(document))
            {
                result[document] = GetDragBounds(document);
            }
            else if (_animationActive && _animationFromBounds.TryGetValue(document, out var from)
                && _animationToBounds.TryGetValue(document, out var to))
            {
                result[document] = Interpolate(from, to, progress);
            }
            else
            {
                result[document] = CalculateLayout(_dragDocuments, _scrollOffset, out _)[document];
            }
        }
        return result;
    }

    private void StartDragSwapAnimation(Dictionary<MarkdownDocument, Rectangle> oldBounds)
    {
        var targetBounds = CalculateLayout(_dragDocuments, _scrollOffset, out _);
        var fromBounds = new Dictionary<MarkdownDocument, Rectangle>();
        var toBounds = new Dictionary<MarkdownDocument, Rectangle>();
        foreach (var document in _dragDocuments)
        {
            if (IsDraggedDocument(document)) continue;
            fromBounds[document] = oldBounds.GetValueOrDefault(document, targetBounds[document]);
            toBounds[document] = targetBounds[document];
        }

        _animationDocuments = _dragDocuments.Where(document => !IsDraggedDocument(document)).ToArray();
        _animationFromBounds = fromBounds;
        _animationToBounds = toBounds;
        StartAnimation();
    }

    private void FinishDragAt(Point location)
    {
        var dragged = _dragDocuments[_dragIndex];
        var releaseBounds = GetDragBounds(dragged);
        var reordered = _dragDocuments.ToArray();
        var targetBounds = CalculateLayout(reordered, _scrollOffset, out _);

        _dragging = false;
        _dragDocuments = [];
        _dragIndex = -1;
        _layoutDocuments = reordered;
        _layoutBounds = targetBounds;
        _documents = reordered;
        _selectedIndex = FindDocumentIndex(reordered, dragged);
        UpdateInteractionBounds(targetBounds);

        _animationDocuments = reordered;
        _animationFromBounds = reordered.ToDictionary(document => document,
            document => ReferenceEquals(document, dragged) ? releaseBounds : targetBounds[document]);
        _animationToBounds = reordered.ToDictionary(document => document,
            document => targetBounds[document]);
        StartAnimation();
        TabsReordered?.Invoke(this, reordered);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Cursor = Cursors.Default;
            _animationTimer.Dispose();
            _font.Dispose(); _selectedFont.Dispose(); _markerFont.Dispose(); _iconFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private string GetMarkerText(MarkdownDocument document)
        => document.IsReadOnly ? Loc.Get("document.readOnlyTab")
            : IsExternalDocument(document) ? Loc.Get("document.externalTab") : string.Empty;

    private bool IsExternalDocument(MarkdownDocument document)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot) || string.IsNullOrWhiteSpace(document.FilePath)) return false;
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(_workspaceRoot), Path.GetFullPath(document.FilePath));
            return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relative);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return true; }
    }

    private Dictionary<MarkdownDocument, Rectangle> CalculateLayout(
        IReadOnlyList<MarkdownDocument> documents, int scrollOffset, out int maximumScrollOffset)
    {
        var outerPadding = this.ScaleForDpi(4);
        var left = outerPadding;
        var right = this.ScaleForDpi(4);
        var top = this.ScaleForDpi(5);
        var bottom = this.ScaleForDpi(5);
        var gap = this.ScaleForDpi(4);
        var height = Math.Max(0, ClientSize.Height - top - bottom);
        UpdateMenuButtonBounds();
        right += _newTabBounds.Width + outerPadding;
        if (_fullScreenMenuVisible)
        {
            left = (UseExpandedMenu ? _expandedMenuBounds[^1].Right : _fullScreenMenuBounds.Right)
                + outerPadding;
        }
        var available = Math.Max(0, ClientSize.Width - left - right);
        var width = GetTabWidth(documents.Count);
        if (_holdTabWidth && _heldTabWidth > 0) width = _heldTabWidth;
        var total = width * documents.Count + gap * Math.Max(0, documents.Count - 1);
        maximumScrollOffset = Math.Max(0, total - available);
        var result = new Dictionary<MarkdownDocument, Rectangle>();
        var x = left - Math.Clamp(scrollOffset, 0, maximumScrollOffset);
        foreach (var document in documents)
        {
            result[document] = new Rectangle(x, top, width, height);
            x += width + gap;
        }
        return result;
    }

    private void UpdateMenuButtonBounds()
    {
        var outerPadding = this.ScaleForDpi(4);
        var top = this.ScaleForDpi(5);
        var bottom = this.ScaleForDpi(5);
        var height = Math.Max(0, ClientSize.Height - top - bottom);
        _newTabBounds = new Rectangle(
            Math.Max(outerPadding, ClientSize.Width - outerPadding - height),
            top,
            height,
            height);
        _expandedMenuBounds.Clear();
        if (!_fullScreenMenuVisible)
        {
            _fullScreenMenuBounds = Rectangle.Empty;
            return;
        }

        if (!UseExpandedMenu)
        {
            _fullScreenMenuBounds = new Rectangle(outerPadding, top, height, height);
            return;
        }

        _fullScreenMenuBounds = Rectangle.Empty;
        var x = outerPadding;
        var gap = this.ScaleForDpi(2);
        var horizontalPadding = this.ScaleForDpi(_showMenuMnemonics ? 6 : 7);
        foreach (var label in GetExpandedMenuLabels())
        {
            var textWidth = TextRenderer.MeasureText(
                label,
                _font,
                Size.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            var width = textWidth + horizontalPadding * 2;
            _expandedMenuBounds.Add(new Rectangle(x, top, width, height));
            x += width + gap;
        }
    }

    private bool UseExpandedMenu
    {
        get => _menuLayoutLocked ? _lockedUseExpandedMenu : CalculateUseExpandedMenu();
    }

    private bool CalculateUseExpandedMenu()
    {
        if (_documents.Count == 0) return true;

        // Keep the complete menu only while every open tab can retain at
        // least two thirds of its normal width. This also makes the choice
        // follow the actual localized menu text instead of a fixed tab count.
        var minimumTabWidth = this.ScaleForDpi(180 * 2 / 3);
        var expandedAvailable = GetAvailableTabWidth(_documents.Count, expanded: true);
        var expandedWidth = GetTabWidth(_documents.Count, expandedAvailable);
        return expandedWidth >= minimumTabWidth;
    }

    private string[] GetExpandedMenuLabels()
        =>
        [
            FormatExpandedMenuLabel("menu.file.label"),
            FormatExpandedMenuLabel("menu.edit.label"),
            FormatExpandedMenuLabel("menu.paragraph.label"),
            FormatExpandedMenuLabel("menu.format.label"),
            FormatExpandedMenuLabel("menu.view.label"),
            FormatExpandedMenuLabel("menu.help.label"),
        ];

    private string[] GetRawExpandedMenuLabels()
        =>
        [
            Loc.Get("menu.file.label"),
            Loc.Get("menu.edit.label"),
            Loc.Get("menu.paragraph.label"),
            Loc.Get("menu.format.label"),
            Loc.Get("menu.view.label"),
            Loc.Get("menu.help.label"),
        ];

    private string FormatExpandedMenuLabel(string localizationKey)
    {
        var text = MenuTextFormatter.Format(
            Loc.Get(localizationKey),
            _showMenuKeyboardShortcuts,
            _showMenuMnemonics,
            _uiLanguage);
        return text.Replace("&", string.Empty, StringComparison.Ordinal);
    }

    private int GetMaximumScrollOffset()
    {
        if (_documents.Count == 0) return 0;
        CalculateLayout(_documents, _scrollOffset, out var maximum);
        return maximum;
    }

    private int GetTabWidth(int count)
    {
        if (count == 0) return 0;
        var available = GetAvailableTabWidth(count, UseExpandedMenu);
        return GetTabWidth(count, available);
    }

    private int GetTabWidth(int count, int available)
    {
        var width = this.ScaleForDpi(180);
        var gap = this.ScaleForDpi(4);
        var total = width * count + gap * Math.Max(0, count - 1);
        return total > available
            ? Math.Max(this.ScaleForDpi(80), (available - gap * (count - 1)) / count)
            : width;
    }

    private int GetAvailableTabWidth(int count, bool expanded)
    {
        if (count == 0) return 0;

        var outerPadding = this.ScaleForDpi(4);
        var newTabWidth = Math.Max(
            outerPadding,
            ClientSize.Height - this.ScaleForDpi(5) - this.ScaleForDpi(5));
        var menuWidth = !_fullScreenMenuVisible
            ? 0
            : expanded ? GetExpandedMenuWidth() : newTabWidth;
        var menuReserved = menuWidth > 0 ? menuWidth + outerPadding : 0;
        var newTabReserved = newTabWidth + outerPadding;
        return Math.Max(0, ClientSize.Width - this.ScaleForDpi(8)
            - menuReserved - newTabReserved);
    }

    private int GetExpandedMenuWidth()
    {
        var x = this.ScaleForDpi(4);
        var gap = this.ScaleForDpi(2);
        var horizontalPadding = this.ScaleForDpi(_showMenuMnemonics ? 6 : 7);
        foreach (var label in GetExpandedMenuLabels())
        {
            var textWidth = TextRenderer.MeasureText(
                label,
                _font,
                Size.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            x += textWidth + horizontalPadding * 2 + gap;
        }
        return Math.Max(0, x - gap);
    }

    private void UpdateInteractionBounds(Dictionary<MarkdownDocument, Rectangle> bounds)
    {
        _tabBounds = _documents.Select(document => bounds.GetValueOrDefault(document)).ToList();
        _closeBounds = _tabBounds.Select(rectangle => new Rectangle(
            rectangle.Right - this.ScaleForDpi(18) - this.ScaleForDpi(5),
            rectangle.Top + (rectangle.Height - this.ScaleForDpi(18)) / 2,
            this.ScaleForDpi(18), this.ScaleForDpi(18))).ToList();
    }

    private void LockWidthForPointer()
    {
        if (_animationActive) return;
        if (_documents.Count > 0)
        {
            var normalWidth = _layoutBounds.Values.FirstOrDefault(rectangle => rectangle.Width > 0).Width;
            if (normalWidth > 0)
            {
                _heldTabWidth = normalWidth;
                _holdTabWidth = true;
            }
        }
    }

    private Dictionary<MarkdownDocument, Rectangle> GetCurrentRenderBounds()
    {
        if (!_animationActive)
            return new Dictionary<MarkdownDocument, Rectangle>(_layoutBounds);

        var progress = Ease(GetAnimationProgress());
        var bounds = new Dictionary<MarkdownDocument, Rectangle>();
        foreach (var document in _animationDocuments)
        {
            if (!_documents.Contains(document)) continue;
            bounds[document] = Interpolate(
                _animationFromBounds[document], _animationToBounds[document], progress);
        }
        return bounds;
    }

    private void ReflowWithAnimation()
    {
        if (_documents.Count == 0) return;

        var oldDocuments = _layoutDocuments.ToArray();
        var oldBounds = new Dictionary<MarkdownDocument, Rectangle>(_layoutBounds);
        if (_animationActive)
        {
            var progress = Ease(GetAnimationProgress());
            oldDocuments = _animationDocuments.ToArray();
            oldBounds = _animationDocuments.ToDictionary(
                document => document,
                document => Interpolate(
                    _animationFromBounds[document], _animationToBounds[document], progress));
        }

        var targetBounds = CalculateLayout(_documents, _scrollOffset, out var maximumScrollOffset);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maximumScrollOffset);
        targetBounds = CalculateLayout(_documents, _scrollOffset, out _);
        _layoutBounds = targetBounds;
        UpdateInteractionBounds(targetBounds);

        var renderDocuments = BuildRenderOrder(oldDocuments, _documents);
        var fromBounds = new Dictionary<MarkdownDocument, Rectangle>();
        var toBounds = new Dictionary<MarkdownDocument, Rectangle>();
        foreach (var document in renderDocuments)
        {
            var hasOld = oldBounds.TryGetValue(document, out var from);
            var hasNew = targetBounds.TryGetValue(document, out var to);
            if (!hasOld) from = Collapse(to);
            if (!hasNew) to = Collapse(from);
            fromBounds[document] = from;
            toBounds[document] = to;
        }

        if (SameLayout(renderDocuments, fromBounds, toBounds))
        {
            StopAnimation();
            return;
        }

        _animationDocuments = renderDocuments;
        _animationFromBounds = fromBounds;
        _animationToBounds = toBounds;
        StartAnimation();
    }

    private int IndexOfDocument(MarkdownDocument document)
    {
        for (var index = 0; index < _documents.Count; index++)
        {
            if (ReferenceEquals(_documents[index], document)) return index;
        }
        return -1;
    }

    private static int FindDocumentIndex(
        IReadOnlyList<MarkdownDocument> documents, MarkdownDocument document)
    {
        for (var index = 0; index < documents.Count; index++)
        {
            if (ReferenceEquals(documents[index], document)) return index;
        }
        return -1;
    }

    private void StartAnimation()
    {
        _animationStartedAt = Environment.TickCount64;
        _animationActive = true;
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _animationActive = false;
        _animationDocuments = [];
        _animationFromBounds = [];
        _animationToBounds = [];
        Visible = !_displaySuppressed && (_documents.Count > 0 || _fullScreenMenuVisible);
    }

    private float GetAnimationProgress()
        => Math.Clamp((Environment.TickCount64 - _animationStartedAt)
            / (float)AnimationDurationMs, 0F, 1F);

    private void OnAnimationTimerTick(object? sender, EventArgs e)
    {
        if (GetAnimationProgress() >= 1F)
        {
            StopAnimation();
            if (_pointerInside) LockWidthForPointer();
            UpdateInteractionBounds(_layoutBounds);
        }
        Invalidate();
    }

    private static Rectangle Collapse(Rectangle rectangle)
        => new(rectangle.Left, rectangle.Top, 0, rectangle.Height);

    private static Rectangle Interpolate(Rectangle from, Rectangle to, float progress)
        => new(
            (int)Math.Round(from.Left + (to.Left - from.Left) * progress),
            (int)Math.Round(from.Top + (to.Top - from.Top) * progress),
            Math.Max(0, (int)Math.Round(from.Width + (to.Width - from.Width) * progress)),
            Math.Max(0, (int)Math.Round(from.Height + (to.Height - from.Height) * progress)));

    private static float Ease(float value)
    {
        // Ease-out: move quickly at the beginning and settle gently at the end.
        var inverse = 1F - value;
        return 1F - inverse * inverse * inverse;
    }

    private static bool SameLayout(
        IReadOnlyList<MarkdownDocument> documents,
        IReadOnlyDictionary<MarkdownDocument, Rectangle> from,
        IReadOnlyDictionary<MarkdownDocument, Rectangle> to)
    {
        foreach (var document in documents)
        {
            if (!from.TryGetValue(document, out var oldRect)
                || !to.TryGetValue(document, out var newRect)
                || oldRect != newRect) return false;
        }
        return true;
    }

    private static bool SameDocuments(
        IReadOnlyList<MarkdownDocument> left,
        IReadOnlyList<MarkdownDocument> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!ReferenceEquals(left[index], right[index])) return false;
        }
        return true;
    }

    private static IReadOnlyList<MarkdownDocument> BuildRenderOrder(
        IReadOnlyList<MarkdownDocument> oldDocuments, IReadOnlyList<MarkdownDocument> newDocuments)
    {
        var result = new List<MarkdownDocument>(newDocuments);
        for (var index = 0; index < oldDocuments.Count; index++)
        {
            if (!newDocuments.Contains(oldDocuments[index]))
                result.Insert(Math.Min(index, result.Count), oldDocuments[index]);
        }
        return result;
    }
}
