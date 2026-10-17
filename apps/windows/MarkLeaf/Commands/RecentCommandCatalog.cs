namespace MarkLeaf.Commands;

internal static class RecentCommandCatalog
{
    // Keep the original command IDs: the next historical ID is not recent item 9.
    public static readonly AppCommand[] Files = [
        AppCommand.OpenRecentFile1, AppCommand.OpenRecentFile2, AppCommand.OpenRecentFile3,
        AppCommand.OpenRecentFile4, AppCommand.OpenRecentFile5, AppCommand.OpenRecentFile6,
        AppCommand.OpenRecentFile7, AppCommand.OpenRecentFile8,
        AppCommand.OpenRecentFile9, AppCommand.OpenRecentFile10];
    public static readonly AppCommand[] Folders = [
        AppCommand.OpenRecentWorkspace1, AppCommand.OpenRecentWorkspace2, AppCommand.OpenRecentWorkspace3,
        AppCommand.OpenRecentWorkspace4, AppCommand.OpenRecentWorkspace5, AppCommand.OpenRecentWorkspace6,
        AppCommand.OpenRecentWorkspace7, AppCommand.OpenRecentWorkspace8,
        AppCommand.OpenRecentWorkspace9, AppCommand.OpenRecentWorkspace10];
}
