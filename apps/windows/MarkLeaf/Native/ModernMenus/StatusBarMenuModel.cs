using MarkLeaf.Commands;
using MarkLeaf.Documents;

namespace MarkLeaf.Native.ModernMenus;

internal static class StatusBarMenuModel
{
    public static EditorMenuModel Encoding(string currentId, bool enabled) =>
        Options(DocumentEncodingPolicy.All.Select((policy, index) =>
            ((AppCommand)(AppCommand.StatusEncodingUtf8 + index), policy)).ToArray(),
            currentId, enabled, policy => policy.DisplayName);

    public static EditorMenuModel NewLine(string current, bool enabled) => new(
        Array.Empty<EditorMenuEntry>(),
        [
            Item(AppCommand.StatusNewLineCrlf, "CRLF", enabled, current == "\r\n"),
            Item(AppCommand.StatusNewLineLf, "LF", enabled, current == "\n"),
        ]);

    public static EditorMenuModel Zoom(int current, IReadOnlyList<int> options)
    {
        var commands = new Dictionary<int, AppCommand>
        {
            [50] = AppCommand.StatusZoom50,
            [75] = AppCommand.StatusZoom75,
            [90] = AppCommand.StatusZoom90,
            [100] = AppCommand.StatusZoom100,
            [110] = AppCommand.StatusZoom110,
            [125] = AppCommand.StatusZoom125,
            [150] = AppCommand.StatusZoom150,
            [175] = AppCommand.StatusZoom175,
            [200] = AppCommand.StatusZoom200,
        };
        return new(Array.Empty<EditorMenuEntry>(),
            options.Select(percent => Item(commands[percent], $"{percent}%", true, percent == current)).ToArray());
    }

    private static EditorMenuModel Options<T>(
        IReadOnlyList<(AppCommand Command, T Value)> options,
        string currentId,
        bool enabled,
        Func<T, string> label)
    {
        var current = options.Single(x => string.Equals(x.Value switch
        {
            DocumentEncodingPolicy policy => policy.Id,
            _ => string.Empty,
        }, currentId, StringComparison.Ordinal)).Value;
        return new(Array.Empty<EditorMenuEntry>(), options.Select(x =>
            Item(x.Command, label(x.Value), enabled, Equals(x.Value, current))).ToArray());
    }

    private static EditorMenuEntry Item(
        AppCommand command, string label, bool enabled, bool @checked = false) => new(
            EditorMenuEntryKind.Command,
            command,
            label,
            "",
            new CommandState(enabled, @checked),
            Array.Empty<EditorMenuEntry>());
}
