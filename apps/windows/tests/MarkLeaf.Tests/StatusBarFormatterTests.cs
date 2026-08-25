using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class StatusBarFormatterTests
{
    [TestInitialize]
    public void Setup() => LocTestHelper.EnsureInitialized();

    [TestMethod]
    public void FormatCharacterCount_IncludesSelectionOnlyWhenPresent()
    {
        Assert.AreEqual(
            "字数 12",
            StatusBarFormatter.FormatCharacterCount(CreateStatus(12, 0)));
        Assert.AreEqual(
            "字数 12（已选 3）",
            StatusBarFormatter.FormatCharacterCount(CreateStatus(12, 3)));
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
        Assert.AreEqual("UTF-8", StatusBarFormatter.FormatEncoding(DocumentEncodingPolicy.Utf8));
        Assert.AreEqual("UTF-8 with BOM", StatusBarFormatter.FormatEncoding(DocumentEncodingPolicy.Utf8Bom));
        Assert.AreEqual("UTF-16 with BOM", StatusBarFormatter.FormatEncoding(DocumentEncodingPolicy.Utf16Bom));
        Assert.AreEqual("CRLF", StatusBarFormatter.FormatNewLine("\r\n"));
        Assert.AreEqual("LF", StatusBarFormatter.FormatNewLine("\n"));
    }

    [TestMethod]
    public void CommandStatus_UsesChineseCommandNames()
    {
        Assert.AreEqual("已执行：粗体", CommandStatusFormatter.FormatExecuted(Commands.AppCommand.ToggleBold));
        Assert.AreEqual("已执行：三级标题", CommandStatusFormatter.FormatExecuted(Commands.AppCommand.SetHeading3));
        Assert.AreEqual("已执行：任务列表", CommandStatusFormatter.FormatExecuted(Commands.AppCommand.ToggleTaskList));
    }

    private static EditorStatus CreateStatus(int characterCount, int selectionCharacterCount) =>
        new(
            characterCount,
            selectionCharacterCount,
            characterCount,
            characterCount,
            0,
            0,
            0,
            0,
            0,
            "paragraph",
            1,
            1);
}
