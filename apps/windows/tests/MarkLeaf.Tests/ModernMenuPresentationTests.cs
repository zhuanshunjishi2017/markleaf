using MarkLeaf.Native;
using MarkLeaf.Commands;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ModernMenuPresentationTests
{
    [TestMethod]
    public void MetricsScaleForDisplayDpi()
    {
        var metrics = ModernMenuMetrics.ForDpi(144);

        Assert.AreEqual(51, metrics.ItemHeight);
        Assert.AreEqual(21, metrics.SeparatorHeight);
        Assert.AreEqual(48, metrics.ToolbarButtonSize);
        Assert.AreEqual(9, metrics.CornerRadius);
    }

    [TestMethod]
    public void ThemeFollowsRequestedMode()
    {
        var light = ModernMenuTheme.Create(dark: false);
        var dark = ModernMenuTheme.Create(dark: true);

        Assert.IsLessThan(50, dark.Background.R);
        Assert.AreNotEqual(light.Background, dark.Background);
        Assert.IsTrue(dark.Background.R == dark.Background.G && dark.Background.G == dark.Background.B);
        Assert.AreNotEqual(light.Hover, dark.Hover);
        Assert.AreNotEqual(light.Text, dark.Text);
    }

    [TestMethod]
    public void HoverRectangleIsInsetAndRounded()
    {
        var metrics = ModernMenuMetrics.ForDpi(96);
        var rectangle = metrics.HoverBounds(new Rectangle(0, 0, 260, 34));

        Assert.AreEqual(4, rectangle.Left);
        Assert.AreEqual(4, rectangle.Top);
        Assert.AreEqual(252, rectangle.Width);
        Assert.AreEqual(26, rectangle.Height);
    }

    [TestMethod]
    public void OwnerDrawMenuPreservesCommandTreeMetadata()
    {
        var enabled = new CommandState(true);
        var submenuChild = new EditorMenuEntry(EditorMenuEntryKind.Command, AppCommand.ToggleItalic,
            "Italic", "Ctrl+I", enabled, Array.Empty<EditorMenuEntry>());
        var submenu = new EditorMenuEntry(EditorMenuEntryKind.Submenu, null, "Format", "", enabled,
            new[] { submenuChild });
        var command = new EditorMenuEntry(EditorMenuEntryKind.Command, AppCommand.ToggleBold,
            "Bold", "Ctrl+B", enabled, Array.Empty<EditorMenuEntry>());
        var model = new EditorMenuModel(Array.Empty<EditorMenuEntry>(),
            new EditorMenuEntry[] { EditorMenuEntry.Separator, command, submenu });

        using var builder = new ModernMenuBuilder();
        var menu = builder.Build(model, showShortcuts: true);

        Assert.AreNotEqual(IntPtr.Zero, menu);
        Assert.IsTrue(builder.TryGetByCommand(AppCommand.ToggleBold, out var bold));
        Assert.AreEqual("Bold", bold.Label);
        Assert.AreEqual("Ctrl+B", bold.Shortcut);
        Assert.IsFalse(bold.State.IsChecked);
        Assert.IsTrue(builder.TryGetByCommand(AppCommand.ToggleItalic, out var italic));
        Assert.AreEqual("Italic", italic.Label);
    }
}
