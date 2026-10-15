using System.ComponentModel;
using System.Runtime.InteropServices;
using MarkLeaf.Commands;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Native;

internal sealed record ModernMenuTheme(
    Color Background,
    Color Hover,
    Color Separator,
    Color Text,
    Color SecondaryText,
    Color DisabledText,
    Color Accent)
{
    internal static ModernMenuTheme Create(bool dark)
    {
        return dark
            ? new ModernMenuTheme(
                Color.FromArgb(43, 43, 43),
                Color.FromArgb(58, 58, 58),
                Color.FromArgb(64, 64, 64),
                Color.FromArgb(245, 245, 245),
                Color.FromArgb(178, 178, 178),
                Color.FromArgb(128, 128, 128),
                Color.FromArgb(118, 185, 237))
            : new ModernMenuTheme(
                Color.FromArgb(249, 249, 249),
                Color.FromArgb(229, 229, 229),
                Color.FromArgb(229, 229, 229),
                Color.FromArgb(27, 27, 27),
                Color.FromArgb(96, 96, 96),
                Color.FromArgb(152, 152, 152),
                Color.FromArgb(0, 95, 184));
    }
}

internal sealed record ModernMenuMetrics(
    int ItemHeight,
    int SeparatorHeight,
    int ToolbarButtonSize,
    int CornerRadius,
    int Inset,
    int HorizontalPadding,
    int TextIndent,
    int MinimumWidth)
{
    internal static ModernMenuMetrics ForDpi(int dpi)
    {
        int Scale(int value) => (int)Math.Round(value * dpi / 96.0);

        return new ModernMenuMetrics(
            Scale(34),
            Scale(14),
            Scale(32),
            Scale(6),
            Scale(4),
            Scale(8),
            Scale(36),
            Scale(240));
    }

    internal Rectangle HoverBounds(Rectangle itemBounds)
    {
        return new Rectangle(
            itemBounds.Left + Inset,
            itemBounds.Top + Inset,
            itemBounds.Width - Inset * 2,
            itemBounds.Height - Inset * 2);
    }
}

internal enum ModernMenuKind
{
    Command,
    Separator,
    Submenu
}

internal sealed class ModernMenuItem
{
    internal ModernMenuKind Kind { get; init; }
    internal AppCommand? Command { get; init; }
    internal string Label { get; init; } = "";
    internal string Shortcut { get; init; } = "";
    internal CommandState State { get; init; }
}

/// <summary>Builds one presentation-neutral editor menu tree as a Win32 owner-draw menu.</summary>
internal sealed class ModernMenuBuilder : IDisposable
{
    private const uint MfOwnerDraw = 0x0100;
    private readonly Dictionary<nint, ModernMenuItem> _items = [];
    private readonly List<GCHandle> _handles = [];
    private readonly List<AppCommand> _commands = [];
    private bool _disposed;

    internal IReadOnlyList<AppCommand> Commands => _commands;

    internal nint Menu { get; private set; }

    internal nint Build(EditorMenuModel model, bool showShortcuts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Menu != 0) throw new InvalidOperationException("The menu has already been built.");

        Menu = CreatePopupMenu();
        try
        {
            Render(Menu, model.Secondary, showShortcuts);
            return Menu;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool TryGetItem(nint itemData, out ModernMenuItem item)
    {
        item = null!;
        return !_disposed && _items.TryGetValue(itemData, out var value) && (item = value) is not null;
    }

    internal bool TryGetByCommand(AppCommand command, out ModernMenuItem item)
    {
        item = _items.Values.FirstOrDefault(value => value.Command == command)!;
        return item is not null;
    }

    private nint Render(nint parent, IReadOnlyList<EditorMenuEntry> entries, bool showShortcuts)
    {
        foreach (var entry in entries)
        {
            var item = new ModernMenuItem
            {
                Kind = entry.Kind switch
                {
                    EditorMenuEntryKind.Submenu => ModernMenuKind.Submenu,
                    EditorMenuEntryKind.Separator => ModernMenuKind.Separator,
                    _ => ModernMenuKind.Command
                },
                Command = entry.Command,
                Label = entry.Label,
                Shortcut = showShortcuts ? entry.Shortcut : "",
                State = entry.State
            };
            var data = TrackItem(item);
            if (entry.Command is not null) _commands.Add(entry.Command.Value);
            if (entry.Kind == EditorMenuEntryKind.Separator)
            {
                AppendOwnerDraw(parent, NativeMethods.MfSeparator | MfOwnerDraw, 0, data);
            }
            else if (entry.Kind == EditorMenuEntryKind.Submenu)
            {
                var popup = CreatePopupMenu();
                try
                {
                    Render(popup, entry.Children, showShortcuts);
                    AppendOwnerDraw(parent, NativeMethods.MfPopup | MfOwnerDraw, (nuint)popup, data);
                }
                catch
                {
                    NativeMethods.DestroyMenu(popup);
                    throw;
                }
            }
            else if (entry.Command is { } command)
            {
                AppendOwnerDraw(parent, NativeMethods.MfString | MfOwnerDraw, (nuint)command, data);
            }
        }

        return parent;
    }

    private nint TrackItem(ModernMenuItem item)
    {
        var handle = GCHandle.Alloc(item);
        _handles.Add(handle);
        var pointer = GCHandle.ToIntPtr(handle);
        _items.Add(pointer, item);
        return pointer;
    }

    private static void AppendOwnerDraw(nint menu, uint flags, nuint id, nint data)
    {
        if (!AppendMenu(menu, flags, id, data)) throw new Win32Exception();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _items.Clear();
        foreach (var handle in _handles)
        {
            if (handle.IsAllocated) handle.Free();
        }
        _handles.Clear();
        if (Menu != 0)
        {
            NativeMethods.DestroyMenu(Menu);
            Menu = 0;
        }
    }

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint item, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();
}
