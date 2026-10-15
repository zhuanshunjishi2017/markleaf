using System.Drawing;
using MarkLeaf.Commands;
using MarkLeaf.Native;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class NativeFormatToolbarTests
{
    [TestMethod]
    public void Hover_PreviewsEnabledButtonAndClearsOnExit()
    {
        var previews = new List<string?>();
        var toolbar = new NativeFormatToolbar(96, _ => new CommandState(true, false), previews.Add);
        var row = new Rectangle(300, 200, toolbar.Size.Width + 100, toolbar.Size.Height);
        var italic = toolbar.ButtonBounds(row, 1);
        toolbar.Hover(new Point(italic.Left + 1, italic.Top + 1), row);
        toolbar.Hover(new Point(italic.Left + 2, italic.Top + 2), row);
        toolbar.Hover(new Point(row.Left, row.Bottom + 10), row);

        CollectionAssert.AreEqual(new string?[] { "toggleItalic", null }, previews);
        Assert.AreEqual(-1, toolbar.HotIndex);
    }

    [TestMethod]
    public void DisabledButton_DoesNotPreviewOrExecute()
    {
        var previews = new List<string?>();
        var toolbar = new NativeFormatToolbar(96, _ => new CommandState(false, false), previews.Add);
        var row = new Rectangle(Point.Empty, toolbar.Size);
        var button = toolbar.ButtonBounds(row, 0);
        var point = new Point(button.Left + 2, button.Top + 2);
        toolbar.Hover(point, row);

        Assert.AreEqual(-1, toolbar.HotIndex);
        Assert.IsNull(toolbar.CommandAt(point, row));
        Assert.IsEmpty(previews);
    }

    [TestMethod]
    public void Buttons_KeepEqualDimensionsAndHitTargetsAtEveryDpi()
    {
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        {
            var toolbar = new NativeFormatToolbar(dpi, _ => new CommandState(true, false), _ => { });
            var row = new Rectangle(50, 70, toolbar.Size.Width, toolbar.Size.Height);
            var bounds = Enumerable.Range(0, NativeFormatToolbar.Commands.Length)
                .Select(i => toolbar.ButtonBounds(row, i)).ToArray();
            Assert.AreEqual(1, bounds.Select(b => b.Size).Distinct().Count());
            Assert.AreEqual(1, bounds.Select(b => b.Top).Distinct().Count());
            for (var i = 0; i < bounds.Length; i++)
            {
                Assert.IsTrue(row.Contains(bounds[i]));
                Assert.AreEqual(NativeFormatToolbar.Commands[i], toolbar.CommandAt(
                    new Point(bounds[i].Left + 1, bounds[i].Top + 1), row));
                if (i > 0) Assert.IsGreaterThanOrEqualTo(bounds[i - 1].Right, bounds[i].Left);
            }
        }
    }

    [TestMethod]
    public void Cancel_ClearsPreviewAndPreservesCheckedState()
    {
        var previews = new List<string?>();
        var toolbar = new NativeFormatToolbar(96, c => new CommandState(true, c == AppCommand.ToggleBold), previews.Add);
        var row = new Rectangle(Point.Empty, toolbar.Size);
        var bold = toolbar.ButtonBounds(row, 0);
        toolbar.Hover(new Point(bold.Left + 1, bold.Top + 1), row);
        toolbar.ClearHover();

        CollectionAssert.AreEqual(new string?[] { "toggleBold", null }, previews);
        Assert.IsTrue(toolbar.States[0].IsChecked);
    }

    [TestMethod]
    public void KeyboardNavigation_SkipsDisabledButtonsAndDoesNotAutoPreviewOnOpen()
    {
        var previews = new List<string?>();
        var toolbar = new NativeFormatToolbar(144, c => new CommandState(c != AppCommand.ToggleItalic), previews.Add);
        Assert.IsEmpty(previews);
        Assert.AreEqual(-1, toolbar.HotIndex);
        toolbar.MoveFocus(1);
        toolbar.MoveFocus(1);
        Assert.AreEqual(2, toolbar.HotIndex);
        CollectionAssert.AreEqual(new string?[] { "toggleBold", "toggleUnderline" }, previews);
    }
}
