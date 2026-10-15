using System.Windows.Forms;
using MarkLeaf.Commands;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorContextMenuTests
{
    [STATestMethod]
    public void HighContrastCheckedQuickActionKeepsItsGlyphVisibleWithoutHover()
    {
        var theme = new ModernMenuTheme(SystemColors.Menu, SystemColors.Highlight, SystemColors.MenuText,
            SystemColors.MenuText, SystemColors.MenuText, SystemColors.GrayText, SystemColors.Highlight);
        using var button = new ToolStripButton { Checked = true };
        var foreground = EditorMenuRenderer.TextColor(button, theme, highContrast: true);
        Assert.AreEqual(SystemColors.HighlightText, foreground);
        Assert.AreNotEqual(theme.Hover, foreground, "An active format glyph must contrast with its checked background.");
        button.Enabled = false;
        Assert.AreEqual(theme.DisabledText, EditorMenuRenderer.TextColor(button, theme, highContrast: true));
    }

    [STATestMethod]
    public void ToolbarAndSubmenusShareCommandSessionAndDisabledCommandsCannotCommit()
    {
        var events = new List<string>();
        var session = Session(events);
        using var menu = CreateMenu(144, session);
        var toolbar = (ToolStrip)menu.Items.OfType<ToolStripControlHost>().Single().Control;
        var buttons = toolbar.Items.OfType<ToolStripButton>().ToArray();
        Assert.HasCount(2, buttons);
        Assert.IsTrue(buttons[0].Checked);
        Assert.AreEqual("Bold", buttons[0].AccessibleName);
        var submenu = menu.Items.OfType<ToolStripMenuItem>().Single();
        var disabled = (ToolStripMenuItem)submenu.DropDownItems[0];
        Assert.IsFalse(disabled.Enabled);
        disabled.PerformClick();
        Assert.IsFalse(session.HasCommitted);
        var copy = (ToolStripMenuItem)submenu.DropDownItems[1];
        Assert.AreEqual("Ctrl+C", copy.ShortcutKeyDisplayString);
        copy.PerformClick();
        CollectionAssert.AreEqual(new[] { "preview:", "execute:Copy", "end" }, events);
    }

    [STATestMethod]
    public void ToolbarClickCommitsOnceAndRejectsLatePreview()
    {
        var events = new List<string>();
        var session = Session(events);
        using var menu = CreateMenu(96, session);
        var toolbar = (ToolStrip)menu.Items.OfType<ToolStripControlHost>().Single().Control;
        var italic = toolbar.Items.OfType<ToolStripButton>().Last();
        italic.PerformClick();
        italic.PerformClick();
        session.Preview("toggleBold");
        CollectionAssert.AreEqual(new[] { "preview:", "execute:ToggleItalic", "end" }, events);
        Assert.IsTrue(session.HasCommitted);
    }

    [STATestMethod]
    [DataRow(96)]
    [DataRow(144)]
    [DataRow(192)]
    [DataRow(216)]
    public void LayoutKeepsEqualButtonsInsideOneToolbar(int dpi)
    {
        using var menu = CreateMenu(dpi, Session([]));
        menu.PerformLayout();
        var host = menu.Items.OfType<ToolStripControlHost>().Single();
        var toolbar = (ToolStrip)host.Control;
        toolbar.PerformLayout();
        var buttons = toolbar.Items.OfType<ToolStripButton>().ToArray();
        Assert.AreEqual(buttons[0].Size, buttons[1].Size);
        Assert.AreEqual(buttons[0].Bounds.Top, buttons[1].Bounds.Top);
        Assert.IsTrue(buttons.All(button => toolbar.ClientRectangle.Contains(button.Bounds)),
            "The menu must reserve space for the complete quick-action row.");
        var left = toolbar.Left + buttons[0].Bounds.Left;
        var right = menu.ClientSize.Width - toolbar.Left - buttons[^1].Bounds.Right;
        Assert.IsLessThanOrEqualTo(1, Math.Abs(left - right),
            $"Quick actions must be centered in the popup. Left={left}; Right={right}; Host={host.Bounds}; Toolbar={toolbar.Bounds}; Menu={menu.ClientRectangle}");
        Assert.DoesNotContain("Icons", menu.Font.Name, "Menu text must not use the icon font.");
    }

    private static EditorMenuSession Session(List<string> events) => new(1,
        command => new CommandState(command != AppCommand.Cut),
        command => { events.Add("execute:" + command); return Task.CompletedTask; },
        command => events.Add("preview:" + command), () => events.Add("end"));

    private static ToolStripDropDownMenu CreateMenu(int dpi, EditorMenuSession session)
    {
        EditorMenuEntry Command(AppCommand command, string label, string shortcut = "", bool enabled = true, bool check = false) =>
            new(EditorMenuEntryKind.Command, command, label, shortcut, new(enabled, check), []);
        var model = new EditorMenuModel(
            [Command(AppCommand.ToggleBold, "Bold", "Ctrl+B", check: true), Command(AppCommand.ToggleItalic, "Italic")],
            [new(EditorMenuEntryKind.Submenu, null, "Clipboard", "", new(true),
                [Command(AppCommand.Cut, "Cut", enabled: false), Command(AppCommand.Copy, "Copy", "Ctrl+C")])]);
        return new EditorContextMenu(model, dpi, false, true, session);
    }

}
