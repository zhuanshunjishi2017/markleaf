using System.ComponentModel;
using System.Drawing.Drawing2D;
using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class ColorThemeDialog : Form
{
    private readonly Action<string, bool, string, string> _apply;
    private readonly ThemeStrip _light = new();
    private readonly ThemeStrip _dark = new();
    private readonly CheckBox _follow = new();
    private readonly Button _addThemeButton = new();
    private readonly Button _openThemeFolderButton = new();
    private readonly Panel _separator = new() { Dock = DockStyle.Fill };
    private readonly Panel _footerSeparator = new() { Dock = DockStyle.Bottom };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom };
    private readonly List<ColorThemeButton> _buttons = [];
    private readonly Panel _lightHost = new() { Dock = DockStyle.Fill };
    private readonly Panel _darkHost = new() { Dock = DockStyle.Fill };
    private readonly PreferenceSegmentedTab _tabs = new(
        Loc.Get("dialog.themeSettings.colors"), Loc.Get("dialog.themeSettings.typography"));
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill };
    private readonly Control _colorPage;
    private readonly StyleStrip _typographyPage = new();
    private readonly List<StyleButton> _styleButtons = [];
    private readonly ToolTip _styleToolTip = new();
    private readonly Action<string> _applyTypographyStyle;
    private string _selectedTypographyStyle;
    private readonly ToolTip _themeToolTip = new();
    private readonly bool _systemDark = ColorThemeService.IsSystemDarkMode();
    private string _manualThemeId;
    private int _lastPageIndex;
    private bool _optionalFontsChecked;

    public string SelectedThemeId { get; private set; }
    public string DefaultLightThemeId { get; private set; }
    public string DefaultDarkThemeId { get; private set; }
    public bool FollowSystem => _follow.Checked;

    public ColorThemeDialog(
        string selectedThemeId,
        bool followSystem,
        string defaultLightThemeId,
        string defaultDarkThemeId,
        Action<string, bool, string, string> apply,
        Action addTheme,
        Action openThemeFolder,
        string selectedTypographyStyle,
        Action<string> applyTypographyStyle)
    {
        _apply = apply;
        _applyTypographyStyle = applyTypographyStyle;
        _selectedTypographyStyle = selectedTypographyStyle;
        SelectedThemeId = selectedThemeId;
        _manualThemeId = selectedThemeId;
        DefaultLightThemeId = ColorThemeService.TryGetTheme(defaultLightThemeId) is { IsDark: false } ? defaultLightThemeId : ColorThemeService.GetDefaultLightThemeId();
        DefaultDarkThemeId = ColorThemeService.TryGetTheme(defaultDarkThemeId) is { IsDark: true } ? defaultDarkThemeId : ColorThemeService.GetDefaultDarkThemeId();
        Text = Loc.Get("dialog.themeSettings.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        Size = new Size(this.ScaleForDpi(600), this.ScaleForDpi(310));

        BuildThemeRow(_light, ColorThemeService.All.Where(theme => !theme.IsDark));
        BuildThemeRow(_dark, ColorThemeService.All.Where(theme => theme.IsDark));
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = Padding.Empty };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(1))); body.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        ConfigureThemeHost(_lightHost, _light);
        ConfigureThemeHost(_darkHost, _dark);
        body.Controls.Add(_lightHost, 0, 0); body.Controls.Add(_separator, 0, 1); body.Controls.Add(_darkHost, 0, 2);
        _colorPage = body;
        _typographyPage.Padding = new Padding(this.ScaleForDpi(12), this.ScaleForDpi(8), this.ScaleForDpi(12), this.ScaleForDpi(8));
        foreach (var style in StyleService.Styles)
        {
            var button = new StyleButton(style);
            button.Click += (_, _) => SelectTypographyStyle(style.Id);
            button.MissingFontsClick += (_, _) =>
            {
                using var dialog = new OptionalFontsDialog();
                dialog.ShowDialog(this);
            };
            button.MouseMove += (_, e) =>
            {
                var text = button.IsMissingFontsIconAt(e.Location)
                    ? Loc.Get("dialog.themeSettings.missingFonts")
                    : style.DisplayName;
                if (!string.Equals(_styleToolTip.GetToolTip(button), text, StringComparison.Ordinal))
                    _styleToolTip.SetToolTip(button, text);
            };
            button.MouseLeave += (_, _) => _styleToolTip.SetToolTip(button, style.DisplayName);
            _styleToolTip.SetToolTip(button, style.DisplayName);
            _styleButtons.Add(button);
            _typographyPage.Controls.Add(button);
        }
        _tabs.Dock = DockStyle.Top;
        _tabs.Height = this.ScaleForDpi(38);
        _tabs.TabChanged += (_, index) => ShowPage(index);
        _pageHost.Controls.Add(_colorPage);
        _pageHost.Controls.Add(_typographyPage);
        ShowPage(_lastPageIndex);

        _footer.Height = this.ScaleForDpi(44); _footer.Padding = new Padding(this.ScaleForDpi(12), this.ScaleForDpi(6), this.ScaleForDpi(12), this.ScaleForDpi(7));
        _follow.Text = Loc.Get("prefs.appearance.followSystemColor"); _follow.AutoSize = true; _follow.Dock = DockStyle.Left;
        _follow.CheckedChanged += (_, _) =>
        {
            var wasDark = ColorThemeService.IsActiveThemeDark();
            SelectedThemeId = _follow.Checked
                ? _systemDark ? DefaultDarkThemeId : DefaultLightThemeId
                : _manualThemeId;
            ApplyImmediately();
            UpdateSelection();
            ApplyDialogColors(recreateSystemControls: wasDark != ColorThemeService.IsActiveThemeDark());
            RestoreFocusWhenReady(_follow);
        };
        _addThemeButton.Text = Loc.Get("prefs.appearance.addTheme");
        _addThemeButton.AutoSize = true;
        _addThemeButton.FlatStyle = FlatStyle.System;
        _openThemeFolderButton.Text = Loc.Get("prefs.appearance.openThemeFolder");
        _openThemeFolderButton.AutoSize = true;
        _openThemeFolderButton.FlatStyle = FlatStyle.System;
        _addThemeButton.Click += (_, _) => addTheme();
        _openThemeFolderButton.Click += (_, _) => openThemeFolder();
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        buttons.Controls.Add(_openThemeFolderButton); buttons.Controls.Add(_addThemeButton);
        _footerSeparator.Height = this.ScaleForDpi(1);
        _footer.Controls.Add(buttons); _footer.Controls.Add(_follow);
        Controls.Add(_pageHost); Controls.Add(_tabs); Controls.Add(_footerSeparator); Controls.Add(_footer);
        _follow.Checked = followSystem; UpdateSelection(); ApplyDialogColors();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void Open(Form owner, int? pageIndex = null)
    {
        var targetPage = pageIndex ?? _lastPageIndex;
        _tabs.SelectedIndex = targetPage;
        ShowPage(targetPage);
        if (Owner != owner) Owner = owner;
        var workingArea = Screen.FromControl(owner).WorkingArea;
        var x = owner.Left + (owner.Width - Width) / 2;
        var y = owner.Bottom - Height;
        Location = new Point(
            Math.Clamp(x, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width)),
            Math.Clamp(y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height)));
        if (!Visible) ShowDialog(owner);
        else { BringToFront(); Activate(); }
    }

    private void ShowPage(int index)
    {
        _lastPageIndex = index;
        _colorPage.Visible = index == 0;
        _typographyPage.Visible = index == 1;
        // The system color-mode option only applies to color schemes. Keep the
        // footer itself visible for both pages, but hide this checkbox while
        // browsing typography styles.
        _follow.Visible = index == 0;
        if (index == 0) _colorPage.BringToFront(); else _typographyPage.BringToFront();
        if (index == 1 && !_optionalFontsChecked)
        {
            _optionalFontsChecked = true;
            var missingStyles = OptionalFontsDialog.GetStylesWithMissingFonts();
            foreach (var button in _styleButtons)
            {
                button.HasMissingFonts = missingStyles.Contains(button.Style.Id);
                button.Invalidate();
            }
        }
    }

    private void BuildThemeRow(ThemeStrip panel, IEnumerable<ColorTheme> themes)
    {
        foreach (var theme in themes)
        {
            var button = new ColorThemeButton(theme, GetCurrentThemeColor);
            button.Click += (_, _) => SelectTheme(theme.Id);
            _themeToolTip.SetToolTip(button, theme.DisplayName);
            _buttons.Add(button);
            panel.Controls.Add(button);
        }
    }

    private void SelectTypographyStyle(string id)
    {
        _selectedTypographyStyle = id;
        _applyTypographyStyle(id);
        UpdateTypographySelection();
    }

    private void UpdateTypographySelection()
    {
        foreach (var button in _styleButtons)
        {
            button.Selected = button.Style.Id == _selectedTypographyStyle;
            button.Invalidate();
        }
    }

    private void SelectTheme(string id)
    {
        var focused = ActiveControl;
        var theme = ColorThemeService.TryGetTheme(id);
        if (theme is null) return;
        var wasDark = ColorThemeService.IsActiveThemeDark();
        SelectedThemeId = id;
        if (_follow.Checked)
        {
            if (theme.IsDark) DefaultDarkThemeId = id;
            else DefaultLightThemeId = id;
            ApplyImmediately();
        }
        else
        {
            _manualThemeId = id;
            ApplyImmediately();
        }
        UpdateSelection();
        ApplyDialogColors(recreateSystemControls: wasDark != ColorThemeService.IsActiveThemeDark());
        if (focused is not null)
            RestoreFocusWhenReady(focused);
    }

    private void UpdateSelection()
    {
        foreach (var button in _buttons)
        {
            var isModeDefault = button.Theme.Id == (button.Theme.IsDark ? DefaultDarkThemeId : DefaultLightThemeId);
            button.Selected = _follow.Checked ? isModeDefault && button.Theme.IsDark == _systemDark : button.Theme.Id == SelectedThemeId;
            button.Emphasized = _follow.Checked && isModeDefault && button.Theme.IsDark != _systemDark;
            button.Muted = _follow.Checked && !isModeDefault;
            button.BoldLabel = _follow.Checked ? isModeDefault : button.Theme.Id == SelectedThemeId;
            button.Invalidate();
        }
    }

    private void ApplyDialogColors(bool recreateSystemControls = true)
    {
        var colors = ColorThemeService.GetActiveColors(); Color Get(string name, Color fallback) => colors.TryGetValue(name, out var value) ? value : fallback;
        BackColor = Get("bg-secondary", SystemColors.Control); ForeColor = Get("text-primary", SystemColors.ControlText);
        _tabs.ApplyThemeColors(colors);
        _pageHost.BackColor = BackColor;
        _typographyPage.BackColor = BackColor;
        foreach (var button in _styleButtons) { button.ApplyThemeColors(colors); button.Invalidate(); }
        _light.BackColor = _dark.BackColor = _footer.BackColor = BackColor;
        _separator.BackColor = _footerSeparator.BackColor = Get("bg-selected", SystemColors.ControlDark);
        _follow.BackColor = BackColor;
        _follow.ForeColor = ForeColor;
        foreach (var button in _buttons) button.Invalidate(); UpdateTypographySelection(); Invalidate(true);
        if (recreateSystemControls)
            RecreateSystemControls();
    }

    private void RecreateSystemControls()
    {
        if (!IsHandleCreated) return;
        foreach (var control in new Control[] { _follow, _addThemeButton, _openThemeFolderButton })
        {
            if (!control.IsHandleCreated) continue;
            typeof(Control).GetMethod("RecreateHandle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(control, null);
            control.Invalidate(true);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape)
        {
            Close();
            return true;
        }
        if (key is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down)) return base.ProcessCmdKey(ref msg, keyData);
        if (_tabs.SelectedIndex == 1)
        {
            if (_styleButtons.Count == 0) return true;
            const int columns = 3;
            var styleIndex = Math.Max(0, _styleButtons.FindIndex(button => button.Style.Id == _selectedTypographyStyle));
            styleIndex = key switch
            {
                Keys.Left => Math.Max(0, styleIndex - 1),
                Keys.Right => Math.Min(_styleButtons.Count - 1, styleIndex + 1),
                Keys.Up => Math.Max(0, styleIndex - columns),
                Keys.Down => Math.Min(_styleButtons.Count - 1, styleIndex + columns),
                _ => styleIndex,
            };
            var button = _styleButtons[styleIndex];
            SelectTypographyStyle(button.Style.Id);
            button.Focus();
            _typographyPage.ScrollControlIntoView(button);
            return true;
        }
        var current = ColorThemeService.TryGetTheme(SelectedThemeId); if (current is null) return base.ProcessCmdKey(ref msg, keyData);
        var same = ColorThemeService.All.Where(theme => theme.IsDark == current.IsDark).ToArray(); var index = Math.Max(0, Array.FindIndex(same, theme => theme.Id == current.Id));
        var target = key is Keys.Up or Keys.Down ? ColorThemeService.All.Where(theme => theme.IsDark != current.IsDark).ToArray() : same;
        if (target.Length == 0) return true;
        index = key switch { Keys.Left => (index - 1 + target.Length) % target.Length, Keys.Right => (index + 1) % target.Length, _ => Math.Min(index, target.Length - 1) };
        SelectTheme(target[index].Id); _buttons.First(button => button.Theme.Id == target[index].Id).Focus(); return true;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyThemeGroupDpiMetrics();
    }

    private void RestoreFocusWhenReady(Control control)
    {
        if (control.IsDisposed) return;
        if (IsHandleCreated) BeginInvoke(() => { if (!control.IsDisposed) control.Focus(); });
        else Shown += RestoreOnShown;

        void RestoreOnShown(object? sender, EventArgs e)
        {
            Shown -= RestoreOnShown;
            if (!control.IsDisposed) control.Focus();
        }
    }
    private void ApplyImmediately() => _apply(_manualThemeId, _follow.Checked, DefaultLightThemeId, DefaultDarkThemeId);
    private Color GetCurrentThemeColor(string name, Color fallback) => ColorThemeService.GetActiveColors().TryGetValue(name, out var value) ? value : fallback;
    private void ConfigureThemeHost(Panel host, Control panel) { host.Controls.Add(panel); ApplyThemeGroupDpiMetrics(); }
    private void ApplyThemeGroupDpiMetrics()
    {
        var horizontalInset = this.ScaleForDpi(12);
        var verticalInset = this.ScaleForDpi(8);
        var bottomInset = this.ScaleForDpi(2)
            + this.ScaleForDpi((int)Math.Round(22d * 2d / 3d));
        _lightHost.Padding = _darkHost.Padding = Padding.Empty;
        _light.Padding = _dark.Padding = new Padding(horizontalInset, verticalInset, 0, 0);
        foreach (var button in _buttons)
            button.SetBottomSpacing(bottomInset);
    }

    private sealed class ThemeStrip : FlowLayoutPanel
    {
        public ThemeStrip() { Dock = DockStyle.Fill; WrapContents = false; AutoScroll = true; FlowDirection = FlowDirection.LeftToRight; }
        protected override void OnMouseWheel(MouseEventArgs e) { AutoScrollPosition = new Point(-AutoScrollPosition.X - Math.Sign(e.Delta) * 60, 0); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            ShowScrollBar(Handle, 0, false);
            ShowScrollBar(Handle, 1, false);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowScrollBar(nint handle, int bar, bool show);
    }

    private sealed class StyleStrip : FlowLayoutPanel
    {
        public StyleStrip()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            Resize += (_, _) => ResizeButtons();
        }

        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); ResizeButtons(); }
        private void ResizeButtons()
        {
            const int columns = 3;
            var spacing = this.ScaleForDpi((int)Math.Round(8d * 2d / 3d));
            var innerWidth = Math.Max(0, ClientSize.Width - Padding.Left - Padding.Right);
            var width = Math.Max(1, (innerWidth - spacing * columns) / columns);
            foreach (Control control in Controls)
                if (control is StyleButton button) button.SetCardWidth(width);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var currentY = -AutoScrollPosition.Y;
            AutoScrollPosition = new Point(0, Math.Max(0, currentY - Math.Sign(e.Delta) * this.ScaleForDpi(48)));
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            ShowScrollBar(Handle, 0, false);
            ShowScrollBar(Handle, 1, false);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowScrollBar(nint handle, int bar, bool show);
    }

    private sealed class StyleButton : Button
    {
        private bool _hovered;
        private IReadOnlyDictionary<string, Color> _colors = new Dictionary<string, Color>();
        private readonly Font _nameFont = new(SystemFonts.MessageBoxFont!.FontFamily, 9, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _iconFont = new(SystemIconProvider.IconFontName, 12, FontStyle.Regular, GraphicsUnit.Point);
        private bool _missingFontsPressed;
        public StyleDefinition Style { get; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool HasMissingFonts { get; set; }
        public event EventHandler? MissingFontsClick;

        public StyleButton(StyleDefinition style)
        {
            Style = style;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ApplyDpiMetrics();
        }

        public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors) => _colors = colors;
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ApplyDpiMetrics(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            _missingFontsPressed = e.Button == MouseButtons.Left && IsMissingFontsIconAt(e.Location);
            base.OnMouseDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_missingFontsPressed)
            {
                _missingFontsPressed = false;
                MissingFontsClick?.Invoke(this, EventArgs.Empty);
                return;
            }
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color Get(string key, Color fallback) => _colors.TryGetValue(key, out var value) ? value : fallback;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            var borderWidth = Selected || _hovered ? ScaleButtonMetric(3) : ScaleButtonMetric(2);
            var content = new Rectangle(ScaleButtonMetric(5), ScaleButtonMetric(5), Width - ScaleButtonMetric(10), Height - ScaleButtonMetric(10));
            var card = Rectangle.Inflate(content, borderWidth, borderWidth);
            using (var background = new SolidBrush(Get("bg-primary", Color.White)))
            using (var path = SidebarGdi.CreateRoundedRect(card, ScaleButtonMetric(8)))
                e.Graphics.FillPath(background, path);
            var border = Selected ? Get("theme-light", Color.DodgerBlue) : _hovered ? Get("bg-selected-hover", Color.Gray) : Get("bg-selected", Color.Gray);
            using (var pen = new Pen(border, borderWidth) { LineJoin = LineJoin.Round })
                SidebarGdi.DrawRoundedRect(e.Graphics, card, ScaleButtonMetric(8), pen);
            using var font = new Font(_nameFont, Selected ? FontStyle.Bold : FontStyle.Regular);
            var textBounds = HasMissingFonts
                ? new Rectangle(content.X, content.Y, Math.Max(0, content.Width - MissingFontsIconWidth), content.Height)
                : content;
            TextRenderer.DrawText(e.Graphics, Style.DisplayName, font, textBounds, Get("text-primary", Color.Black), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (HasMissingFonts)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    SystemIconProvider.OptionalFontWarningIcon,
                    _iconFont,
                    MissingFontsIconBounds,
                    Color.FromArgb(247, 99, 12),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private int MissingFontsIconWidth => ScaleButtonMetric(26);
        private Rectangle MissingFontsIconBounds => new(
            Width - ScaleButtonMetric(12) - MissingFontsIconWidth,
            ScaleButtonMetric(5),
            MissingFontsIconWidth,
            Height - ScaleButtonMetric(10));
        public bool IsMissingFontsIconAt(Point point) => HasMissingFonts && MissingFontsIconBounds.Contains(point);

        private void ApplyDpiMetrics()
        {
            Size = new Size(ScaleButtonMetric(96), ScaleButtonMetric(60));
            var spacing = ScaleButtonMetric(8);
            Margin = new Padding(0, 0, spacing, spacing);
        }

        public void SetCardWidth(int width) { if (width > 0 && Width != width) Width = width; }
        private int ScaleButtonMetric(int value) => this.ScaleForDpi((int)Math.Round(value * 2d / 3d));
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _iconFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class ColorThemeButton : Button
    {
        private bool _hovered;
        private readonly Func<string, Color, Color> _currentColor;
        public ColorTheme Theme { get; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Emphasized { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Muted { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool BoldLabel { get; set; }
        public ColorThemeButton(ColorTheme theme, Func<string, Color, Color> currentColor) { Theme = theme; _currentColor = currentColor; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 7.5F, FontStyle.Regular, GraphicsUnit.Point); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); ApplyDpiMetrics(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ApplyDpiMetrics(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            Color Get(string name, Color fallback)
            {
                var color = Theme.Colors.TryGetValue(name, out var value) ? value : fallback;
                return Muted ? AdjustForMutedTheme(color, Theme.IsDark) : color;
            }
            var highlighted = Selected || Emphasized || _hovered;
            var width = highlighted ? ScaleButtonMetric(3) : ScaleButtonMetric(2);
            var content = new Rectangle(
                ScaleButtonMetric(9),
                ScaleButtonMetric(5),
                ScaleButtonMetric(78),
                ScaleButtonMetric(78));
            var square = Rectangle.Inflate(content, width, width);
            var border = Selected || Emphasized
                ? Get("theme-light", Color.DodgerBlue)
                : highlighted ? Get("bg-selected-hover", Color.Gray) : Get("bg-selected", Color.Gray);
            using (var path = Rounded(square, ScaleButtonMetric(8)))
            {
                var oldClip = e.Graphics.Clip;
                e.Graphics.SetClip(path, CombineMode.Replace);
                var left = content.Width / 3;
                using (var brush = new SolidBrush(Get("bg-secondary", Color.White))) e.Graphics.FillRectangle(brush, square.Left, square.Top, left + width, square.Height);
                using (var brush = new SolidBrush(Get("bg-primary", Color.White))) e.Graphics.FillRectangle(brush, square.Left + left, square.Top, square.Width - left, square.Height);
                using (var brush = new SolidBrush(Get("bg-hover", Color.Gainsboro))) e.Graphics.FillRectangle(brush, square.Left + left, square.Bottom - ScaleButtonMetric(8), square.Width - left, ScaleButtonMetric(8));
                e.Graphics.Clip = oldClip;
                using var pen = new Pen(border, width) { LineJoin = LineJoin.Round };
                e.Graphics.DrawPath(pen, path);
            }
            var leftStart = content.Left + content.Width / 3;
            using (var pen = new Pen(Get("theme-light", Color.DodgerBlue), ScaleButtonMetric(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round }) e.Graphics.DrawLine(pen, content.Left + ScaleButtonMetric(8), content.Top + ScaleButtonMetric(9), content.Left + ScaleButtonMetric(14), content.Top + ScaleButtonMetric(9));
            var lineWidths = new[] { 20, 12, 32, 32, 32 };
            for (var i = 0; i < lineWidths.Length; i++)
            {
                using var pen = new Pen(Get(i == lineWidths.Length - 1 ? "text-secondary" : "text-primary", Color.Gray), ScaleButtonMetric(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                var y = content.Top + ScaleButtonMetric(10 + i * 13);
                e.Graphics.DrawLine(pen, leftStart + ScaleButtonMetric(8), y, Math.Min(content.Right - ScaleButtonMetric(7), leftStart + ScaleButtonMetric(8 + lineWidths[i])), y);
            }
            var labelColor = Muted ? _currentColor("text-secondary", Color.Gray) : _currentColor("text-primary", Color.Black);
            using var labelFont = BoldLabel ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, Theme.DisplayName, labelFont, new Rectangle(0, ScaleButtonMetric(94), Width, ScaleButtonMetric(22)), labelColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
        private void ApplyDpiMetrics()
        {
            Size = new Size(ScaleButtonMetric(96), ScaleButtonMetric(118));
            Margin = new Padding(0, 0, ScaleButtonMetric(8), Margin.Bottom);
        }
        public void SetBottomSpacing(int spacing) => Margin = new Padding(Margin.Left, Margin.Top, Margin.Right, spacing);
        private int ScaleButtonMetric(int value) => this.ScaleForDpi((int)Math.Round(value * 2d / 3d));
        private static Color AdjustForMutedTheme(Color color, bool dark)
        {
            const double amount = 0.38;
            var target = dark ? 0 : 255;
            static int Mix(int value, int target, double amount) => Math.Clamp((int)Math.Round(value + (target - value) * amount), 0, 255);
            return Color.FromArgb(color.A, Mix(color.R, target, amount), Mix(color.G, target, amount), Mix(color.B, target, amount));
        }
        private static GraphicsPath Rounded(Rectangle r, int radius) { var p = new GraphicsPath(); var d = radius * 2; p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
    }
}
