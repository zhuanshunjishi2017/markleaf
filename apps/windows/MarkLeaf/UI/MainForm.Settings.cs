using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;

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
        _settingsService.SaveAsync(_settings).GetAwaiter().GetResult();
        SetStatus(Loc.Get("status.settingsReset"));
    }

    private void CollectWindowState()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        _settings.MainWindow = new WindowSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = WindowPlacementCalculator.ToLogicalPixels(bounds.Width, _effectiveDpi),
            Height = WindowPlacementCalculator.ToLogicalPixels(bounds.Height, _effectiveDpi),
            Dpi = _effectiveDpi,
            IsMaximized = WindowState == FormWindowState.Maximized,
            WorkspaceWidth = WindowPlacementCalculator.ToLogicalPixels(
                _sidebarSplit.SplitterDistance,
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
        PersistSettings("Window state saved.", "Window state could not be saved.");
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
        PersistSettings("Settings saved.", "Settings could not be saved.");
    }

    private void PersistSettings(string successMessage, string errorMessage)
    {
        try
        {
            _settingsService.SaveAsync(_settings).GetAwaiter().GetResult();
            _logger.Info(successMessage);
        }
        catch (Exception exception)
        {
            _logger.Error(errorMessage, exception);
        }
    }
}
