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
    public void CloseActionConsistentlyNamesTheCurrentTab()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var close = service.BuildSelfDrawnTopLevelMenu(0).Secondary.Single(item =>
            item.RawCommandId == (uint)AppCommand.CloseCurrentDocumentTab);
        Assert.AreEqual("关闭当前标签页", close.Label);
        var tabMenu = service.BuildSelfDrawnTopLevelMenu(4).Secondary
            .Single(item => item.Label == "标签页管理");
        Assert.AreEqual("关闭当前标签页", tabMenu.Children.Single(item =>
            item.RawCommandId == (uint)AppCommand.CloseCurrentDocumentTab).Label);
    }

    [TestMethod]
    public void FormatGroupsReflectTheCheckedDescendantAndClearWhenItChanges()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        AppCommand? checkedCommand = AppCommand.SetHeading2;
        using var service = new NativeMenuService(new CommandRouter(shortcuts,
            command => new(true, command == checkedCommand), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        foreach (var current in new AppCommand?[] { AppCommand.SetHeading2, AppCommand.SetParagraph, AppCommand.ToggleBulletList, null })
        {
            checkedCommand = current;
            var entries = service.BuildSelfDrawnTopLevelMenu(3).Secondary;
            var paragraph = entries.Single(item => item.Label == "段落样式");
            var heading = paragraph.Children.Single(item => item.Label == "标题");
            var list = entries.Single(item => item.Label == "列表");
            Assert.AreEqual(current is AppCommand.SetHeading2 or AppCommand.SetParagraph, paragraph.State.IsChecked);
            Assert.AreEqual(current == AppCommand.SetHeading2, heading.State.IsChecked);
            Assert.AreEqual(current == AppCommand.ToggleBulletList, list.State.IsChecked);
        }
    }

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
            Assert.DoesNotContain(AppCommand.ExportWithLastSettings, commands);
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
    public void InsertMenuMatchesMacGroupingAndWording()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => false, () => true, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var insert = (nint)typeof(NativeMenuService).GetMethod("BuildInsertMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            var labels = Enumerable.Range(0, GetMenuItemCount(insert)).Select(index =>
            {
                var buffer = new StringBuilder(256);
                GetMenuString(insert, (uint)index, buffer, buffer.Capacity, 0x400);
                return buffer.ToString();
            }).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "图片(&I)",
                "插入表格(&I)",
                "插入 Mermaid 图表(&M)",
                "插入超链接(&K)...",
                "插入脚注(&F)",
                string.Empty,
                "行内公式(&M)",
                "块级公式(&M)",
                "水平线(&R)",
                string.Empty,
                "YAML 前置元数据(&Y)",
            }, labels);
            Assert.AreEqual(0xFFFFFFFFu, GetMenuItemID(insert, 0), "首项应为图片子菜单");
            Assert.AreEqual(0u, GetMenuItemID(insert, 5), "第一组与公式之间应有分隔线");
            Assert.AreEqual(0u, GetMenuItemID(insert, 9), "公式与 YAML 元数据之间应有分隔线");
        }
        finally { DestroyMenu(insert); }
    }

    [TestMethod]
    public void PopupContainerFollowsDescendantAvailability()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => false, () => true, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var file = (nint)typeof(NativeMenuService).GetMethod("BuildFileMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            Assert.AreEqual(0xFFFFFFFFu, GetMenuItemID(file, 0), "首项应为“新建”子菜单");
            NativeMenuService.RefreshPopupAvailability(file);
            Assert.AreEqual(0u, GetMenuState(file, 0, ByPosition) & 0x1u, "有可用命令时容器应保持可用");

            DisableAllCommands(file);
            NativeMenuService.RefreshPopupAvailability(file);
            Assert.AreEqual(0x1u, GetMenuState(file, 0, ByPosition) & 0x1u, "所有命令禁用后容器应置灰");

            EnableMenuItem(file, (uint)AppCommand.NewDocument, 0);
            NativeMenuService.RefreshPopupAvailability(file);
            Assert.AreEqual(0u, GetMenuState(file, 0, ByPosition) & 0x1u, "恢复任一命令后容器应重新可用");
        }
        finally { DestroyMenu(file); }
    }

    [TestMethod]
    public void SelfDrawnMenuModelKeepsNativeContentAndCommandIds()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var file = service.BuildSelfDrawnTopLevelMenu(0);
        var labels = file.Secondary.Select(entry => entry.Label).ToArray();
        CollectionAssert.Contains(labels, "新建");
        CollectionAssert.Contains(labels, "保存");
        Assert.IsFalse(labels.Any(label => label.Contains('&')), "自绘菜单不应显示助记键标记");
        Assert.IsTrue(file.Secondary.Any(entry => entry.Kind == EditorMenuEntryKind.Separator),
            "文件菜单应保留分组分隔线");

        var newDocument = file.Secondary.First(entry => entry.Label == "新建");
        Assert.AreEqual(EditorMenuEntryKind.Submenu, newDocument.Kind);
        CollectionAssert.Contains(newDocument.Children.Select(entry => entry.Label).ToArray(), "Markdown");

        var save = file.Secondary.First(entry => entry.Label == "保存");
        Assert.AreEqual((uint?)AppCommand.SaveDocument, save.RawCommandId,
            "选中项必须携带原始命令 ID 才能走现有的命令分发");
        Assert.AreEqual("Ctrl+S", save.Shortcut);
    }

    [TestMethod]
    public void SelfDrawnMenuModelDisablesContainerWhenAllChildrenAreUnavailable()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new CommandState(false), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var file = service.BuildSelfDrawnTopLevelMenu(0);
        var newDocument = file.Secondary.First(entry => entry.Label == "新建");
        Assert.AreEqual(EditorMenuEntryKind.Submenu, newDocument.Kind);
        Assert.IsFalse(newDocument.State.IsEnabled, "所有子命令禁用时，外层子菜单项也应置灰");
    }

    [TestMethod]
    public void SelfDrawnViewMenuCarriesModeChecksFromRouter()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var router = new CommandRouter(shortcuts, command => new CommandState(true,
            command is AppCommand.ToggleEditorFocusMode
                or AppCommand.ToggleEditorTypewriterMode
                or AppCommand.ToggleEditorFullScreen), _ => { });
        using var service = new NativeMenuService(router,
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);

        var view = service.BuildSelfDrawnTopLevelMenu(4);
        var modes = Flatten(view.Secondary)
            .Where(entry => entry.Label is "段落专注模式" or "打字机模式" or "全屏幕模式")
            .ToArray();

        Assert.HasCount(3, modes);
        Assert.IsTrue(modes.All(entry => entry.State.IsChecked),
            "自绘视图菜单必须把宿主模式状态传到子菜单项。");
    }

    [TestMethod]
    [DataRow("zh-CN", "全屏幕模式(&F)\tF11")]
    [DataRow("zh-TW", "全螢幕模式(&F)\tF11")]
    [DataRow("en-US", "&Full Screen Mode\tF11")]
    [DataRow("ja-JP", "フルスクリーンモード(&F)\tF11")]
    public void ViewMenuUsesFullScreenModeWording(string language, string expected)
    {
        Loc.Initialize(LocalesDirectory, language);
        try
        {
            Assert.AreEqual(expected, Loc.Get("menu.view.editorFullScreen"));
        }
        finally { Loc.Initialize(LocalesDirectory, "zh-CN"); }
    }

    [TestMethod]
    public void MenuBarTitlesStayEnabledWhileNestedPopupsCollapse()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        using var service = new NativeMenuService(new CommandRouter(shortcuts, _ => new(true), _ => { }),
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => false, () => true, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
        var root = (nint)typeof(NativeMenuService).GetMethod("BuildMainMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, null)!;
        try
        {
            DisableAllCommands(root);
            NativeMenuService.RefreshMenuBarPopupAvailability(root);
            Assert.AreEqual(0u, GetMenuState(root, 0, ByPosition) & 0x1u, "菜单栏顶级项应始终可展开");
            var file = GetSubMenu(root, 0);
            Assert.AreEqual(0x1u, GetMenuState(file, 0, ByPosition) & 0x1u, "嵌套子菜单无可用命令时应置灰");
        }
        finally { DestroyMenu(root); }
    }

    private const uint ByPosition = 0x400;

    private static void DisableAllCommands(nint menu)
    {
        for (var index = 0; index < GetMenuItemCount(menu); index++)
        {
            var child = GetSubMenu(menu, index);
            if (child != 0)
            {
                DisableAllCommands(child);
                continue;
            }

            var id = GetMenuItemID(menu, index);
            if (id == 0 || id == uint.MaxValue)
            {
                continue;
            }

            EnableMenuItem(menu, id, 0 /* MF_BYCOMMAND */ | 0x1 /* MF_GRAYED */);
        }
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
    public void SelfDrawnEmptyWindowMenusUseHostAvailabilityFacts()
    {
        Loc.Initialize(LocalesDirectory, "zh-CN");
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        var context = new CommandContext(DocumentAvailable: false, EditorReady: false);
        var router = new CommandRouter(shortcuts,
            command => CommandStateResolver.Resolve(command, context), _ => { });
        using var service = new NativeMenuService(router,
            shortcuts, () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN", () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);

        var file = service.BuildSelfDrawnTopLevelMenu(0);
        var view = service.BuildSelfDrawnTopLevelMenu(4);
        var edit = service.BuildSelfDrawnTopLevelMenu(1);
        var insert = service.BuildSelfDrawnTopLevelMenu(2);
        var format = service.BuildSelfDrawnTopLevelMenu(3);
        var help = service.BuildSelfDrawnTopLevelMenu(5);
        bool IsEnabled(EditorMenuModel model, string label) =>
            Flatten(model.Secondary).First(entry => entry.Label == label).State.IsEnabled;

        Assert.IsTrue(IsEnabled(file, "文本文件"));
        Assert.IsTrue(IsEnabled(file, "最近项目"));
        Assert.IsFalse(Flatten(file.Secondary).First(entry => entry.Label == "(暂无最近项目)").State.IsEnabled);
        Assert.IsFalse(IsEnabled(file, "关闭文件夹"));
        Assert.IsFalse(IsEnabled(file, "保存"));
        Assert.IsFalse(IsEnabled(edit, "撤销"));
        Assert.IsFalse(IsEnabled(insert, "插入超链接..."));
        Assert.IsFalse(IsEnabled(format, "加粗"));
        Assert.IsFalse(IsEnabled(view, "标签页管理"));
        Assert.IsFalse(IsEnabled(view, "设置缩放"));
        Assert.IsFalse(Flatten(view.Secondary).First(entry => entry.Label == "100%").State.IsEnabled);
        Assert.IsFalse(IsEnabled(view, "源码模式"));
        Assert.IsFalse(IsEnabled(view, "段落专注模式"));
        Assert.IsFalse(IsEnabled(view, "重新渲染所有 Mermaid 图表"));
        Assert.IsTrue(IsEnabled(view, "显示侧栏"));
        Assert.IsTrue(IsEnabled(view, "显示状态栏"));
        Assert.IsTrue(IsEnabled(view, "最简模式"));
        Assert.IsTrue(IsEnabled(view, "显示代码高亮"));
        Assert.IsTrue(IsEnabled(help, "欢迎"));
        Assert.IsTrue(IsEnabled(help, "示例文档"));
        Assert.IsTrue(IsEnabled(help, "学习 Markdown..."));
        Assert.IsTrue(IsEnabled(help, "代码格式化器"));

        context = context with { WorkspaceAvailable = true, ClosedTabsAvailable = true };
        file = service.BuildSelfDrawnTopLevelMenu(0);
        view = service.BuildSelfDrawnTopLevelMenu(4);
        Assert.IsTrue(IsEnabled(file, "关闭文件夹"));
        Assert.IsTrue(IsEnabled(view, "标签页管理"));
        Assert.IsTrue(IsEnabled(view, "重新打开关闭的标签"));
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
    [DllImport("user32.dll")] private static extern uint EnableMenuItem(nint menu, uint item, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
    private static extern int GetMenuString(nint menu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
}
