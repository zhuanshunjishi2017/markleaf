using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ThemeSettingsDialogPlacementTests
{
    [TestMethod]
    public void DialogIsCenteredInsideOwnerWindow()
    {
        var location = ColorThemeDialog.ComputeDialogLocation(
            new Rectangle(100, 100, 1200, 800),
            new Size(600, 310),
            new Rectangle(0, 0, 1920, 1080));

        Assert.AreEqual(new Point(400, 345), location);
    }

    [TestMethod]
    public void DialogStaysInsideWorkingAreaNearScreenEdges()
    {
        var location = ColorThemeDialog.ComputeDialogLocation(
            new Rectangle(500, 500, 1200, 800),
            new Size(600, 310),
            new Rectangle(0, 0, 1000, 800));

        Assert.AreEqual(new Point(400, 490), location);
    }

    [TestMethod]
    public void DialogUsesTopLeftWhenOwnerIsLargerThanWorkingArea()
    {
        var location = ColorThemeDialog.ComputeDialogLocation(
            new Rectangle(0, 0, 800, 400),
            new Size(600, 310),
            new Rectangle(0, 0, 500, 200));

        Assert.AreEqual(new Point(0, 0), location);
    }
}
