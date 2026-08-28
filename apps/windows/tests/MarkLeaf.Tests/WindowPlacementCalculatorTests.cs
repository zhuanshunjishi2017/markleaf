using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class WindowPlacementCalculatorTests
{
    [TestMethod]
    public void Normalize_ScalesLogicalWindowAndPanelsExactlyOnce()
    {
        var saved = new WindowSettings
        {
            Left = 100,
            Top = 100,
            Width = 1200,
            Height = 800,
            Dpi = 168,
            WorkspaceWidth = 220,
            OutlineWidth = 200,
            OutlineDetached = true,
            SidebarCollapsed = true,
        };

        var actual = WindowPlacementCalculator.Normalize(
            saved,
            144,
            [new ScreenArea(0, 0, 2560, 1400)]);

        Assert.AreEqual(1800, actual.Width);
        Assert.AreEqual(1200, actual.Height);
        Assert.AreEqual(330, actual.WorkspaceWidth);
        Assert.AreEqual(300, actual.OutlineWidth);
        Assert.IsTrue(actual.OutlineDetached);
        Assert.IsTrue(actual.SidebarCollapsed);
        Assert.AreEqual(144, actual.Dpi);
    }

    [TestMethod]
    public void Normalize_RecentersWindowThatIsOutsideAllScreens()
    {
        var saved = new WindowSettings
        {
            Left = 8000,
            Top = 8000,
            Width = 1200,
            Height = 800,
            Dpi = 96,
            IsMaximized = true,
        };

        var actual = WindowPlacementCalculator.Normalize(
            saved,
            96,
            [new ScreenArea(0, 0, 1920, 1040)]);

        Assert.AreEqual(360, actual.Left);
        Assert.AreEqual(120, actual.Top);
        Assert.IsFalse(actual.IsMaximized);
    }

    [TestMethod]
    public void Normalize_EnforcesMinimumLogicalSize()
    {
        var saved = new WindowSettings
        {
            Width = 100,
            Height = 100,
            Dpi = 96,
        };

        var actual = WindowPlacementCalculator.Normalize(saved, 192, []);

        Assert.AreEqual(1800, actual.Width);
        Assert.AreEqual(1200, actual.Height);
    }

    [TestMethod]
    public void ToLogicalPixels_ConvertsPhysicalValuesAtCurrentDpi()
    {
        Assert.AreEqual(1067, WindowPlacementCalculator.ToLogicalPixels(1868, 168));
        Assert.AreEqual(220, WindowPlacementCalculator.ToLogicalPixels(385, 168));
    }
}
