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
            .Take(8)
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
            .Take(8)
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
        if (command is < AppCommand.OpenRecentWorkspace1 or > AppCommand.OpenRecentWorkspace8)
        {
            return false;
        }

        var index = (int)command - (int)AppCommand.OpenRecentWorkspace1;
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
        if (command is < AppCommand.OpenRecentFile1 or > AppCommand.OpenRecentFile8)
        {
            return false;
        }

        var index = (int)command - (int)AppCommand.OpenRecentFile1;
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
            .Take(8)
            .ToList();
    }
}
