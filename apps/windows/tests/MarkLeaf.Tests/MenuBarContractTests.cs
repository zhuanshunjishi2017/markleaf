using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class MenuBarContractTests
{
    [TestMethod]
    public void FileMenuExposesCurrentFileOperations()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var menu = (nint)typeof(NativeMenuService).GetMethod("BuildFileMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var labels = MenuLabels(menu).ToArray();
            foreach (var expected in new[] { "复制文件路径", "复制文件内容", "在资源管理器中显示", "重命名…" })
                Assert.Contains(expected, labels);
            Assert.AreNotEqual(uint.MaxValue, GetMenuState(menu, (uint)AppCommand.CloseCurrentDocumentTab, 0));
        }
        finally { DestroyMenu(menu); }
    }

    [TestMethod]
    public void FileMenuUsesOneUnifiedExportEntry()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var menu = (nint)typeof(NativeMenuService).GetMethod("BuildFileMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var commands = Enumerable.Range(0, GetMenuItemCount(menu))
                .Select(index => GetMenuItemID(menu, index))
                .Where(id => id is > 0 and not 0xFFFFFFFF)
                .Select(id => (AppCommand)id)
                .ToArray();
            Assert.HasCount(1, commands.Where(x => x == AppCommand.ExportDocument).ToArray());
            Assert.HasCount(1, commands.Where(x => x == AppCommand.ExportWithLastSettings).ToArray());
            Assert.DoesNotContain(AppCommand.ExportPdf, commands);
            Assert.DoesNotContain(AppCommand.ExportHtml, commands);
            Assert.DoesNotContain(AppCommand.ExportImage, commands);
        }
        finally { DestroyMenu(menu); }
    }

    [TestMethod]
    public void RecentItemsWithDuplicateNamesShowParentDirectory()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts,
            () => new[] { @"C:\notes\a\README.md", @"C:\notes\b\README.md" },
            () => Array.Empty<string>(),
            () => "serif", () => 100, () => "light", () => false, () => true, () => false, () => "zh-CN",
            () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var root = (nint)typeof(NativeMenuService).GetMethod("BuildMainMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            typeof(NativeMenuService).GetMethod("RefreshRecentWorkspaces", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null);
            var recent = (nint)typeof(NativeMenuService).GetField("_recentWorkspaceMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            var labels = MenuLabels(recent).ToArray();
            Assert.Contains("1  README.md — a", labels);
            Assert.Contains("2  README.md — b", labels);
            Assert.IsFalse(labels.Any(label => label.Contains(@"C:\notes\", StringComparison.Ordinal)));
        }
        finally { DestroyMenu(root); }
    }

    [TestMethod]
    public void FormatImageSubmenuPutsResizeBeforeRotate()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var format = (nint)typeof(NativeMenuService).GetMethod("BuildFormatMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var image = FindSubMenu(format, "图片");
            Assert.AreNotEqual(nint.Zero, image);
            Assert.AreEqual((uint)AppCommand.ChangeImage, GetMenuItemID(image, 0));
            Assert.AreEqual((uint)AppCommand.EditImageCaption, GetMenuItemID(image, 1));
            Assert.AreEqual(0xFFFFFFFFu, GetMenuItemID(image, 2));
            Assert.AreEqual((uint)AppCommand.RotateImageClockwise, GetMenuItemID(image, 3));
            Assert.AreEqual((uint)AppCommand.SaveImageAs, GetMenuItemID(image, 4));
        }
        finally { DestroyMenu(format); }
    }

    [TestMethod]
    public void RecentMenusExposeTenFilesAndTenFoldersWithoutCommandCollisions()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }), shortcuts,
            () => Enumerable.Range(0, 10).Select(i => $@"C:\folders\{i}").ToArray(),
            () => Enumerable.Range(0, 10).Select(i => $@"C:\files\{i}.md").ToArray(),
            () => "serif", () => 100, () => "light", () => false, () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var root = (nint)typeof(NativeMenuService).GetMethod("BuildMainMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            typeof(NativeMenuService).GetMethod("RefreshRecentWorkspaces", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null);
            var recent = (nint)typeof(NativeMenuService).GetField("_recentWorkspaceMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            Assert.AreEqual(23, GetMenuItemCount(recent)); // two headers, separator and twenty entries
            var ids = Enumerable.Range(0, GetMenuItemCount(recent)).Select(i => GetMenuItemID(recent, i)).Where(id => id != 0 && id != uint.MaxValue).ToArray();
            Assert.HasCount(20, ids.Distinct().ToArray());
        }
        finally { DestroyMenu(root); }
    }
    private static string LocalesDirectory => Path.Combine(
        Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Locales");

    [STATestMethod]
    [DataRow("zh-CN", "插入(&I)")]
    [DataRow("en-US", "&Insert")]
    [DataRow("zh-TW", "插入(&I)")]
    [DataRow("ja-JP", "挿入(&I)")]
    public void VisibleMenuAndMnemonicUseInsert(string language, string expected)
    {
        Loc.Initialize(LocalesDirectory, language);
        try
        {
            using var tabs = new DocumentTabBar();
            var raw = (string[])typeof(DocumentTabBar).GetMethod("GetRawExpandedMenuLabels",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tabs, null)!;
            Assert.AreEqual(expected, raw[2]);
            tabs.SetMenuTextOptions(true, true, language);
            var displayed = (string[])typeof(DocumentTabBar).GetMethod("GetExpandedMenuLabels",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tabs, null)!;
            Assert.AreEqual(expected.Replace("&", ""), displayed[2]);
            tabs.SetFullScreenMenuVisible(true);
            Assert.IsTrue(tabs.TryGetMnemonicMenuIndex(System.Windows.Forms.Keys.I, out var index));
            Assert.AreEqual(2, index);
        }
        finally { Loc.Initialize(LocalesDirectory, "zh-CN"); }
    }

    [TestMethod]
    [DataRow("zh-CN")]
    [DataRow("en-US")]
    [DataRow("zh-TW")]
    [DataRow("ja-JP")]
    public void MainMenuHasNoUntranslatedEntries(string language)
    {
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(LocalesDirectory, language + ".json")))!;
        foreach (var key in new[] { "menu.insert.label", "menu.format.paragraphOperations",
            "menu.paragraph.duplicateParagraph", "menu.paragraph.deleteParagraph" })
            Assert.IsTrue(translations.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text),
                $"{language} must translate {key} without falling back to another language");
        Loc.Initialize(LocalesDirectory, language);
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var router = new CommandRouter(shortcuts, _ => new CommandState(true), _ => { });
        using var service = new NativeMenuService(router, shortcuts, () => [], () => [], () => "serif", () => 100,
            () => "light", () => false, () => true, () => false, () => language, () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var root = (nint)typeof(NativeMenuService).GetMethod("BuildMainMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            Assert.AreEqual(6, GetMenuItemCount(root));
            foreach (var label in MenuLabels(root))
                Assert.IsFalse(label.Contains('<') || label.Contains('>'), $"Untranslated {language} menu item: {label}");
        }
        finally { DestroyMenu(root); Loc.Initialize(LocalesDirectory, "zh-CN"); }
    }

    private static IEnumerable<string> MenuLabels(nint menu)
    {
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            var buffer = new StringBuilder(256);
            GetMenuString(menu, (uint)index, buffer, buffer.Capacity, 0x400);
            yield return buffer.ToString();
            var child = GetSubMenu(menu, index);
            if (child != 0)
                foreach (var label in MenuLabels(child)) yield return label;
        }
    }

    [TestMethod]
    public void RootOrderMatchesSixApprovedMenus()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var router = new CommandRouter(shortcuts, _ => new CommandState(true), _ => { });
        using var service = new NativeMenuService(router, shortcuts, () => [], () => [], () => "serif", () => 100,
            () => "light", () => false, () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var root = (nint)typeof(NativeMenuService).GetMethod("BuildMainMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var labels = Enumerable.Range(0, GetMenuItemCount(root)).Select(index => {
                var buffer = new StringBuilder(256);
                GetMenuString(root, (uint)index, buffer, buffer.Capacity, 0x400);
                return buffer.ToString().Replace("&", "");
            }).ToArray();
            Assert.HasCount(6, labels, string.Join(" | ", labels));
            CollectionAssert.AreEqual(new[] { "文件", "编辑", "插入", "格式", "视图", "帮助" }, labels);
        }
        finally { DestroyMenu(root); }
    }

    [TestMethod]
    public void BlockMenuUsesModernSessionFactory()
    {
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var router = new CommandRouter(shortcuts, _ => new CommandState(true), _ => { });
        using var service = new NativeMenuService(router, shortcuts, () => [], () => [], () => "serif", () => 100,
            () => "light", () => false, () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);

        var model = service.BuildBlockMenuModel(EditorCommandStatus.Empty with { InTable = true });

        Assert.IsEmpty(model.Primary);
        Assert.Contains(AppCommand.AddTableRowBefore,
            Flatten(model.Secondary).Select(x => x.Command!.GetValueOrDefault()).ToArray());
    }

    [TestMethod]
    public void EmptyWindowHelpAndClosedHistoryRemainEnabled()
    {
        var context = new CommandContext(DocumentAvailable: false, EditorReady: false, ClosedTabsAvailable: true);
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var router = new CommandRouter(shortcuts, command => CommandStateResolver.Resolve(command, context), _ => { });
        using var service = new NativeMenuService(router, shortcuts, () => [], () => [], () => "serif", () => 100,
            () => "light", () => false, () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var view = (nint)typeof(NativeMenuService).GetMethod("BuildViewMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        var help = (nint)typeof(NativeMenuService).GetMethod("BuildHelpMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var tabs = GetSubMenu(view, 3);
            Assert.AreEqual(0u, GetMenuState(tabs, (uint)AppCommand.ReopenClosedTab, 0) & 0x3u);
            Assert.AreNotEqual(0u, GetMenuState(tabs, (uint)AppCommand.SwitchToPreviousDocumentTab, 0) & 0x3u);
            Assert.AreEqual((uint)AppCommand.SwitchToPreviousDocumentTab, GetMenuItemID(tabs, 0));
            Assert.AreEqual((uint)AppCommand.SwitchToNextDocumentTab, GetMenuItemID(tabs, 1));
            Assert.AreEqual((uint)AppCommand.CloseCurrentDocumentTab, GetMenuItemID(tabs, 2));
            Assert.AreEqual((uint)AppCommand.CloseOtherDocumentTabs, GetMenuItemID(tabs, 3));
            Assert.AreEqual((uint)AppCommand.ReopenClosedTab, GetMenuItemID(tabs, 4));
            Assert.AreEqual(0xFFFFFFFFu, GetMenuItemID(tabs, 5));
            Assert.AreEqual(0xFFFFFFFFu, GetMenuItemID(tabs, 6));
            Assert.AreEqual(0u, GetMenuState(help, (uint)AppCommand.LearnMarkdown, 0) & 0x3u);
            Assert.AreEqual(0u, GetMenuState(help, (uint)AppCommand.OpenOnlineHelp, 0) & 0x3u);
            Assert.AreEqual(0u, GetMenuState(help, (uint)AppCommand.OpenSampleYamlBasic, 0) & 0x3u);
            Assert.AreEqual(0u, GetMenuState(help, (uint)AppCommand.ShowCodeFormatter, 0) & 0x3u);
        }
        finally
        {
            DestroyMenu(view);
            DestroyMenu(help);
        }
    }

    [TestMethod]
    public void DocumentTabsShowDirtyMarkerAndDisambiguatedDuplicateParents()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var tabs = new NativeMenuService.DocumentTabMenuEntry[]
        {
            new("README.md", @"C:\notes\a\README.md", IsDirty: true),
            new("README.md", @"C:\notes\b\README.md"),
        };
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => tabs, () => 0);
        var view = (nint)typeof(NativeMenuService).GetMethod("BuildViewMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var tabMenu = GetSubMenu(view, 3);
            var labels = MenuLabels(tabMenu).ToArray();
            CollectionAssert.Contains(labels, "● README.md — a\tAlt+1");
            CollectionAssert.Contains(labels, "README.md — b\tAlt+2");
        }
        finally { DestroyMenu(view); }
    }

    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));

    private static nint FindSubMenu(nint menu, string title)
    {
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            var child = GetSubMenu(menu, index);
            var buffer = new StringBuilder(256);
            GetMenuString(menu, (uint)index, buffer, buffer.Capacity, 0x400);
            if (buffer.ToString().Replace("&", "") == title) return child;
            var nested = FindSubMenu(child, title);
            if (nested != nint.Zero) return nested;
        }
        return nint.Zero;
    }

    private static IEnumerable<string> TopLevelMenuLabels(nint menu)
    {
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            var buffer = new StringBuilder(256);
            GetMenuString(menu, (uint)index, buffer, buffer.Capacity, 0x400);
            yield return buffer.ToString().Replace("&", "");
        }
    }

    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, int position);
    [DllImport("user32.dll")] private static extern nint GetSubMenu(nint menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuState(nint menu, uint id, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
    private static extern int GetMenuString(nint menu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
}
