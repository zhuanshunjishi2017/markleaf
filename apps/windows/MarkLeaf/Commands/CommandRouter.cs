namespace MarkLeaf.Commands;

public sealed class CommandRouter
{
    private readonly ShortcutManager _shortcutManager;
    private readonly Func<AppCommand, CommandState> _getState;
    private readonly Action<AppCommand> _execute;

    public CommandRouter(
        ShortcutManager shortcutManager,
        Func<AppCommand, CommandState> getState,
        Action<AppCommand> execute)
    {
        _shortcutManager = shortcutManager;
        _getState = getState;
        _execute = execute;
    }

    public CommandState GetState(AppCommand command) => _getState(command);

    public bool TryExecuteById(int commandId)
    {
        return CommandCatalog.TryGetById(commandId, out var command) && ExecuteIfEnabled(command);
    }

    public bool TryExecuteShortcut(Keys keyData)
    {
        if (!_shortcutManager.TryGetCommand(keyData, out var command))
        {
            return false;
        }

        ExecuteIfEnabled(command);
        return true;
    }

    public bool ExecuteIfEnabled(AppCommand command)
    {
        if (!_getState(command).IsEnabled)
        {
            return false;
        }

        _execute(command);
        return true;
    }
}
