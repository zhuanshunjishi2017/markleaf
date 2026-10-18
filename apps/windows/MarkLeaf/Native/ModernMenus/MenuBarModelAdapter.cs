using System.Text;
using System.Text.RegularExpressions;
using MarkLeaf.Commands;

namespace MarkLeaf.Native.ModernMenus;

/// <summary>
/// 把现有 Win32 菜单树转换成自绘菜单模型。菜单内容仍由 NativeMenuService 的单一定义构建，
/// 这里只负责读取最终的文本、命令 ID、可用/勾选状态，供 EditorContextMenu 渲染。
/// </summary>
internal static partial class MenuBarModelAdapter
{
    internal static EditorMenuModel Convert(nint menu) =>
        new(Array.Empty<EditorMenuEntry>(), ConvertEntries(menu));

    private static IReadOnlyList<EditorMenuEntry> ConvertEntries(nint menu)
    {
        var entries = new List<EditorMenuEntry>(NativeMethods.GetMenuItemCount(menu));
        var count = NativeMethods.GetMenuItemCount(menu);
        for (var index = 0; index < count; index++)
        {
            var text = ReadText(menu, index);
            var submenu = NativeMethods.GetSubMenu(menu, index);
            var state = NativeMethods.GetMenuState(menu, (uint)index, NativeMethods.MfByPosition);
            var commandState = ToCommandState(state);
            if (submenu != 0)
            {
                var (label, _) = SplitShortcut(text);
                entries.Add(new EditorMenuEntry(
                    EditorMenuEntryKind.Submenu, null, StripMnemonic(label), "", commandState,
                    ConvertEntries(submenu)));
                continue;
            }

            if (string.IsNullOrEmpty(text))
            {
                entries.Add(EditorMenuEntry.Separator);
                continue;
            }

            var id = NativeMethods.GetMenuItemID(menu, index);
            var (itemLabel, shortcut) = SplitShortcut(text);
            entries.Add(new EditorMenuEntry(
                EditorMenuEntryKind.Command,
                null,
                StripMnemonic(itemLabel),
                shortcut,
                commandState,
                Array.Empty<EditorMenuEntry>(),
                id == 0 || id == uint.MaxValue ? null : id));
        }

        return entries;
    }

    private static CommandState ToCommandState(uint state)
    {
        var unavailable = NativeMethods.MfGrayed | NativeMethods.MfDisabled;
        return new CommandState((state & unavailable) == 0, (state & NativeMethods.MfChecked) != 0);
    }

    private static (string Label, string Shortcut) SplitShortcut(string text)
    {
        var separator = text.IndexOf('\t');
        return separator < 0
            ? (text, "")
            : (text[..separator], text[(separator + 1)..].Trim());
    }

    private static string StripMnemonic(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var withoutParenthesized = ParenthesizedMnemonicRegex().Replace(text, string.Empty);
        if (!withoutParenthesized.Contains('&')) return withoutParenthesized.Trim();
        var builder = new StringBuilder(withoutParenthesized.Length);
        for (var index = 0; index < withoutParenthesized.Length; index++)
        {
            var current = withoutParenthesized[index];
            if (current != '&') { builder.Append(current); continue; }
            if (index + 1 < withoutParenthesized.Length && withoutParenthesized[index + 1] == '&')
            {
                builder.Append('&');
                index++;
            }
        }
        return builder.ToString().Trim();
    }

    private static string ReadText(nint menu, int index)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetMenuString(menu, (uint)index, buffer, buffer.Capacity, NativeMethods.MfByPosition);
        return buffer.ToString();
    }

    [GeneratedRegex(@"\s*\(&[^)]\)")]
    private static partial Regex ParenthesizedMnemonicRegex();
}
