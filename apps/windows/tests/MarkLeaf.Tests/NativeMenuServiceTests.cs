using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class NativeMenuServiceTests
{
    private static readonly AppCommand[] ClipboardCommands =
    [
        AppCommand.Cut,
        AppCommand.Copy,
        AppCommand.CopyMarkdown,
        AppCommand.CopyPlainText,
        AppCommand.Paste,
        AppCommand.PastePlainText,
    ];

    private static NativeMenuService CreateService() =>
        new(new CommandRouter(new ShortcutManager(new ShortcutSettings()), _ => new CommandState(true), _ => { }),
            new ShortcutManager(new ShortcutSettings()), () => [], () => [], () => "serif", () => 100,
            () => "light", () => false, () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);

    private static AppCommand[] Commands(EditorMenuModel model) =>
        Flatten(model.Secondary).Where(x => x.Command.HasValue).Select(x => x.Command!.Value).ToArray();

    [TestMethod]
    public void BuildBlockMenuModel_InTableUsesTableContextCommandsWithoutClipboard()
    {
        using var service = CreateService();
        var commands = Commands(service.BuildBlockMenuModel(EditorCommandStatus.Empty with { InTable = true }));

        CollectionAssert.AreEqual(
            new AppCommand[]
            {
                AppCommand.EditTableCaption,
                AppCommand.AddTableRowBefore,
                AppCommand.AddTableRowAfter,
                AppCommand.DeleteTableRow,
                AppCommand.AddTableColumnBefore,
                AppCommand.AddTableColumnAfter,
                AppCommand.DeleteTableColumn,
                AppCommand.AlignTableLeft,
                AppCommand.AlignTableCenter,
                AppCommand.AlignTableRight,
                AppCommand.DeleteTable,
            },
            commands);
        Assert.IsFalse(commands.Intersect(ClipboardCommands).Any());
    }

    [TestMethod]
    public void BuildBlockMenuModel_InFootnoteUsesFootnoteContextCommandsWithoutClipboard()
    {
        using var service = CreateService();
        var commands = Commands(service.BuildBlockMenuModel(
            EditorCommandStatus.Empty with { FootnoteDefinitionLabel = "note" }));

        CollectionAssert.AreEqual(
            new AppCommand[]
            {
                AppCommand.GoToFootnoteReference,
                AppCommand.ResetFootnoteLabel,
                AppCommand.ClearFootnoteReferences,
                AppCommand.DeleteFootnote,
            },
            commands);
        Assert.IsFalse(commands.Intersect(ClipboardCommands).Any());
    }

    [TestMethod]
    public void BuildBlockMenuModel_OutsideSpecialBlocksUsesParagraphCommands()
    {
        using var service = CreateService();
        var commands = Commands(service.BuildBlockMenuModel(EditorCommandStatus.Empty));

        Assert.Contains(AppCommand.SetParagraph, commands);
        Assert.Contains(AppCommand.InsertLineBefore, commands);
        Assert.Contains(AppCommand.DeleteParagraph, commands);
        Assert.IsFalse(commands.Intersect(ClipboardCommands).Any());
    }

    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));
}
