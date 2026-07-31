using System.Text;
using MarkLeaf.Editor;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class StatusBarFormatterTests
{
    [TestMethod]
    public void FormatCharacterCount_IncludesSelectionOnlyWhenPresent()
    {
        Assert.AreEqual(
            "字数 12",
            StatusBarFormatter.FormatCharacterCount(new EditorStatus(12, 0, "paragraph", 1, 1)));
        Assert.AreEqual(
            "字数 12（已选 3）",
            StatusBarFormatter.FormatCharacterCount(new EditorStatus(12, 3, "paragraph", 1, 1)));
    }

    [TestMethod]
    public void FormatBlockType_MapsEditorBlocksToChineseLabels()
    {
        Assert.AreEqual("标题 2", StatusBarFormatter.FormatBlockType("heading2"));
        Assert.AreEqual("任务列表", StatusBarFormatter.FormatBlockType("taskList"));
        Assert.AreEqual("图片", StatusBarFormatter.FormatBlockType("image"));
        Assert.AreEqual("正文", StatusBarFormatter.FormatBlockType("paragraph"));
    }

    [TestMethod]
    public void FormatDocumentMetadata_FormatsEncodingAndNewLines()
    {
        Assert.AreEqual("UTF-8", StatusBarFormatter.FormatEncoding(new UTF8Encoding(false), false));
        Assert.AreEqual("UTF-8 BOM", StatusBarFormatter.FormatEncoding(new UTF8Encoding(true), true));
        Assert.AreEqual("UTF-16 LE", StatusBarFormatter.FormatEncoding(Encoding.Unicode, true));
        Assert.AreEqual("CRLF", StatusBarFormatter.FormatNewLine("\r\n"));
        Assert.AreEqual("LF", StatusBarFormatter.FormatNewLine("\n"));
    }

    [TestMethod]
    public void EditorContextMenu_UsesOnlyApprovedUnifiedCommands()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                Commands.AppCommand.ToggleBold,
                Commands.AppCommand.ToggleItalic,
                Commands.AppCommand.SetParagraph,
                Commands.AppCommand.SetHeading1,
                Commands.AppCommand.SetHeading2,
                Commands.AppCommand.SetHeading3,
                Commands.AppCommand.SetHeading4,
                Commands.AppCommand.SetHeading5,
                Commands.AppCommand.SetHeading6,
                Commands.AppCommand.ToggleBulletList,
                Commands.AppCommand.ToggleOrderedList,
                Commands.AppCommand.ToggleTaskList,
                Commands.AppCommand.Cut,
                Commands.AppCommand.Copy,
                Commands.AppCommand.Paste,
            },
            Native.NativeMenuService.EditorContextCommands);
    }

    [TestMethod]
    public void CommandStatus_UsesChineseCommandNames()
    {
        Assert.AreEqual("已执行：粗体", CommandStatusFormatter.FormatExecuted(Commands.AppCommand.ToggleBold));
        Assert.AreEqual("已执行：三级标题", CommandStatusFormatter.FormatExecuted(Commands.AppCommand.SetHeading3));
        Assert.AreEqual("已执行：任务列表", CommandStatusFormatter.FormatExecuted(Commands.AppCommand.ToggleTaskList));
    }
}
