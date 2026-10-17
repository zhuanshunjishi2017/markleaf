using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.App;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private async void OnRecoveryTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_document is null || !_document.IsDirty || _editorHost?.IsDocumentLoaded != true) return;
        try
        {
            _logger.Info("Recovery timer: requesting snapshot...");
            var snapshot = await _editorHost.RequestSnapshotAsync();
            await _recoveryService.WriteSnapshotAsync(
                RecoverySnapshot.FromDocument(_document, snapshot.Markdown));
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Recovery timer: snapshot request timed out.");
        }
        catch (Exception exception)
        {
            _logger.Warning($"Recovery timer: {exception.Message}");
        }
    }

    private async void OnAutoSaveTimerTick(object? sender, EventArgs eventArgs)
    {
        _autoSaveTimer.Stop();
        if (_document is null
            || !_document.IsDirty
            || _editorHost?.IsDocumentLoaded != true
            || _document.FilePath is null
            || _documentOperationInProgress) return;

        _logger.Info("Auto-save timer: saving...");
        await SaveDocumentAsync(saveAs: false);
        UpdateDocumentChrome();
    }

    private void ResetAllSettingsToDefaults()
    {
        SettingsReset.Apply(_settings, AppSettings.CreateDefaults());
        _settingsService.SaveAsync(
            _settings,
            IsolatedWindow ? SettingsSaveScope.Shared : SettingsSaveScope.Full).GetAwaiter().GetResult();
        SetStatus(Loc.Get("status.settingsReset"));
    }

    private void CollectWindowState()
    {
        var bounds = _editorFullScreen
            ? _boundsBeforeEditorFullScreen
            : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        _settings.MainWindow = new WindowSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = WindowPlacementCalculator.ToLogicalPixels(bounds.Width, _effectiveDpi),
            Height = WindowPlacementCalculator.ToLogicalPixels(bounds.Height, _effectiveDpi),
            Dpi = _effectiveDpi,
            IsMaximized = _editorFullScreen
                ? _windowStateBeforeEditorFullScreen == FormWindowState.Maximized
                : WindowState == FormWindowState.Maximized,
            WorkspaceWidth = WindowPlacementCalculator.ToLogicalPixels(
                _sidebarExpandedWidth,
                _effectiveDpi),
            OutlineWidth = WindowPlacementCalculator.ToLogicalPixels(
                _detachedOutlineWidth,
                _effectiveDpi),
            OutlineDetached = _outlineDetached,
            SidebarCollapsed = _sidebarSplit.Panel1Collapsed,
            SidebarActiveOutline = _sidebarActiveOutline,
        };
        _settings.Workspace.LastFolder = _workspaceRoot;
        _settings.Workspace.LastFile = _document?.FilePath;
        _settings.Workspace.LastFileReadOnly = _document?.IsReadOnly == true;
        _settings.Workspace.OpenDocuments = _openDocuments
            .Where(document => document.FilePath is not null && File.Exists(document.FilePath))
            .Select(document => new OpenDocumentSetting
            {
                Path = document.FilePath!,
                ReadOnly = document.IsReadOnly,
            })
            .ToList();
        _settings.Workspace.ActiveDocumentIndex = _document?.FilePath is null
            ? -1
            : _settings.Workspace.OpenDocuments.FindIndex(item => PathEquals(item.Path, _document.FilePath!));
        _settings.Workspace.RecentFolders = _settings.Workspace.RecentFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.Workspace.RecentFiles = _settings.Workspace.RecentFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void SaveWindowState()
    {
        CollectWindowState();
        PersistSettings(
            "Window state saved.",
            "Window state could not be saved.",
            IsolatedWindow ? SettingsSaveScope.Window : SettingsSaveScope.Full);
    }

    private void SaveSettings()
    {
        CollectWindowState();
        _settings.MarkdownStyle = _markdownStyle;
        if (!_settings.Appearance.FollowSystemColorMode)
        {
            _settings.ColorTheme = _colorTheme;
        }
        _settings.Appearance.ZoomPercent = _zoomPercent;
        PersistSettings(
            "Settings saved.",
            "Settings could not be saved.",
            IsolatedWindow ? SettingsSaveScope.Shared : SettingsSaveScope.Full);
        _ = SettingsChangeChannel.BroadcastAsync(new SettingsPeerNotice(
            SharedSettings: SharedSettingsNotice.From(_settings),
            RecentHistory: null));
    }

    private void PersistSettings(
        string successMessage,
        string errorMessage,
        SettingsSaveScope scope)
    {
        try
        {
            _settingsService.SaveAsync(_settings, scope).GetAwaiter().GetResult();
            _logger.Info(successMessage);
        }
        catch (Exception exception)
        {
            _logger.Error(errorMessage, exception);
        }
    }

    private void ApplyPeerSettings(SettingsPeerNotice notice)
    {
        if (IsDisposed || Disposing) return;
        if (notice.SharedSettings is not null)
        {
            ApplyPeerSharedSettings(notice.SharedSettings);
        }

        if (notice.RecentHistory is not null)
        {
            ApplyPeerRecentHistory(notice.RecentHistory);
        }
    }

    private void ApplyPeerSharedSettings(SharedSettingsNotice shared)
    {
        var previousGeneral = _settings.General;
        var previousLanguage = _settings.General.UiLanguage ?? "";

        shared.ApplyTo(_settings);

        ColorThemeService.DefaultLightThemeId = _settings.Appearance.DefaultLightThemeId;
        ColorThemeService.DefaultDarkThemeId = _settings.Appearance.DefaultDarkThemeId;

        var newLanguage = _settings.General.UiLanguage ?? "";
        if (!string.Equals(previousLanguage, newLanguage, StringComparison.Ordinal))
        {
            ReloadUiLanguage(newLanguage);
        }

        _recoveryTimer.Interval = Math.Clamp(_settings.File.SnapshotIntervalSeconds, 10, 300) * 1000;
        _recoveryTimer.Stop();
        _recoveryTimer.Start();

        var editor = _settings.Editor;
        _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily, editor.CjkLanguageTag.ToBcp47(), editor.VisualCjkAutoSpacing, editor.VisualIgnoreMaxContentWidth);
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);
        _editorHost?.ApplyAutoConvertUnsafeEmphasis(editor.AutoConvertUnsafeEmphasis);
        _editorHost?.ApplyMarkdownEditingSettings(editor);
        ApplyCodeFormatterSettings();
        ApplyCodeHighlightVisibility();
        ApplyBlockHandleVisibility();

        _menuService.RefreshStates();

        if (_settings.General.AssociateMarkdownFiles != previousGeneral.AssociateMarkdownFiles
            || _settings.General.AssociateTextFiles != previousGeneral.AssociateTextFiles)
        {
            ApplyFileAssociations();
        }

        SetMarkdownStyle(_settings.MarkdownStyle);
        ApplyEffectiveColorTheme();
        SetZoomPercent(_settings.Appearance.ZoomPercent);
        TopMost = _settings.Appearance.TopMostWindow;
        _editorHost?.ApplyAutoHideScrollbar(_settings.Appearance.AutoHideScrollbars);
        ApplySidebarAutoHideScrollbar();
        RefreshPersistentStatusBar();
        ApplyMenuPresentation();
        _shortcutManager.ImportSettings(_settings.Shortcut);
        _menuService.RebuildMenu();
        UpdateDocumentChrome();
    }

    private void ApplyPeerRecentHistory(RecentHistoryNotice notice)
    {
        if (IsDisposed || Disposing) return;

        _settings.Workspace.RecentFiles = notice.RecentFiles.ToList();
        _settings.Workspace.RecentFolders = notice.RecentFolders.ToList();
        _menuService.RebuildMenu();
    }

    private bool IsolatedWindow =>
        _options.IsolatedFileWindow || _options.DocumentStatePath is not null || _options.IsExportCommand
            ;
}
