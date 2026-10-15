using MarkLeaf.Commands;

namespace MarkLeaf.Native;

/// <summary>One size/hit-test model for the native context menu's formatting row.</summary>
internal sealed class NativeFormatToolbar
{
    internal static readonly AppCommand[] Commands =
    [
        AppCommand.ToggleBold, AppCommand.ToggleItalic, AppCommand.ToggleUnderline,
        AppCommand.ToggleStrike, AppCommand.ToggleHighlight, AppCommand.FormatPainter,
        AppCommand.ClearFormat,
    ];
    internal static readonly string[] Glyphs = ["\uE8DD", "\uE8DB", "\uE8DC", "\uEDE0", "\uE7E6", "\uEC34", "\uE75C"];

    private readonly Action<string?> _preview;
    private readonly int _buttonSize;
    private readonly int _gap;
    private readonly int _padding;
    private readonly int _groupGap;
    internal int HotIndex { get; private set; } = -1;
    internal CommandState[] States { get; }
    internal Size Size { get; }

    internal NativeFormatToolbar(int dpi, Func<AppCommand, CommandState> state, Action<string?> preview)
    {
        int Scale(int value) => (int)Math.Round(value * dpi / 96.0);
        _buttonSize = Scale(32);
        _gap = Scale(4);
        _padding = Scale(8);
        _groupGap = Scale(12);
        Size = new Size(2 * _padding + Commands.Length * _buttonSize + (Commands.Length - 1) * _gap + _groupGap,
            _buttonSize + 2 * _padding);
        States = Commands.Select(state).ToArray();
        _preview = preview;
    }

    internal Rectangle ButtonBounds(Rectangle row, int index) => new(
        row.Left + (row.Width - Size.Width) / 2 + _padding + index * (_buttonSize + _gap) + (index >= 5 ? _groupGap : 0),
        row.Top + (row.Height - _buttonSize) / 2, _buttonSize, _buttonSize);

    internal int HitTest(Point point, Rectangle row)
    {
        for (var i = 0; i < Commands.Length; i++)
            if (States[i].IsEnabled && ButtonBounds(row, i).Contains(point)) return i;
        return -1;
    }

    internal AppCommand? CommandAt(Point point, Rectangle row)
    {
        var index = HitTest(point, row);
        return index >= 0 ? Commands[index] : null;
    }

    internal bool Hover(Point point, Rectangle row) => SetHotIndex(HitTest(point, row));
    internal bool ClearHover() => SetHotIndex(-1);

    internal bool MoveFocus(int direction)
    {
        for (var step = 1; step <= Commands.Length; step++)
        {
            var start = HotIndex < 0 && direction < 0 ? 0 : HotIndex;
            var index = (start + direction * step + Commands.Length * 2) % Commands.Length;
            if (States[index].IsEnabled) return SetHotIndex(index);
        }
        return false;
    }

    private bool SetHotIndex(int index)
    {
        if (HotIndex == index) return false;
        HotIndex = index;
        _preview(index is >= 0 and < 5 && EditorCommandBindings.TryGetCommand(Commands[index], out var command)
            ? command : null);
        return true;
    }
}
