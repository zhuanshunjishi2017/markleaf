using MarkLeaf.Commands;

namespace MarkLeaf.Native.ModernMenus;

/// <summary>Owns one context selection. Root Closed may arrive before Click.</summary>
internal sealed class EditorMenuSession(
    long id, Func<AppCommand, CommandState> state, Func<AppCommand, Task> execute,
    Action<string?> preview, Action end)
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _ended;
    private bool _committed;
    private bool _closing;
    internal long Id { get; } = id;
    internal Task Completion => _completion.Task;
    internal bool IsCurrent => !_ended;
    internal bool HasCommitted => _committed;

    internal void Preview(string? command)
    {
        if (!_ended && !_committed && !_closing) preview(command);
    }

    internal void Select(AppCommand command, Action? closeMenu = null)
    {
        if (_ended || _committed || !state(command).IsEnabled) return;
        _committed = true;
        _ = ExecuteAsync(command, closeMenu);
    }

    private async Task ExecuteAsync(AppCommand command, Action? closeMenu)
    {
        Exception? failure = null;
        try
        {
            preview(null);
            closeMenu?.Invoke();
            await execute(command);
        }
        catch (Exception error) { failure = error; }
        finally { Finish(clearPreview: false, failure); }
    }

    internal void Closed(Action<Action> post)
    {
        if (_ended || _closing) return;
        _closing = true;
        // Native controls can raise Closed before their command Click callback.
        post(() => { if (!_committed) Finish(clearPreview: true); });
    }

    internal void Cancel() => Finish(clearPreview: true);

    private void Finish(bool clearPreview, Exception? failure = null)
    {
        if (_ended) return;
        _ended = true;
        try
        {
            if (clearPreview) preview(null);
        }
        finally
        {
            try { end(); }
            finally
            {
                if (failure is not null) _completion.TrySetException(failure);
                else _completion.TrySetResult();
            }
        }
    }
}
