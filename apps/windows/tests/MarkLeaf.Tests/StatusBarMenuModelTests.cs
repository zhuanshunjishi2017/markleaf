using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class StatusBarMenuModelTests
{
    [TestMethod]
    public void StatusEncodingNewLineAndReadonlyStateAreProjected()
    {
        var encoding = StatusBarMenuModel.Encoding(DocumentEncodingPolicy.Utf16Bom.Id, enabled: false);
        var newLine = StatusBarMenuModel.NewLine("\r\n", enabled: true);

        Assert.IsEmpty(encoding.Primary);
        Assert.IsTrue(Flatten(encoding.Secondary).All(x => !x.State.IsEnabled));
        Assert.IsTrue(Flatten(encoding.Secondary).Single(x => x.Command == AppCommand.StatusEncodingUtf16Bom).State.IsChecked);
        Assert.IsTrue(Flatten(newLine.Secondary).Single(x => x.Command == AppCommand.StatusNewLineCrlf).State.IsChecked);
        Assert.IsFalse(Flatten(newLine.Secondary).Single(x => x.Command == AppCommand.StatusNewLineLf).State.IsChecked);
    }

    [TestMethod]
    public void NewLineOptionsUseLfThenCrlf()
    {
        var newLine = StatusBarMenuModel.NewLine("\n", enabled: true);

        CollectionAssert.AreEqual(
            new[] { AppCommand.StatusNewLineLf, AppCommand.StatusNewLineCrlf },
            Flatten(newLine.Secondary).Select(x => x.Command!.Value).ToArray());
    }

    [TestMethod]
    public void ZoomPresetsUseApprovedOptionsAndCurrentCheckState()
    {
        var options = new[] { 50, 75, 90, 100, 110, 125, 150, 175, 200 };
        var model = StatusBarMenuModel.Zoom(125, options);

        CollectionAssert.AreEqual(options, Flatten(model.Secondary).Select(x => int.Parse(x.Label.TrimEnd('%'))).ToArray());
        Assert.IsTrue(Flatten(model.Secondary).Single(x => x.Command == AppCommand.StatusZoom125).State.IsChecked);
    }

    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));
}
