namespace MarkLeaf.Commands;

public static class CommandCatalog
{
    private static readonly IReadOnlyDictionary<Keys, AppCommand> Shortcuts =
        new Dictionary<Keys, AppCommand>
        {
            [Keys.Control | Keys.N] = AppCommand.NewDocument,
            [Keys.Control | Keys.O] = AppCommand.OpenDocument,
            [Keys.Control | Keys.S] = AppCommand.SaveDocument,
            [Keys.Control | Keys.Shift | Keys.S] = AppCommand.SaveDocumentAs,
            [Keys.Control | Keys.Z] = AppCommand.Undo,
            [Keys.Control | Keys.Y] = AppCommand.Redo,
            [Keys.Control | Keys.X] = AppCommand.Cut,
            [Keys.Control | Keys.C] = AppCommand.Copy,
            [Keys.Control | Keys.V] = AppCommand.Paste,
            [Keys.Control | Keys.F] = AppCommand.Find,
            [Keys.Control | Keys.H] = AppCommand.Replace,
            [Keys.Control | Keys.B] = AppCommand.ToggleBold,
            [Keys.Control | Keys.I] = AppCommand.ToggleItalic,
            [Keys.Control | Keys.K] = AppCommand.InsertLink,
            [Keys.Control | Keys.D1] = AppCommand.SetHeading1,
            [Keys.Control | Keys.D2] = AppCommand.SetHeading2,
            [Keys.Control | Keys.D3] = AppCommand.SetHeading3,
            [Keys.Control | Keys.D4] = AppCommand.SetHeading4,
            [Keys.Control | Keys.D5] = AppCommand.SetHeading5,
            [Keys.Control | Keys.D6] = AppCommand.SetHeading6,
        };

    public static IReadOnlyDictionary<Keys, AppCommand> ShortcutMap => Shortcuts;

    public static bool TryGetById(int commandId, out AppCommand command)
    {
        command = (AppCommand)commandId;
        return Enum.IsDefined(command);
    }

    public static bool TryGetByShortcut(Keys keyData, out AppCommand command)
    {
        var normalized = keyData & (Keys.KeyCode | Keys.Modifiers);
        return Shortcuts.TryGetValue(normalized, out command);
    }
}
