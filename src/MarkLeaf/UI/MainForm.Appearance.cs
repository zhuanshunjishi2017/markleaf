using System.Diagnostics;
using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private void SetMarkdownStyle(string style)
    {
        _markdownStyle = StyleService.TryGetStyle(style) is not null ? style : StyleService.DefaultStyleId;
        _settings.MarkdownStyle = _markdownStyle;
        var editor = _settings.Editor;
        _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily, editor.CjkLanguageTag.ToBcp47());
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);
        _editorHost?.ExecuteCommand("setStyle", _markdownStyle);
        _menuService.RefreshStates();
    }

    private void ToggleFollowSystemColorMode()
    {
        var follow = !_settings.Appearance.FollowSystemColorMode;
        _settings.Appearance.FollowSystemColorMode = follow;
        if (follow)
        {
            _colorTheme = ColorThemeService.GetSystemDefaultThemeId();
        }
        else
        {
            _colorTheme = _settings.ColorTheme;
        }
        ColorThemeService.SetActiveTheme(_colorTheme);
        ApplySidebarColors();
        _editorHost?.ApplyStyles(StyleService.BaseCss, StyleService.Styles, _markdownStyle);
        _menuService.RefreshStates();
        ApplyWindowDarkMode(ColorThemeService.IsActiveThemeDark());
    }

    private void SetColorTheme(string themeId)
    {
        if (ColorThemeService.TryGetTheme(themeId) is null) return;
        _colorTheme = themeId;
        ColorThemeService.SetActiveTheme(themeId);
        _settings.ColorTheme = themeId;
        ApplySidebarColors();
        _editorHost?.ApplyStyles(StyleService.BaseCss, StyleService.Styles, _markdownStyle);
        _menuService.RefreshStates();
        ApplyWindowDarkMode(ColorThemeService.IsActiveThemeDark());
    }

    private void ApplyWindowDarkMode(bool dark)
    {
        DarkModeService.Apply(dark);
        if (!IsHandleCreated) return;
        var value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref value,
            sizeof(int));
        NativeMethods.SetWindowPos(Handle, 0, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);
        NativeMethods.DrawMenuBar(Handle);
    }

    private void OnSystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General)
            return;
        if (!_settings.Appearance.FollowSystemColorMode)
            return;

        var targetThemeId = ColorThemeService.GetSystemDefaultThemeId();
        if (string.Equals(_colorTheme, targetThemeId, StringComparison.Ordinal))
            return;

        BeginInvoke(() => SetColorTheme(targetThemeId));
    }

    private void ApplySidebarColors()
    {
        var colors = ColorThemeService.GetActiveColors();
        if (colors.Count == 0) return;

        if (colors.TryGetValue("bg-primary", out var bg))
        {
            _sidebarPanel.BackColor = bg;
            _workspacePanelHost.BackColor = bg;
            _outlinePanelHost.BackColor = bg;
            _sidebarSplit.Panel2.BackColor = bg;
            _editorPanel.BackColor = bg;
            _workspaceContentPanel.BackColor = bg;
            _editorLoadingView.BackColor = bg;
            if (_searchResultsHost is not null)
            {
                _searchResultsHost.BackColor = bg;
            }
        }
        if (colors.TryGetValue("bg-hover", out var splitter))
            _sidebarSplit.BackColor = splitter;

        if (_statusStrip is not null)
        {
            if (colors.TryGetValue("bg-hover", out var statusBg))
                _statusStrip.BackColor = statusBg;
            if (colors.TryGetValue("text-primary", out var statusText))
            {
                _statusStrip.ForeColor = statusText;
                foreach (ToolStripItem item in _statusStrip.Items)
                    item.ForeColor = statusText;
            }
        }

        _sidebarTabBar.ApplyThemeColors(colors);
        _openFolderPrompt.ApplyThemeColors(colors);
        _workspaceTree.ApplyThemeColors(colors);
        _workspaceDocumentList.ApplyThemeColors(colors);
        _outlineTree.ApplyThemeColors(colors);
        if (_searchResultsView is not null)
        {
            _searchResultsView.ApplyThemeColors(colors);
        }

        if (colors.TryGetValue("bg-primary", out var menuBg))
        {
            _menuBgBrush.Dispose();
            _menuBgBrush = new SolidBrush(menuBg);
        }
        if (colors.TryGetValue("bg-hover", out var menuHl))
        {
            _menuHighlightBrush.Dispose();
            _menuHighlightBrush = new SolidBrush(menuHl);
        }
        if (colors.TryGetValue("text-primary", out var menuText))
        {
            _menuTextBrush.Dispose();
            _menuTextBrush = new SolidBrush(menuText);
        }
        if (colors.TryGetValue("text-tertiary", out var menuDisabled))
        {
            _menuDisabledBrush.Dispose();
            _menuDisabledBrush = new SolidBrush(menuDisabled);
        }
        _menuDarkMode = _settings.Appearance.MenuBarStyle switch
        {
            Services.Settings.MenuBarStyle.Always => true,
            Services.Settings.MenuBarStyle.System => false,
            _ => ColorThemeService.IsActiveThemeDark(),
        };
    }

    private void AddThemeFromFile()
    {
        var directory = StyleService.StylesDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ShowMessage(this, Loc.Get("error.themeFolderNotFound"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = Loc.Get("dialog.selectThemeCss"),
            Filter = Loc.Get("fileFilter.css"),
            DefaultExt = "css",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var sourceFile = dialog.FileName;
        if (!File.Exists(sourceFile))
            return;

        var destFile = Path.Combine(directory, Path.GetFileName(sourceFile));
        if (File.Exists(destFile))
        {
            var choice = MessageBox.Show(
                this,
                Loc.Format("dialog.themeFileExists", Path.GetFileName(destFile)),
                Loc.Get("dialog.fileExistsTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes)
                return;
        }

        try
        {
            File.Copy(sourceFile, destFile, overwrite: true);
            RefreshColorThemes();
            _logger.Info($"Theme file added: {Path.GetFileName(destFile)}");
        }
        catch (Exception exception)
        {
            ShowMessage(this, Loc.Format("error.copyThemeFailed", exception.Message),
                "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshColorThemes()
    {
        var directory = StyleService.StylesDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        ColorThemeService.Initialize(directory);
        _colorTheme = ColorThemeService.TryGetTheme(_colorTheme) is not null
            ? _colorTheme
            : ColorThemeService.All.Count > 0 ? ColorThemeService.All[0].Id : "white";
        _editorHost?.ApplyStyles(StyleService.BaseCss, StyleService.Styles, _markdownStyle);
        ApplySidebarColors();
        _menuService.RefreshStates();
    }

    private void OpenThemeFolder()
    {
        var directory = StyleService.StylesDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ShowMessage(this, Loc.Get("error.themeFolderNotFound"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
                { UseShellExecute = false });
        }
        catch (Exception exception)
        {
            ShowMessage(this, Loc.Format("error.openThemeFolderFailed", exception.Message),
                "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetZoomPercent(int percent)
    {
        var target = NearestZoom(percent);
        _zoomPercent = target;
        _settings.Appearance.ZoomPercent = target;
        _zoomLabel.Text = $"{target}%";
        _editorHost?.SetZoomPercent(target);
        _menuService.RefreshStates();
    }

    private static int NextZoom(int current, int delta)
    {
        var options = AppearanceSettings.ZoomPercentOptions;
        if (options.Length == 0)
        {
            return 100;
        }

        var index = Array.IndexOf(options, current);
        if (index < 0)
        {
            index = 0;
        }

        return options[Math.Clamp(index + delta, 0, options.Length - 1)];
    }

    private static int NearestZoom(int percent)
    {
        var options = AppearanceSettings.ZoomPercentOptions;
        if (options.Length == 0)
        {
            return 100;
        }

        var closest = options[0];
        foreach (var option in options)
        {
            if (Math.Abs(option - percent) < Math.Abs(closest - percent))
            {
                closest = option;
            }
        }

        return closest;
    }

    private void ToggleFocusMode()
    {
        if (!_focusMode)
        {
            _sidebarVisibleBeforeFocus = !_sidebarSplit.Panel1Collapsed;
            if (_sidebarVisibleBeforeFocus)
                CollapseSidebar();
            _menuService.Detach();
            MainMenuStrip = null;
            if (_statusStrip is not null) _statusStrip.Visible = false;
            _focusMode = true;
            SetStatus(Loc.Get("status.focusModeOn"));
            return;
        }

        _focusMode = false;
        if (!IsDisposed)
        {
            _menuService.Attach(Handle);
            if (_statusStrip is not null) _statusStrip.Visible = true;
        }

        if (_sidebarVisibleBeforeFocus)
            ExpandSidebar();
        SetStatus(Loc.Get("status.focusModeOff"));
    }
}
