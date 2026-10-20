using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorContextMenuDesktopTests
{
    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void CheckedMenuRowsPaintTheCustomCheckGlyph()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var owner = new Form { Text = "MarkLeaf checked menu verification", Size = new Size(700, 500) };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var session = new EditorMenuSession(1, _ => new CommandState(true),
            _ => Task.CompletedTask, _ => { }, () => { });
        var checkedRow = new EditorMenuEntry(
            EditorMenuEntryKind.Command, AppCommand.ToggleEditorFocusMode, "Paragraph Focus Mode", "F8",
            new(true, true), []);
        using var menu = new EditorContextMenu(new([], [checkedRow]), owner.DeviceDpi, false, true, session);
        menu.Show(owner, new Point(100, 100));
        PumpUntil(() => menu.Visible && menu.Opacity == 1);

        var row = menu.Items.OfType<ToolStripMenuItem>().Single();
        GetWindowRect(menu.Handle, out var bounds);
        using var bitmap = new Bitmap(menu.ClientSize.Width, menu.ClientSize.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(new Point(bounds.Left, bounds.Top), Point.Empty, bitmap.Size);
        var background = menu.BackColor.ToArgb();
        var y = Center(row.Bounds).Y;
        var checkColumn = Enumerable.Range(2, Math.Max(1, Scale(menu, 24) - 3))
            .Where(x => bitmap.GetPixel(x, y).ToArgb() != background)
            .ToArray();

        Assert.IsTrue(menu.ShowCheckMargin);
        Assert.IsGreaterThan(0, checkColumn.Length,
            $"The checked row must paint a glyph in its leading column. Row={row.Bounds}; Menu={menu.Bounds}.");
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow(false)]
    [DataRow(true)]
    public void PopupKeepsToolbarInsideWorkingAreaAndHoverRestoresOnClose(bool dark)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        LocTestHelper.EnsureInitialized();
        using var owner = new Form { Text = "MarkLeaf menu verification", Size = new Size(900, 750), StartPosition = FormStartPosition.CenterScreen };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var events = new List<string?>();
        var session = new EditorMenuSession(1, _ => new CommandState(true),
            _ => Task.CompletedTask, events.Add, () => events.Add("end"));
        var model = new EditorMenuModelBuilder(_ => new CommandState(true), _ => "Ctrl+Shift+Alt+K").Build(EditorCommandStatus.Empty);
        using var menu = new EditorContextMenu(model, owner.DeviceDpi, dark, true, session);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        var area = Screen.FromControl(owner).WorkingArea;
        menu.Show(new Point(area.Right - 3, area.Bottom - 3));
        PumpUntil(() => menu.Visible);
        Assert.IsTrue(area.Contains(menu.Bounds), $"Menu {menu.Bounds} must fit working area {area}.");
        var host = menu.Items.OfType<ToolStripControlHost>().Single();
        var toolbar = (ToolStrip)host.Control;
        var buttons = toolbar.Items.OfType<ToolStripButton>().ToArray();
        Assert.HasCount(7, buttons);
        Assert.IsTrue(buttons.All(button => button.Placement == ToolStripItemPlacement.Main
            && toolbar.ClientRectangle.Contains(button.Bounds)), "No quick action may overflow or be clipped.");
        var edgeInsets = new List<int>();
        foreach (var button in new[] { buttons[0], buttons[^1] })
        {
            Cursor.Position = toolbar.PointToScreen(Center(button.Bounds));
            PumpUntil(() => button.Selected && menu.Opacity == 1);
            var row = menu.RectangleToClient(toolbar.RectangleToScreen(button.Bounds));
            var insets = PaintedHoverInsets(menu, row);
            edgeInsets.Add(button == buttons[0] ? insets.Left : insets.Right);
            Save(menu, $"menu-{(dark ? "dark" : "light")}-{button.Name}.png");
        }
        Assert.IsLessThanOrEqualTo(1, Math.Abs(edgeInsets[0] - edgeInsets[1]),
            $"Toolbar hover must leave equal outer insets. Left={edgeInsets[0]}; Right={edgeInsets[1]}; Host={host.Bounds}; Toolbar={toolbar.Bounds}; Menu={menu.ClientRectangle}");
        Cursor.Position = toolbar.PointToScreen(Center(buttons[1].Bounds));
        PumpUntil(() => events.Contains("toggleItalic"));
        Assert.IsTrue(buttons[1].Selected, "Hover must visibly select the button.");
        Save(menu, dark ? "menu-dark-hover.png" : "menu-light-hover.png");

        var cut = menu.Items.OfType<ToolStripMenuItem>().First(item => item.Name == nameof(AppCommand.Cut));
        Assert.AreEqual(cut.Bounds.Left, menu.ClientSize.Width - cut.Bounds.Right,
            $"Menu rows must leave equal side insets. Client={menu.ClientRectangle}; Padding={menu.Padding}; Row={cut.Bounds}");
        Cursor.Position = menu.PointToScreen(Center(cut.Bounds));
        PumpUntil(() => cut.Selected && menu.Opacity == 1);
        AssertHoverInsets(menu, cut);
        Save(menu, dark ? "menu-dark-row.png" : "menu-light-row.png");

        var submenu = menu.Items.OfType<ToolStripMenuItem>().Last(item => item.HasDropDownItems);
        Cursor.Position = menu.PointToScreen(Center(submenu.Bounds));
        PumpUntil(() => submenu.DropDown.Visible);
        Assert.IsTrue(area.Contains(submenu.DropDown.Bounds), "A long submenu must also fit the screen working area.");
        Assert.IsTrue(submenu.DropDown.Items.Cast<ToolStripItem>().All(item => item.Bounds.Right <= submenu.DropDown.ClientSize.Width),
            "A submenu must reserve the full painted width of its labels and shortcuts.");
        foreach (var row in submenu.DropDown.Items.OfType<ToolStripMenuItem>())
            Assert.AreEqual(row.Bounds.Left, submenu.DropDown.ClientSize.Width - row.Bounds.Right,
                "Submenu rows must use the same symmetric layout as the root menu.");
        Assert.IsNull(events[^1], "Moving to a regular menu item must clear temporary formatting.");
        Save(menu, dark ? "menu-dark-submenu.png" : "menu-light-submenu.png");
        // Closing a child must not release the root editor selection.
        submenu.HideDropDown();
        PumpUntil(() => !submenu.DropDown.Visible);
        Assert.IsFalse(session.Completion.IsCompleted);
        SendKeys.SendWait("{ESC}");
        PumpUntil(() => session.Completion.IsCompleted);
        Assert.AreEqual("end", events[^1]);
        Assert.IsFalse(session.HasCommitted);
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void KeyboardCanEnterQuickActionsAndCommitOnce()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var owner = new Form { Text = "MarkLeaf keyboard verification", Size = new Size(700, 600) };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var commands = new List<AppCommand>();
        var session = new EditorMenuSession(1, _ => new CommandState(true),
            command => { commands.Add(command); return Task.CompletedTask; }, _ => {}, () => {});
        var model = new EditorMenuModelBuilder(_ => new CommandState(true), _ => "").Build(EditorCommandStatus.Empty);
        using var menu = new EditorContextMenu(model, owner.DeviceDpi, false, true, session);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        Cursor.Position = owner.PointToScreen(new Point(20, 20));
        menu.Show(owner, new Point(100, 100));
        SendKeys.SendWait("{HOME}{RIGHT}");
        var toolbar = (ToolStrip)menu.Items.OfType<ToolStripControlHost>().Single().Control;
        PumpUntil(() => toolbar.Items.OfType<ToolStripButton>().Any(button => button.Selected));
        var selected = toolbar.Items.OfType<ToolStripButton>().Single(button => button.Selected);
        SendKeys.SendWait("{ENTER}");
        PumpUntil(() => session.Completion.IsCompleted);
        PumpUntil(() => menu.Dismissed.IsCompleted);
        Assert.HasCount(1, commands);
        Assert.AreEqual(selected.Name, commands[0].ToString());
        Assert.IsFalse(menu.Visible);
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void MouseClickCommitsQuickActionOnce()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var owner = new Form { Text = "MarkLeaf quick-action mouse verification", Size = new Size(700, 600), StartPosition = FormStartPosition.CenterScreen };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var commands = new List<AppCommand>();
        var session = new EditorMenuSession(1, _ => new CommandState(true),
            command => { commands.Add(command); return Task.CompletedTask; }, _ => { }, () => { });
        var model = new EditorMenuModelBuilder(_ => new CommandState(true), _ => "").Build(EditorCommandStatus.Empty);
        using var menu = new EditorContextMenu(model, owner.DeviceDpi, false, true, session, animate: true);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        Cursor.Position = owner.PointToScreen(new Point(20, 20));
        menu.Show(owner, new Point(100, 100));
        PumpUntil(() => menu.Visible && menu.Opacity == 1);
        var toolbar = (ToolStrip)menu.Items.OfType<ToolStripControlHost>().Single().Control;
        var button = toolbar.Items.OfType<ToolStripButton>().First(item => item.Enabled);
        Cursor.Position = toolbar.PointToScreen(Center(button.Bounds));
        PumpUntil(() => button.Selected);
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
        PumpUntil(() => session.Completion.IsCompleted);
        PumpUntil(() => menu.Dismissed.IsCompleted);
        Assert.HasCount(1, commands);
        Assert.AreEqual(button.Name, commands[0].ToString());
        Assert.IsFalse(menu.Visible);
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void FadePreservesCloseAndCommandLifetime()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var owner = new Form { Size = new Size(700, 600) };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var executions = 0;
        var session = new EditorMenuSession(1, _ => new CommandState(true),
            _ => { executions++; return Task.CompletedTask; }, _ => {}, () => {});
        var model = new EditorMenuModelBuilder(_ => new CommandState(true), _ => "").Build(EditorCommandStatus.Empty);
        using var menu = new EditorContextMenu(model, owner.DeviceDpi, false, true, session, animate: true);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        menu.Show(owner, new Point(100, 100));
        Assert.IsLessThan(1d, menu.Opacity, "Opening must begin below full opacity.");
        Assert.IsTrue(GetLayeredWindowAttributes(menu.Handle, out _, out var alpha, out _), "Windows must receive the fade opacity.");
        Assert.IsLessThan((byte)255, alpha);
        PumpUntil(() => menu.Opacity == 1);
        menu.Items.OfType<ToolStripMenuItem>().First(item => !item.HasDropDownItems).PerformClick();
        Assert.AreEqual(1, executions);
        Assert.IsFalse(menu.Dismissed.IsCompleted, "The visual close must finish before disposing the popup.");
        PumpUntil(() => menu.Opacity < 1);
        PumpUntil(() => menu.Dismissed.IsCompleted);
        Assert.IsFalse(menu.Visible);
        Assert.AreEqual(1, executions);
        // Reopening a submenu instance must reset its animation and close task.
        menu.Show(owner, new Point(100, 100));
        Assert.IsFalse(menu.Dismissed.IsCompleted);
        menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        Assert.IsTrue(menu.Dismissed.IsCompleted, "Programmatic cancellation must not leave an animated popup behind.");
        Assert.IsFalse(menu.Visible);
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow(false, 1)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    public void OneOutsideClickDismissesEntireSubmenuChain(bool animate, int depth)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var owner = new Form { Size = new Size(900, 750), StartPosition = FormStartPosition.CenterScreen };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var clicks = 0;
        owner.MouseDown += (_, _) => clicks++;
        var ended = 0;
        var executions = 0;
        var session = new EditorMenuSession(1, _ => new CommandState(true),
            _ => { executions++; return Task.CompletedTask; }, _ => {}, () => ended++);
        EditorMenuEntry entry = new(EditorMenuEntryKind.Command, AppCommand.Copy, "Copy", "", new(true), []);
        for (var i = 0; i < depth; i++)
            entry = new(EditorMenuEntryKind.Submenu, null, "Submenu", "", new(true), [entry]);
        using var menu = new EditorContextMenu(new([], [entry]), owner.DeviceDpi, false, true, session, animate);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        var popups = new List<EditorContextMenu> { menu };
        menu.Show(owner, new Point(40, 40));
        for (var i = 0; i < depth; i++)
        {
            var parent = popups[^1];
            PumpUntil(() => parent.Opacity == 1);
            var item = parent.Items.OfType<ToolStripMenuItem>().Single();
            item.ShowDropDown();
            popups.Add((EditorContextMenu)item.DropDown);
        }
        PumpUntil(() => popups[^1].Opacity == 1);
        Cursor.Position = owner.PointToScreen(new Point(30, owner.ClientSize.Height - 30));
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
        PumpUntil(() => popups.All(popup => !popup.Visible));
        PumpUntil(() => session.Completion.IsCompleted);
        Assert.AreEqual(1, ended, "The whole menu chain owns one editor selection lifetime.");
        Assert.AreEqual(0, executions);
        Assert.AreEqual(1, clicks, "Dismissal must not swallow or replay the outside click.");
        Assert.IsTrue(popups.All(popup => popup.Dismissed.IsCompleted));
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void SwitchingToAnotherWindowDismissesEntireSubmenuChain()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var other = new Form { Size = new Size(700, 600) };
        other.Show();
        using var owner = new Form { Size = new Size(900, 750), StartPosition = FormStartPosition.CenterScreen };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var session = new EditorMenuSession(1, _ => new CommandState(true), _ => Task.CompletedTask, _ => {}, () => {});
        var copy = new EditorMenuEntry(EditorMenuEntryKind.Command, AppCommand.Copy, "Copy", "", new(true), []);
        var branch = new EditorMenuEntry(EditorMenuEntryKind.Submenu, null, "Submenu", "", new(true), [copy]);
        using var menu = new EditorContextMenu(new([], [branch]), owner.DeviceDpi, false, true, session, animate: true);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        menu.Show(owner, new Point(40, 40));
        var item = menu.Items.OfType<ToolStripMenuItem>().Single();
        item.ShowDropDown();
        var child = (EditorContextMenu)item.DropDown;
        PumpUntil(() => child.Opacity == 1 && menu.Opacity == 1);
        SetForegroundWindow(other.Handle);
        PumpUntil(() => !menu.Visible && !child.Visible);
        PumpUntil(() => session.Completion.IsCompleted);
        Assert.IsFalse(session.HasCommitted);
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow(false)]
    [DataRow(true)]
    public void ClosingChildWithinMenuKeepsRootSessionAlive(bool clickAncestor)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        using var owner = new Form { Size = new Size(900, 750), StartPosition = FormStartPosition.CenterScreen };
        owner.Show();
        SetForegroundWindow(owner.Handle);
        var session = new EditorMenuSession(1, _ => new CommandState(true), _ => Task.CompletedTask, _ => {}, () => {});
        var copy = new EditorMenuEntry(EditorMenuEntryKind.Command, AppCommand.Copy, "Copy", "", new(true), []);
        var branch = new EditorMenuEntry(EditorMenuEntryKind.Submenu, null, "Submenu", "", new(true), [copy]);
        var disabled = new EditorMenuEntry(EditorMenuEntryKind.Command, AppCommand.Cut, "Disabled", "", new(false), []);
        using var menu = new EditorContextMenu(new([], [branch, disabled]), owner.DeviceDpi, false, true, session, animate: true);
        menu.Closed += (_, _) => session.Closed(callback => owner.BeginInvoke(callback));
        menu.Show(owner, new Point(40, 40));
        var item = menu.Items.OfType<ToolStripMenuItem>().First();
        item.ShowDropDown();
        var child = (EditorContextMenu)item.DropDown;
        PumpUntil(() => child.Opacity == 1 && menu.Opacity == 1);
        if (clickAncestor)
        {
            Cursor.Position = menu.PointToScreen(Center(menu.Items[^1].Bounds));
            mouse_event(0x0002, 0, 0, 0, 0);
            mouse_event(0x0004, 0, 0, 0, 0);
        }
        else SendKeys.SendWait("{ESC}");
        PumpUntil(() => !child.Visible);
        Assert.IsTrue(menu.Visible, "Only an outside click may dismiss the whole menu chain.");
        Assert.IsFalse(session.Completion.IsCompleted, "Closing a child must not release the editor selection.");
        menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        PumpUntil(() => session.Completion.IsCompleted);
    }

    private static Point Center(Rectangle rectangle) => new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

    private static int Scale(Control control, int value) =>
        (int)Math.Round(value * control.DeviceDpi / 96.0);

    private static void AssertHoverInsets(EditorContextMenu menu, ToolStripMenuItem row)
    {
        var (left, right) = PaintedHoverInsets(menu, row.Bounds);
        Assert.AreEqual(left, right, $"Painted hover insets: left={left}, right={right}.");
    }

    private static (int Left, int Right) PaintedHoverInsets(EditorContextMenu menu, Rectangle row)
    {
        var hover = ((EditorMenuRenderer)menu.Renderer).Theme.Hover.ToArgb();
        var y = Center(row).Y;
        var left = 0;
        var right = 0;
        // Selected is updated before DWM necessarily presents the new pixels.
        // Wait for the actual hover paint, then measure it without relaxing the insets.
        PumpUntil(() =>
        {
            menu.Refresh();
            GetWindowRect(menu.Handle, out var bounds);
            using var bitmap = new Bitmap(menu.ClientSize.Width, menu.ClientSize.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(new Point(bounds.Left, bounds.Top), Point.Empty, bitmap.Size);
            var pixels = Enumerable.Range(0, bitmap.Width).Where(x => bitmap.GetPixel(x, y).ToArgb() == hover).ToArray();
            if (pixels.Length == 0) return false;
            left = pixels[0];
            right = bitmap.Width - 1 - pixels[^1];
            return true;
        });
        return (left, right);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
        Assert.IsTrue(condition(), "Timed out waiting for a real menu event.");
        Application.DoEvents();
    }

    private static void Save(Control menu, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MARKLEAF_MENU_TEST_SCREENSHOTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        menu.Update();
        PumpUntil(() => ((ToolStripDropDown)menu).Opacity == 1);
        GetWindowRect(menu.Handle, out var native);
        var area = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        foreach (var child in ((ToolStrip)menu).Items.OfType<ToolStripMenuItem>().Where(item => item.DropDown.Visible))
        {
            PumpUntil(() => child.DropDown.Opacity == 1);
            child.DropDown.Update();
            GetWindowRect(child.DropDown.Handle, out var childBounds);
            area = Rectangle.Union(area, Rectangle.FromLTRB(childBounds.Left, childBounds.Top, childBounds.Right, childBounds.Bottom));
            File.WriteAllText(Path.Combine(directory, name + ".child.txt"), $"Menu={child.DropDown.Bounds}; Font={child.DropDown.Font}; "
                + string.Join("; ", child.DropDown.Items.Cast<ToolStripItem>().Select(item => $"{item.Text}:{item.Bounds}")));
        }
        File.WriteAllText(Path.Combine(directory, name + ".txt"), $"DeviceDpi={menu.DeviceDpi}; Bounds={menu.Bounds}; Native={area}; Screen={Screen.FromControl(menu).Bounds}");
        using var bitmap = new Bitmap(area.Width, area.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
        bitmap.Save(Path.Combine(directory, name));
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);

    private sealed class DpiScope : IDisposable
    {
        private readonly nint _previous = SetThreadDpiAwarenessContext(-4);
        public void Dispose() => SetThreadDpiAwarenessContext(_previous);
    }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);
}
