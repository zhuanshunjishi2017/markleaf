using System.Diagnostics;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private string DefaultNewLine =>
        _settings.File.NewLineStyle == NewLineStyle.Lf ? "\n" : "\r\n";

    private void ReloadUiLanguage(string culture)
    {
        var localesDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Locales");
        Loc.Initialize(localesDir, culture);
        _menuService.Attach(Handle);
        OnEditorStateChanged();
        RefreshPersistentStatusBar();
        UpdateDocumentChrome();
        _sidebarTabBar.ReloadTexts();
        _sidebarSearchBar.ReloadTexts();
        _detachedOutlineTabBar.ReloadTexts();
        _detachedOutlineSearchBar.ReloadTexts();
        _editorHost?.SendFindBarLocalization();
        _openFolderPrompt.Invalidate();
    }

    private void ApplyFileAssociations()
    {
        var enabled = GetEnabledExtensions();
        try
        {
            FileAssociationService.ApplyFileAssociations(Application.ExecutablePath, enabled);
            SetStatus(enabled.Count > 0
                ? Loc.Format("status.fileAssociationAdded", string.Join("、", enabled))
                : Loc.Get("status.fileAssociationRemoved"));
        }
        catch (Exception exception) when (FileAssociationService.IsExpectedRegistryException(exception))
        {
            _logger.Error("Failed to update file association.", exception);
            ShowMessage(this, Loc.Get("error.fileAssociationFailed") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private IReadOnlySet<string> GetEnabledExtensions()
    {
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_settings.General.AssociateMarkdownFiles)
        {
            enabled.Add(".md");
            enabled.Add(".markdown");
        }
        if (_settings.General.AssociateTextFiles)
        {
            enabled.Add(".txt");
        }

        return enabled;
    }

    private void OpenCacheFolder()
    {
        var directory = Path.Combine(_paths.DataDirectory, "Cache");
        Directory.CreateDirectory(directory);
        OpenFolderInExplorer(directory, Loc.Get("folder.cache"));
    }

    private void OpenLogFolder()
    {
        OpenFolderInExplorer(_paths.LogDirectory, Loc.Get("folder.logs"));
    }

    private void OpenSettingsJson()
    {
        var file = _paths.SettingsFile;
        if (!File.Exists(file))
        {
            ShowMessage(this, Loc.Get("dialog.settingsFileNotCreated"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(file)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Failed to open settings file: {file}.", exception);
            ShowMessage(this, Loc.Get("error.cannotOpenSettingsFile") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearLogs()
    {
        if (!Directory.Exists(_paths.LogDirectory))
        {
            return;
        }

        var deleted = 0;
        foreach (var file in Directory.GetFiles(_paths.LogDirectory, "*.log"))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Could not delete log file: {file}. {exception.Message}");
            }
        }

        SetStatus(deleted > 0 ? Loc.Format("status.logsCleared", deleted) : Loc.Get("status.noLogsToClear"));
    }

    private void CleanOldLogs(int retentionDays = 7)
    {
        if (!Directory.Exists(_paths.LogDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var file in Directory.GetFiles(_paths.LogDirectory, "*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void OpenFolderInExplorer(string directory, string displayName)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Failed to open {displayName}: {directory}.", exception);
            ShowMessage(this, Loc.Format("error.openFolderFailed", displayName) + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
