using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Services;

namespace MarkLeaf.Native.ModernMenus;

internal enum EditorMenuEntryKind { Command, Separator, Submenu }

internal sealed record EditorMenuEntry(
    EditorMenuEntryKind Kind, AppCommand? Command, string Label, string Shortcut,
    CommandState State, IReadOnlyList<EditorMenuEntry> Children)
{
    internal static EditorMenuEntry Separator { get; } = new(EditorMenuEntryKind.Separator, null, "", "", new(false), Array.Empty<EditorMenuEntry>());
}

internal sealed record EditorMenuModel(IReadOnlyList<EditorMenuEntry> Primary, IReadOnlyList<EditorMenuEntry> Secondary);

/// <summary>Presentation-neutral command tree for the editor context menu.</summary>
internal sealed class EditorMenuModelBuilder(Func<AppCommand, CommandState> state, Func<AppCommand, string> shortcut)
{
    private static readonly AppCommand[] PrimaryCommands =
    [
        AppCommand.ToggleBold, AppCommand.ToggleItalic, AppCommand.ToggleUnderline,
        AppCommand.ToggleStrike, AppCommand.ToggleHighlight, AppCommand.FormatPainter, AppCommand.ClearFormat,
    ];
    private static readonly string[] PrimaryLabels =
    [
        "menu.format.bold", "menu.format.italic", "menu.format.underline",
        "menu.format.strikethrough", "menu.format.highlight", "contextMenu.formatPainter", "menu.format.clearFormat",
    ];
    private static AppCommand[] BlockHandleCommands => NativeMenuService.BlockHandleCommands;
    private sealed class Branch { internal List<EditorMenuEntry> Items { get; } = []; }

    internal EditorMenuModel Build(EditorCommandStatus status, bool inlineCommonActions = false)
    {
        var (branch, commands) = status.ExpandedSource ? BuildExpandedSourceContextMenu() : BuildEditorContextMenu(status);
        var canFormat = !status.ReadOnly && !status.SourceMode && !status.ExpandedSource && state(AppCommand.ToggleBold).IsEnabled;
        // Ordinary paragraphs already contain the traditional group. Add the
        // same group to editable special contexts (e.g. table cells) when the
        // quick row is hidden, so the preference never removes those actions.
        if (inlineCommonActions && canFormat && !commands.Contains(AppCommand.ToggleBold))
            AppendCommonFormattingCommands(branch);
        var primary = !inlineCommonActions && canFormat
            ? PrimaryCommands.Select((command, i) => Entry(command, Loc.Get(PrimaryLabels[i]))).ToArray()
            : Array.Empty<EditorMenuEntry>();
        return new(Array.AsReadOnly(primary), Clean(branch.Items, primary.Select(x => x.Command!.Value).ToHashSet()));
    }

    private static IReadOnlyList<EditorMenuEntry> Clean(IEnumerable<EditorMenuEntry> entries, HashSet<AppCommand> primary)
    {
        var result = new List<EditorMenuEntry>();
        foreach (var original in entries)
        {
            if (original.Command is { } command && primary.Contains(command)) continue;
            var entry = original.Kind == EditorMenuEntryKind.Submenu ? original with { Children = Clean(original.Children, primary) } : original;
            if (entry.Kind == EditorMenuEntryKind.Submenu && entry.Children.Count == 0) continue;
            if (entry.Kind == EditorMenuEntryKind.Separator && (result.Count == 0 || result[^1].Kind == EditorMenuEntryKind.Separator)) continue;
            result.Add(entry);
        }
        if (result.Count > 0 && result[^1].Kind == EditorMenuEntryKind.Separator) result.RemoveAt(result.Count - 1);
        return result.AsReadOnly();
    }

    private EditorMenuEntry Entry(AppCommand command, string label) =>
        new(EditorMenuEntryKind.Command, command, label.Split('\t')[0].Replace("&", ""), shortcut(command), state(command), Array.Empty<EditorMenuEntry>());
    private static Branch CreateMenu(bool _) => new();
    private void AppendCommand(Branch menu, AppCommand command, string label) => menu.Items.Add(Entry(command, label));
    private void AppendMainMenuCommand(Branch menu, AppCommand command, string label) => AppendCommand(menu, command, label);
    private static void AppendSeparator(Branch menu) => menu.Items.Add(EditorMenuEntry.Separator);
    private static void AppendPopup(Branch menu, string label, Branch child) =>
        menu.Items.Add(new(EditorMenuEntryKind.Submenu, null, label.Replace("&", ""), "", new(true), child.Items.AsReadOnly()));

    private (Branch Menu, AppCommand[] Commands) BuildEditorContextMenu(EditorCommandStatus status)
    {
        var menu = CreateMenu(true);
        var commands = new List<AppCommand>();
        try
        {
            if (status.ReadOnly)
            {
                AppendReadOnlyContextMenu(menu);
                commands.AddRange([
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText, AppCommand.CopyHtml,
                    AppCommand.PastePlainText, AppCommand.Paste, AppCommand.SelectAll]);
                return (menu, commands.ToArray());
            }

            if (status.SourceMode)
            {
                AppendCommand(menu, AppCommand.Undo, Loc.Get("contextMenu.undo"));
                AppendCommand(menu, AppCommand.Redo, Loc.Get("contextMenu.redo"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange([AppCommand.Undo, AppCommand.Redo, AppCommand.Cut, AppCommand.Copy, AppCommand.Paste, AppCommand.SelectAll]);
            }
            else if (!string.IsNullOrWhiteSpace(status.FootnoteDefinitionLabel))
            {
                AppendFootnoteCommands(menu);
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));

                var copyPasteAs = CreateMenu(true);
                AppendCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
                AppendCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
                AppendCommand(copyPasteAs, AppCommand.CopyHtml, Loc.Get("contextMenu.copyHtml"));
                AppendSeparator(copyPasteAs);
                AppendCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("contextMenu.pastePlainText"));
                AppendPopup(menu, Loc.Get("contextMenu.copyPasteAs"), copyPasteAs);

                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange([
                    AppCommand.GoToFootnoteReference, AppCommand.ResetFootnoteLabel,
                    AppCommand.ClearFootnoteReferences, AppCommand.DeleteFootnote,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText, AppCommand.CopyHtml,
                    AppCommand.Paste, AppCommand.PastePlainText, AppCommand.SelectAll]);
            }
            else if (status.InTable)
            {
                AppendTableCommands(menu, includeClipboard: true);
                commands.AddRange([
                    AppCommand.EditTableCaption,
                    AppCommand.AddTableRowBefore, AppCommand.AddTableRowAfter, AppCommand.DeleteTableRow,
                    AppCommand.AddTableColumnBefore, AppCommand.AddTableColumnAfter, AppCommand.DeleteTableColumn,
                    AppCommand.AlignTableLeft, AppCommand.AlignTableCenter, AppCommand.AlignTableRight,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.Paste,
                    AppCommand.DeleteTable]);
            }
            else if (status.MathInline || status.MathBlock)
            {
                AppendCommand(menu, AppCommand.EditMath, Loc.Get("contextMenu.math.edit"));
                if (status.MathBlock)
                {
                    AppendCommand(menu, AppCommand.SetMathNumber, Loc.Get("contextMenu.math.number"));
                }
                AppendCommand(menu, AppCommand.ConvertMath,
                    status.MathBlock ? Loc.Get("contextMenu.math.toInline") : Loc.Get("contextMenu.math.toBlock"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.DeleteMath, Loc.Get("contextMenu.math.delete"));
                commands.Add(AppCommand.EditMath);
                if (status.MathBlock) commands.Add(AppCommand.SetMathNumber);
                commands.AddRange([AppCommand.ConvertMath, AppCommand.DeleteMath]);
            }
            else if (status.MermaidSelected)
            {
                AppendCommand(menu, AppCommand.EditMermaid, Loc.Get("contextMenu.mermaid.edit"));
                AppendCommand(menu, AppCommand.RerenderMermaid, Loc.Get("contextMenu.mermaid.rerender"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.DeleteMermaid, Loc.Get("contextMenu.mermaid.delete"));
                commands.AddRange([AppCommand.EditMermaid, AppCommand.RerenderMermaid, AppCommand.DeleteMermaid]);
            }
            else if (status.ImageSelected)
            {
                AppendCommand(menu, AppCommand.ChangeImage, Loc.Get("contextMenu.image.change"));
                AppendCommand(menu, AppCommand.EditImageCaption, Loc.Get("contextMenu.image.caption"));

                AppendPopup(menu, Loc.Get("contextMenu.image.resize"), BuildResizeImageSubmenu());

                AppendCommand(menu, AppCommand.RotateImageClockwise, Loc.Get("contextMenu.image.rotate"));
                AppendCommand(menu, AppCommand.SaveImageAs, Loc.Get("contextMenu.image.saveAs"));
                AppendSeparator(menu);

                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                commands.AddRange([
                    AppCommand.ChangeImage, AppCommand.EditImageCaption, AppCommand.ResizeImage100, AppCommand.ResizeImage50,
                    AppCommand.ResizeImage75, AppCommand.ResizeImage90, AppCommand.RotateImageClockwise,
                    AppCommand.SaveImageAs, AppCommand.Cut, AppCommand.Copy, AppCommand.Paste]);
            }
            else if (status.FrontMatter)
            {
                AppendCodeContextMenu(menu, commands, includeLanguage: false, includeFormat: false);
            }
            else if (status.CodeBlock)
            {
                // 格式化入口的可用性由前端 actions 决定：内置格式化器语言本地处理，
                // 外部格式化器语言需宿主已通过 setCodeFormatterSettings 下发。
                AppendCodeContextMenu(
                    menu,
                    commands,
                    includeLanguage: true,
                    includeFormat: status.Actions.TryGetValue("formatCodeBlock", out var formatAction)
                        && formatAction.IsEnabled);
            }
            else
            {
                // 普通上下文（对齐 macOS showEditorContextMenu 普通分支）：
                // [整理行内代码] 历史 剪贴板 行内命令 图片 段落▸ 格式刷 格式▸ 清除格式。
                if (status.Actions.TryGetValue("normalizeInlineCode", out var normalizeAction)
                    && normalizeAction.IsEnabled)
                {
                    AppendCommand(menu, AppCommand.NormalizeInlineCode, Loc.Get("contextMenu.normalizeInlineCode"));
                    AppendSeparator(menu);
                }

                AppendCommand(menu, AppCommand.Undo, Loc.Get("contextMenu.undo"));
                AppendCommand(menu, AppCommand.Redo, Loc.Get("contextMenu.redo"));
                AppendSeparator(menu);

                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));

                var copyPasteAs = CreateMenu(true);
                AppendCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
                AppendCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
                AppendCommand(copyPasteAs, AppCommand.CopyHtml, Loc.Get("contextMenu.copyHtml"));
                AppendSeparator(copyPasteAs);
                AppendCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("contextMenu.pastePlainText"));
                AppendPopup(menu, Loc.Get("contextMenu.copyAs"), copyPasteAs);

                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                AppendSeparator(menu);

                AppendCommand(menu, AppCommand.ToggleInlineCode, Loc.Get("menu.format.inlineCode"));
                AppendCommand(menu, AppCommand.InsertMathInline, Loc.Get("menu.format.insertMathInline"));
                AppendCommand(menu, AppCommand.InsertLink, Loc.Get("contextMenu.insertLink"));
                AppendPopup(menu, Loc.Get("contextMenu.insertImage"), BuildInsertImageSubmenu());
                AppendSeparator(menu);

                AppendPopup(menu, Loc.Get("contextMenu.paragraphGroup"), BuildBlockHandleMenu());
                AppendSeparator(menu);

                AppendCommonFormattingCommands(menu);
                commands.AddRange(BlockHandleCommands);
                commands.AddRange([
                    AppCommand.Undo, AppCommand.Redo, AppCommand.NormalizeInlineCode,
                    AppCommand.ToggleInlineCode, AppCommand.InsertMathInline,
                    AppCommand.InsertLink, AppCommand.InsertImage, AppCommand.InsertImageFromUrl,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText, AppCommand.CopyHtml,
                    AppCommand.Paste, AppCommand.PastePlainText, AppCommand.SelectAll,
                    AppCommand.FormatPainter, AppCommand.FormatPainterApply,
                    AppCommand.ToggleBold, AppCommand.ToggleItalic, AppCommand.ToggleUnderline,
                    AppCommand.ToggleStrike, AppCommand.ToggleHighlight, AppCommand.ClearFormat]);
            }

            return (menu, commands.ToArray());
        }
        catch
        {
            menu.Items.Clear();
            throw;
        }
    }

    private void AppendCommonFormattingCommands(Branch menu)
    {
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.FormatPainter, Loc.Get("contextMenu.formatPainter"));
        AppendCommand(menu, AppCommand.FormatPainterApply, Loc.Get("contextMenu.formatPainterApply"));
        AppendSeparator(menu);
        var format = CreateMenu(true);
        AppendCommand(format, AppCommand.ToggleBold, Loc.Get("menu.format.bold"));
        AppendCommand(format, AppCommand.ToggleItalic, Loc.Get("menu.format.italic"));
        AppendCommand(format, AppCommand.ToggleUnderline, Loc.Get("menu.format.underline"));
        AppendCommand(format, AppCommand.ToggleStrike, Loc.Get("menu.format.strikethrough"));
        AppendCommand(format, AppCommand.ToggleHighlight, Loc.Get("menu.format.highlight"));
        AppendPopup(menu, Loc.Get("contextMenu.formatGroup"), format);
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.ClearFormat, Loc.Get("menu.format.clearFormat"));
    }

    private (Branch Menu, AppCommand[] Commands) BuildExpandedSourceContextMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
            AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
            AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
            return (menu, [AppCommand.Copy, AppCommand.Paste, AppCommand.Cut, AppCommand.SelectAll]);
        }
        catch
        {
            menu.Items.Clear();
            throw;
        }
    }

    private void AppendCodeContextMenu(Branch menu, List<AppCommand> commands, bool includeLanguage, bool includeFormat)
    {
        if (includeLanguage)
        {
            AppendCommand(menu, AppCommand.DeclareCodeLanguage, Loc.Get("contextMenu.code.declareLanguage"));
            commands.Add(AppCommand.DeclareCodeLanguage);
        }
        if (includeFormat)
        {
            AppendCommand(menu, AppCommand.FormatCodeBlock, Loc.Get("contextMenu.code.formatBlock"));
            commands.Add(AppCommand.FormatCodeBlock);
        }
        AppendCommand(menu, AppCommand.CopyCodeBlock, Loc.Get("contextMenu.code.copyBlock"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.ExitCode, Loc.Get("contextMenu.exitCode"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
        AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
        AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
        commands.AddRange([
            AppCommand.CopyCodeBlock, AppCommand.ExitCode,
            AppCommand.Cut, AppCommand.Copy, AppCommand.Paste]);
    }

    private void AppendFootnoteCommands(Branch menu)
    {
        AppendCommand(menu, AppCommand.GoToFootnoteReference, Loc.Get("contextMenu.footnote.goToReference"));
        AppendCommand(menu, AppCommand.ResetFootnoteLabel, Loc.Get("contextMenu.footnote.resetLabel"));
        AppendCommand(menu, AppCommand.ClearFootnoteReferences, Loc.Get("contextMenu.footnote.clearReferences"));
        AppendCommand(menu, AppCommand.DeleteFootnote, Loc.Get("contextMenu.footnote.delete"));
    }

    private void AppendTableCommands(Branch menu, bool includeClipboard)
    {
        AppendCommand(menu, AppCommand.EditTableCaption, Loc.Get("contextMenu.table.caption"));
        AppendSeparator(menu);

        AppendCommand(menu, AppCommand.AddTableRowBefore, Loc.Get("menu.paragraph.addRowAbove"));
        AppendCommand(menu, AppCommand.AddTableRowAfter, Loc.Get("menu.paragraph.addRowBelow"));
        AppendCommand(menu, AppCommand.DeleteTableRow, Loc.Get("menu.paragraph.deleteRow"));
        AppendSeparator(menu);

        AppendCommand(menu, AppCommand.AddTableColumnBefore, Loc.Get("menu.paragraph.addColumnLeft"));
        AppendCommand(menu, AppCommand.AddTableColumnAfter, Loc.Get("menu.paragraph.addColumnRight"));
        AppendCommand(menu, AppCommand.DeleteTableColumn, Loc.Get("menu.paragraph.deleteColumn"));
        AppendSeparator(menu);

        var align = CreateMenu(true);
        AppendCommand(align, AppCommand.AlignTableLeft, Loc.Get("menu.paragraph.alignLeft"));
        AppendCommand(align, AppCommand.AlignTableCenter, Loc.Get("menu.paragraph.alignCenter"));
        AppendCommand(align, AppCommand.AlignTableRight, Loc.Get("menu.paragraph.alignRight"));
        AppendPopup(menu, Loc.Get("contextMenu.table.align"), align);
        AppendSeparator(menu);

        if (includeClipboard)
        {
            AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
            AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
            AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
            AppendSeparator(menu);
        }

        AppendCommand(menu, AppCommand.DeleteTable, Loc.Get("menu.paragraph.deleteTable"));
    }

    private void AppendReadOnlyContextMenu(Branch menu)
    {
        AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
        AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));

        var copyPasteAs = CreateMenu(true);
        AppendCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
        AppendCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
        AppendCommand(copyPasteAs, AppCommand.CopyHtml, Loc.Get("contextMenu.copyHtml"));
        AppendSeparator(copyPasteAs);
        AppendCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("contextMenu.pastePlainText"));
        AppendPopup(menu, Loc.Get("contextMenu.copyPasteAs"), copyPasteAs);

        AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
    }

    private Branch BuildBlockHandleMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.SetParagraph, Loc.Get("menu.paragraph.paragraph"));

            var headings = CreateMenu(true);
            AppendCommand(headings, AppCommand.SetHeading1, Loc.Get("menu.paragraph.heading1"));
            AppendCommand(headings, AppCommand.SetHeading2, Loc.Get("menu.paragraph.heading2"));
            AppendCommand(headings, AppCommand.SetHeading3, Loc.Get("menu.paragraph.heading3"));
            AppendCommand(headings, AppCommand.SetHeading4, Loc.Get("menu.paragraph.heading4"));
            AppendCommand(headings, AppCommand.SetHeading5, Loc.Get("menu.paragraph.heading5"));
            AppendCommand(headings, AppCommand.SetHeading6, Loc.Get("menu.paragraph.heading6"));
            AppendPopup(menu, Loc.Get("menu.paragraph.heading"), headings);
            AppendSeparator(menu);

            AppendCommand(menu, AppCommand.ToggleQuote, Loc.Get("menu.paragraph.quote"));
            AppendCommand(menu, AppCommand.InsertMathBlock, Loc.Get("menu.paragraph.insertMathBlock"));
            AppendCommand(menu, AppCommand.ToggleCodeBlock, Loc.Get("menu.paragraph.codeBlock"));
            AppendCommand(menu, AppCommand.InsertHorizontalRule, Loc.Get("menu.paragraph.horizontalRule"));

            AppendPopup(menu, Loc.Get("menu.paragraph.alert"), BuildAlertSubmenu(useMainMenuText: false));
            AppendSeparator(menu);

            AppendCommand(menu, AppCommand.InsertTable, Loc.Get("menu.paragraph.insertTable"));

            var lists = CreateMenu(true);
            AppendCommand(lists, AppCommand.ToggleBulletList, Loc.Get("menu.paragraph.bulletList"));
            AppendCommand(lists, AppCommand.ToggleOrderedList, Loc.Get("menu.paragraph.orderedList"));
            AppendCommand(lists, AppCommand.ToggleTaskList, Loc.Get("menu.paragraph.taskList"));
            AppendSeparator(lists);
            AppendCommand(lists, AppCommand.IncreaseListIndent, Loc.Get("menu.paragraph.increaseIndent"));
            AppendCommand(lists, AppCommand.DecreaseListIndent, Loc.Get("menu.paragraph.decreaseIndent"));
            AppendPopup(menu, Loc.Get("menu.paragraph.list"), lists);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertFootnote, Loc.Get("menu.paragraph.insertFootnote"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertLineBefore, Loc.Get("menu.paragraph.insertLineBefore"));
            AppendCommand(menu, AppCommand.InsertLineAfter, Loc.Get("menu.paragraph.insertLineAfter"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.DuplicateParagraph, Loc.Get("menu.paragraph.duplicateParagraph"));
            AppendCommand(menu, AppCommand.DeleteParagraph, Loc.Get("menu.paragraph.deleteParagraph"));
            return menu;
        }
        catch
        {
            menu.Items.Clear();
            throw;
        }
    }

    private Branch BuildInsertImageSubmenu()
    {
        var image = CreateMenu(true);
        AppendCommand(image, AppCommand.InsertImage, Loc.Get("menu.format.insertImage"));
        AppendCommand(image, AppCommand.InsertImageFromUrl, Loc.Get("menu.format.insertImageFromUrl"));
        return image;
    }

    private Branch BuildResizeImageSubmenu()
    {
        var resize = CreateMenu(true);
        AppendCommand(resize, AppCommand.ResizeImage100, Loc.Get("contextMenu.image.resizeFull"));
        AppendCommand(resize, AppCommand.ResizeImage50, Loc.Get("contextMenu.image.resize50"));
        AppendCommand(resize, AppCommand.ResizeImage75, Loc.Get("contextMenu.image.resize75"));
        AppendCommand(resize, AppCommand.ResizeImage90, Loc.Get("contextMenu.image.resize90"));
        return resize;
    }

    private Branch BuildAlertSubmenu(bool useMainMenuText)
    {
        var alerts = CreateMenu(true);
        void AppendAlert(AppCommand command, string key)
        {
            if (useMainMenuText) AppendMainMenuCommand(alerts, command, Loc.Get(key));
            else AppendCommand(alerts, command, Loc.Get(key));
        }

        AppendAlert(AppCommand.InsertAlertNote, "menu.paragraph.alertNote");
        AppendAlert(AppCommand.InsertAlertTip, "menu.paragraph.alertTip");
        AppendAlert(AppCommand.InsertAlertImportant, "menu.paragraph.alertImportant");
        AppendAlert(AppCommand.InsertAlertWarning, "menu.paragraph.alertWarning");
        AppendAlert(AppCommand.InsertAlertCaution, "menu.paragraph.alertCaution");
        return alerts;
    }
}
