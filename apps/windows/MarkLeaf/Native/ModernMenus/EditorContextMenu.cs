using System.Runtime.InteropServices;
using MarkLeaf.Commands;

namespace MarkLeaf.Native.ModernMenus;

/// <summary>One WinForms popup owns the toolbar, submenus and command lifetime.</summary>
internal sealed class EditorContextMenu : ToolStripDropDownMenu
{
    private readonly EditorMenuSession _session;
    private readonly EditorMenuRenderer _renderer;
    private readonly Font _textFont;
    private readonly List<Font> _iconFonts = [];
    private readonly int _dpi;
    private readonly bool _animate;
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 15 };
    private TaskCompletionSource _dismissed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _fadeStarted;
    private double _fadeFrom;
    private bool _fadingOut;
    private bool _finishClose;
    private ToolStripDropDownCloseReason _closeReason;
    private ToolStripControlHost? _toolbarHost;

    internal Task Dismissed => _dismissed.Task;

    internal EditorContextMenu(EditorMenuModel model, int dpi, bool dark, bool showShortcuts, EditorMenuSession session,
        bool? animate = null)
    {
        _dpi = dpi;
        _session = session;
        _animate = animate ?? (SystemInformation.IsMenuAnimationEnabled && SystemInformation.IsMenuFadeEnabled
            && !SystemInformation.HighContrast);
        _fadeTimer.Tick += (_, _) => AdvanceFade();
        _renderer = new EditorMenuRenderer(dpi, dark);
        _textFont = new Font("Segoe UI", Scale(12), FontStyle.Regular, GraphicsUnit.Pixel);
        Font = _textFont;
        Renderer = _renderer;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        ShowItemToolTips = true;
        BackColor = _renderer.Theme.Background;
        ForeColor = _renderer.Theme.Text;
        Padding = new Padding(Scale(4), Scale(6), Scale(4), Scale(6));
        AutoClose = true;
        DropShadowEnabled = true;
        var width = Scale(232);
        foreach (var entry in model.Secondary)
        {
            var label = TextRenderer.MeasureText(entry.Label, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            var shortcut = showShortcuts && entry.Shortcut.Length > 0
                ? TextRenderer.MeasureText(entry.Shortcut, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Scale(24) : 0;
            width = Math.Max(width, label + shortcut + Scale(56));
        }

        if (model.Primary.Count > 0)
        {
            _toolbarHost = CreateToolbar(model.Primary);
            width = Math.Max(width, _toolbarHost.Width);
            Items.Add(_toolbarHost);
            Items.Add(CreateSeparator(width));
        }

        foreach (var entry in model.Secondary)
        {
            if (entry.Kind == EditorMenuEntryKind.Separator)
            {
                Items.Add(CreateSeparator(width));
                continue;
            }

            var item = new MenuRow(entry.Label)
            {
                Name = entry.Command?.ToString() ?? entry.Label,
                AccessibleName = entry.Label,
                Enabled = entry.State.IsEnabled,
                Checked = entry.State.IsChecked,
                CheckOnClick = false,
                AutoSize = false,
                Size = new Size(width, Scale(28)),
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Tag = entry,
                ShortcutKeyDisplayString = showShortcuts ? entry.Shortcut : "",
                ShowShortcutKeys = showShortcuts,
            };
            item.MouseEnter += (_, _) => _session.Preview(null);
            if (entry.Kind == EditorMenuEntryKind.Submenu)
            {
                item.DropDown = new EditorContextMenu(new([], entry.Children), dpi, dark, showShortcuts, session, _animate);
            }
            else if (entry.Command is { } command)
            {
                item.Click += (_, _) => Commit(command);
            }
            Items.Add(item);
        }
        // DropDownMenu measures menu labels using its standard columns, while our
        // renderer reserves a leading check column and a separate shortcut gap.
        // Include those columns in the popup's minimum width, including submenus.
        MinimumSize = new Size(width + Padding.Horizontal, 0);
    }

    private int Scale(int value) => (int)Math.Round(value * _dpi / 96.0);

    // DropDownMenu recalculates Padding during every layout. Its default reserves
    // a text/image column on the left only; our renderer already owns those columns.
    protected override Padding DefaultPadding => new(Scale(4), Scale(6), Scale(4), Scale(6));

    private ToolStripSeparator CreateSeparator(int width) => new()
    {
        AutoSize = false,
        Size = new Size(width, Scale(9)),
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };

    private ToolStripControlHost CreateToolbar(IReadOnlyList<EditorMenuEntry> entries)
    {
        var strip = new QuickActionStrip
        {
            AutoSize = false,
            GripStyle = ToolStripGripStyle.Hidden,
            CanOverflow = false,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            Renderer = _renderer,
            Padding = new Padding(Scale(4)),
            Margin = Padding.Empty,
            BackColor = _renderer.Theme.Background,
            ShowItemToolTips = true,
            AccessibleName = "Formatting",
        };
        var buttonSize = Scale(32);
        var gap = Scale(2);
        var totalWidth = strip.Padding.Horizontal;
        var iconFont = new Font(UI.SystemIconProvider.IconFontName, Scale(16), FontStyle.Regular, GraphicsUnit.Pixel);
        _iconFonts.Add(iconFont);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Command is not { } command) continue;
            if (i == 5)
            {
                strip.Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(Scale(9), buttonSize), Margin = Padding.Empty });
                totalWidth += Scale(9);
            }
            var glyphIndex = Array.IndexOf(NativeFormatToolbar.Commands, command);
            var button = new ToolStripButton
            {
                Name = command.ToString(),
                Text = glyphIndex >= 0 ? NativeFormatToolbar.Glyphs[glyphIndex] : entry.Label,
                AccessibleName = entry.Label,
                ToolTipText = entry.Shortcut.Length > 0 ? $"{entry.Label} ({entry.Shortcut})" : entry.Label,
                AutoToolTip = false,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = iconFont,
                AutoSize = false,
                Size = new Size(buttonSize, buttonSize),
                Margin = new Padding(gap, 0, gap, 0),
                Padding = Padding.Empty,
                Enabled = entry.State.IsEnabled,
                Checked = entry.State.IsChecked,
                CheckOnClick = false,
            };
            button.MouseEnter += (_, _) =>
            {
                if (button.Enabled && command is AppCommand.ToggleBold or AppCommand.ToggleItalic
                    or AppCommand.ToggleUnderline or AppCommand.ToggleStrike or AppCommand.ToggleHighlight
                    && EditorCommandBindings.TryGetCommand(command, out var preview)) _session.Preview(preview);
            };
            button.MouseLeave += (_, _) => _session.Preview(null);
            button.Click += (_, _) => Commit(command);
            strip.Items.Add(button);
            totalWidth += buttonSize + gap * 2;
        }
        strip.Size = new Size(totalWidth, buttonSize + strip.Padding.Vertical);
        var host = new QuickActionHost(strip)
        {
            AutoSize = false,
            Size = strip.Size,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        strip.NavigateOut = backwards =>
        {
            _session.Preview(null);
            GetNextItem(host, backwards ? ArrowDirection.Up : ArrowDirection.Down)?.Select();
        };
        return host;
    }

    private void Commit(AppCommand command) => _session.Select(command, () =>
    {
        ToolStripDropDown root = this;
        while (root.OwnerItem?.Owner is ToolStripDropDown parent) root = parent;
        root.Close(ToolStripDropDownCloseReason.ItemClicked);
    });

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_toolbarHost is not null && _toolbarHost.Selected && (keyData & Keys.Modifiers) == Keys.None
            && (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Enter or Keys.Space)
        {
            var strip = (ToolStrip)_toolbarHost.Control;
            var selected = strip.Items.OfType<ToolStripButton>().FirstOrDefault(button => button.Selected);
            if ((keyData & Keys.KeyCode) is Keys.Enter or Keys.Space)
                (selected ?? strip.Items.OfType<ToolStripButton>().FirstOrDefault(button => button.Enabled))?.PerformClick();
            else strip.GetNextItem(selected, (keyData & Keys.KeyCode) == Keys.Right ? ArrowDirection.Right : ArrowDirection.Left)?.Select();
            return true;
        }
        _session.Preview(null);
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        base.OnOpening(e);
        if (e.Cancel) return;
        _fadeTimer.Stop();
        _fadingOut = false;
        _dismissed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Opacity = _animate ? 0.04 : 1;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_animate) return;
        _fadeFrom = Opacity;
        _fadeStarted = Environment.TickCount64;
        _fadeTimer.Start();
    }

    protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        if (_finishClose || !_animate || e.CloseReason == ToolStripDropDownCloseReason.CloseCalled)
        {
            _fadeTimer.Stop();
            return;
        }
        e.Cancel = true;
        if (_fadingOut) return;
        _session.Preview(null);
        _fadingOut = true;
        _closeReason = e.CloseReason;
        _fadeFrom = Opacity;
        _fadeStarted = Environment.TickCount64;
        _fadeTimer.Start();
    }

    private void AdvanceFade()
    {
        var progress = Math.Clamp((Environment.TickCount64 - _fadeStarted) / 120.0, 0, 1);
        Opacity = _fadingOut ? Math.Max(0.01, _fadeFrom * (1 - progress)) : _fadeFrom + (1 - _fadeFrom) * progress;
        if (progress < 1) return;
        _fadeTimer.Stop();
        if (!_fadingOut) return;
        _finishClose = true;
        try { Close(_closeReason); }
        finally { _finishClose = false; }
    }

    protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
    {
        _fadeTimer.Stop();
        _fadingOut = false;
        base.OnClosed(e);
        _dismissed.TrySetResult();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var dark = _renderer.Theme.Background.GetBrightness() < 0.5 ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            var corners = 2;
            _ = DwmSetWindowAttribute(Handle, 33, ref corners, sizeof(int));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fadeTimer.Dispose();
        base.Dispose(disposing);
        if (!disposing) return;
        _dismissed.TrySetResult();
        foreach (var font in _iconFonts) font.Dispose();
        _textFont.Dispose();
    }

    private sealed class QuickActionHost(ToolStrip strip) : ToolStripControlHost(strip)
    {
        protected override void SetBounds(Rectangle bounds)
        {
            if (Owner is EditorContextMenu menu)
            {
                // Labels and shortcuts may make the popup wider than this strip.
                // Center the whole group in the final content area; keeping its
                // natural width preserves button sizes, gaps and equal edge insets.
                var content = menu.DisplayRectangle;
                bounds.X = content.Left + Math.Max(0, (content.Width - bounds.Width) / 2);
            }
            base.SetBounds(bounds);
        }
    }

    private sealed class QuickActionStrip : ToolStrip
    {
        internal QuickActionStrip()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        internal Action<bool>? NavigateOut;
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData is Keys.Up or Keys.Down)
            {
                NavigateOut?.Invoke(keyData == Keys.Up);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
    }

    private sealed class MenuRow(string text) : ToolStripMenuItem(text)
    {
        protected override void SetBounds(Rectangle bounds)
        {
            if (Owner is EditorContextMenu menu)
            {
                // ToolStripMenuItem.SetBounds subtracts the owner's left padding
                // to paint a standard menu's image margin. Compensate at the row
                // boundary, so painting, hover and hit testing all share the same
                // symmetric content area, even when the popup is wider than a label.
                var content = menu.DisplayRectangle;
                bounds.X = content.Left + menu.Padding.Left;
                bounds.Width = content.Width;
            }
            base.SetBounds(bounds);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
