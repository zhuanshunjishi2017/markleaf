using MarkLeaf.Services.Settings;

namespace MarkLeaf.Commands;

public enum ShortcutConflictKind
{
    None,
    Invalid,
    Duplicate,
}

public readonly record struct ShortcutConflict(ShortcutConflictKind Kind, AppCommand OtherCommand);

/// <summary>
/// 快捷键运行时解析与持久化（对应 macOS ShortcutSettings）。
/// 持有 AppSettings.Shortcut 的引用，任何变更都会重建有效映射并触发 Changed。
/// </summary>
public sealed class ShortcutManager
{
    private readonly ShortcutSettings _model;
    private readonly Dictionary<Keys, AppCommand> _effective = new();

    public event Action? Changed;

    public ShortcutManager(ShortcutSettings model)
    {
        _model = model;
        Rebuild();
    }

    public bool TryGetCommand(Keys keyData, out AppCommand command)
    {
        var normalized = keyData & (Keys.KeyCode | Keys.Modifiers);
        return _effective.TryGetValue(normalized, out command);
    }

    public string? GetShortcutText(AppCommand command)
    {
        foreach (var (keys, mapped) in _effective)
        {
            if (mapped == command)
            {
                return ShortcutTextFormatter.Format(keys);
            }
        }

        return null;
    }

    public bool IsRemappable(AppCommand command) => ShortcutCatalog.Find(command) is not null;

    public ShortcutConflict Validate(Keys keys, AppCommand command)
    {
        var normalized = keys & (Keys.KeyCode | Keys.Modifiers);
        var key = normalized & Keys.KeyCode;
        var hasCommandModifier = (normalized & (Keys.Control | Keys.Alt)) != 0;
        var isFunctionKey = key is >= Keys.F1 and <= Keys.F24;

        if (key is Keys.None or Keys.Escape
            or Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.Shift or Keys.Control or Keys.Alt)
        {
            return new ShortcutConflict(ShortcutConflictKind.Invalid, default);
        }

        var representable = key is >= Keys.D0 and <= Keys.D9
            or >= Keys.A and <= Keys.Z
            or >= Keys.F1 and <= Keys.F24
            or Keys.OemPeriod or Keys.Oemcomma or Keys.OemMinus or Keys.Oemplus or Keys.Space;
        if (!hasCommandModifier && !isFunctionKey || !representable)
        {
            return new ShortcutConflict(ShortcutConflictKind.Invalid, default);
        }

        foreach (var (existingKeys, existingCommand) in _effective)
        {
            if (existingCommand != command && existingKeys == normalized)
            {
                return new ShortcutConflict(ShortcutConflictKind.Duplicate, existingCommand);
            }
        }

        return new ShortcutConflict(ShortcutConflictKind.None, default);
    }

    public bool Set(AppCommand command, Keys keys)
    {
        var normalized = keys & (Keys.KeyCode | Keys.Modifiers);
        if (Validate(normalized, command).Kind != ShortcutConflictKind.None)
        {
            return false;
        }

        var name = command.ToString();
        _model.Overrides[name] = ShortcutTextFormatter.Format(normalized);
        _model.Cleared.Remove(name);
        Rebuild();
        Changed?.Invoke();
        return true;
    }

    public void Clear(AppCommand command)
    {
        var name = command.ToString();
        _model.Overrides.Remove(name);
        if (!_model.Cleared.Contains(name))
        {
            _model.Cleared.Add(name);
        }

        Rebuild();
        Changed?.Invoke();
    }

    public void RestoreDefault(AppCommand command)
    {
        var name = command.ToString();
        _model.Overrides.Remove(name);
        _model.Cleared.Remove(name);
        Rebuild();
        Changed?.Invoke();
    }

    public void ResetAll()
    {
        _model.Overrides.Clear();
        _model.Cleared.Clear();
        Rebuild();
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        _effective.Clear();
        foreach (var entry in ShortcutCatalog.Entries)
        {
            var name = entry.Command.ToString();
            if (_model.Cleared.Contains(name))
            {
                continue;
            }

            if (_model.Overrides.TryGetValue(name, out var text)
                && ShortcutTextFormatter.TryParse(text, out var keys))
            {
                _effective[keys] = entry.Command;
            }
            else if (entry.DefaultShortcut != Keys.None)
            {
                _effective[entry.DefaultShortcut] = entry.Command;
            }
        }
    }
}
