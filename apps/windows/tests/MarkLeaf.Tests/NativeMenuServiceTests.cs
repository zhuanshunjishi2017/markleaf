using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native;

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

    [TestMethod]
    public void GetBlockHandleCommands_InTableUsesTableContextCommandsWithoutClipboard()
    {
        var commands = NativeMenuService.GetBlockHandleCommands(EditorCommandStatus.Empty with { InTable = true });

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
    public void GetBlockHandleCommands_InFootnoteUsesFootnoteContextCommandsWithoutClipboard()
    {
        var commands = NativeMenuService.GetBlockHandleCommands(
            EditorCommandStatus.Empty with { FootnoteDefinitionLabel = "note" });

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
    public void GetBlockHandleCommands_OutsideSpecialBlocksUsesParagraphCommands()
    {
        var commands = NativeMenuService.GetBlockHandleCommands(EditorCommandStatus.Empty);

        CollectionAssert.AreEqual(NativeMenuService.BlockHandleCommands, commands);
    }
}
