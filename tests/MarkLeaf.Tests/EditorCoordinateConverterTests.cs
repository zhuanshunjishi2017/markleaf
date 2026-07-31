using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorCoordinateConverterTests
{
    [TestMethod]
    [DataRow(96, 120, 80)]
    [DataRow(120, 150, 100)]
    [DataRow(144, 180, 120)]
    [DataRow(192, 240, 160)]
    public void CssToDevicePoint_ScalesForCurrentMonitorDpi(int dpi, int expectedX, int expectedY)
    {
        var actual = EditorCoordinateConverter.CssToDevicePoint(120, 80, dpi);

        Assert.AreEqual(new Point(expectedX, expectedY), actual);
    }
}
