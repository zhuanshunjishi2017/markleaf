using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MarkLeaf.Commands;
using MarkLeaf.Native;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class NativeFormatMenuSessionTests
{
    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void NativePopup_RowStaysInsideMenuAtScreenEdgeAndHoverCanCommit()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires an interactive Windows desktop, not an SSH service session.");
        var previews = new List<string?>();
        var diagnostics = new List<string>();
        using var owner = new MenuOwner();
        owner.Show();
        var menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, NativeMethods.MfString, (nuint)AppCommand.Copy, "Copy");
        using var session = new NativeFormatMenuSession(owner.Handle, menu, owner.DeviceDpi, false,
            _ => new CommandState(true), previews.Add, diagnostics.Add, ownerDraw: null, quickActions: true);
        owner.Session = session;
        Exception? failure = null;
        var exercised = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 300 };
        var deadline = DateTime.UtcNow.AddSeconds(3);
        timer.Tick += (_, _) =>
        {
            try
            {
                if (!GetMenuItemRect(owner.Handle, menu, 0, out var rect))
                {
                    if (DateTime.UtcNow < deadline) return;
                    Assert.Fail("Native popup did not expose its toolbar rectangle.");
                }
                timer.Stop();
                var row = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                Assert.IsTrue(Screen.GetWorkingArea(row).Contains(row));
                var model = new NativeFormatToolbar(owner.DeviceDpi, _ => new CommandState(true), _ => { });
                var button = model.ButtonBounds(row, 1);
                var point = new Point(button.Left + button.Width / 2, button.Top + button.Height / 2);
                var drawCountBeforeHover = owner.DrawCount;
                Filter(0x0200, point); // actual menu hook, not a mocked callback
                Assert.IsGreaterThan(drawCountBeforeHover, owner.DrawCount,
                    "Hover must repaint the native button row. " + string.Join("; ", diagnostics));
                CollectionAssert.AreEqual(new string?[] { "toggleItalic" }, previews);
                Filter(0x0201, point);
                Filter(0x0202, point);
                exercised = true;
            }
            catch (Exception error) { failure = error; NativeMethods.EndMenu(); }
        };
        timer.Start();
        try
        {
            var area = Screen.GetWorkingArea(owner);
            NativeMethods.SetForegroundWindow(owner.Handle);
            NativeMethods.TrackPopupMenuEx(menu, NativeMethods.TpmReturnCommand | 0x4000 /* TPM_NOANIMATION */,
                area.Right - 5, area.Bottom - 5, owner.Handle, 0);
            if (failure is not null) throw failure;
            Assert.IsTrue(exercised);
            Assert.IsGreaterThan(0, owner.DrawCount);
            Assert.AreEqual(AppCommand.ToggleItalic, session.SelectedCommand);
            CollectionAssert.AreEqual(new string?[] { "toggleItalic", null }, previews);
        }
        finally
        {
            owner.Session = null;
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void Filter(uint kind, Point point)
    {
        var message = new MenuMessage { Message = kind, X = point.X, Y = point.Y };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<MenuMessage>());
        try
        {
            Marshal.StructureToPtr(message, memory, false);
            CallMsgFilter(memory, 2);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private sealed class MenuOwner : Form
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal NativeFormatMenuSession? Session { get; set; }
        internal int DrawCount { get; private set; }
        protected override void WndProc(ref Message message)
        {
            if (Session?.HandleMessage(ref message) == true)
            {
                if (message.Msg == 0x002B) DrawCount++;
                return;
            }
            base.WndProc(ref message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll")]
    private static extern bool GetMenuItemRect(nint owner, nint menu, uint item, out MenuBarDarkMode.Rect rect);
    [DllImport("user32.dll", EntryPoint = "CallMsgFilterW")]
    private static extern bool CallMsgFilter(nint message, int code);
}
