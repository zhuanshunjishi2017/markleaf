using MarkLeaf.Commands;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private IReadOnlyList<string> GetRecentWorkspaces()
    {
        if (!_settings.File.RecordRecentFolders)
        {
            return [];
        }

        return _settings.Workspace.RecentFolders
            .Prepend(_workspaceRoot ?? _settings.Workspace.LastFolder)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TryGetFullPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }

    private IReadOnlyList<string> GetRecentFiles()
    {
        if (!_settings.File.RecordRecentFiles)
        {
            return [];
        }

        return _settings.Workspace.RecentFiles
            .Select(TryGetFullPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }

    private static string? TryGetFullPath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private bool TryGetRecentWorkspace(AppCommand command, out string path)
    {
        path = string.Empty;
        var index = Array.IndexOf(RecentCommandCatalog.Folders, command);
        if (index < 0)
        {
            return false;
        }

        var recent = GetRecentWorkspaces();
        if (index < 0 || index >= recent.Count)
        {
            return false;
        }

        path = recent[index];
        return true;
    }

    private bool TryGetRecentFile(AppCommand command, out string path)
    {
        path = string.Empty;
        var index = Array.IndexOf(RecentCommandCatalog.Files, command);
        if (index < 0)
        {
            return false;
        }

        var recent = GetRecentFiles();
        if (index < 0 || index >= recent.Count)
        {
            return false;
        }

        path = recent[index];
        return true;
    }

    private void AddRecentWorkspace(string path)
    {
        _settings.Workspace.LastFolder = path;
        if (!_settings.File.RecordRecentFolders)
        {
            return;
        }

        _settings.Workspace.RecentFolders = _settings.Workspace.RecentFolders
            .Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
            .Prepend(path)
            .Take(10)
            .ToList();
    }
}
