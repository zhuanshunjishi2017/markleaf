import AppKit

/// 原生菜单栏：按 Windows 版（NativeMenuService）菜单结构移植。
/// 菜单 → AppMenu / 文件 / 编辑 / 段落 / 格式 / 视图 / 外观 / 帮助。
/// macOS 惯例调整：关于/偏好设置/退出放在 App 菜单（Windows 在帮助/文件菜单）。
final class NativeMenuBuilder {
    private static let zoomOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200]

    func build() -> NSMenu {
        let mainMenu = NSMenu()
        addMenu(mainMenu, title: ProcessInfo.processInfo.processName, submenu: appMenu())
        addMenu(mainMenu, title: "文件", submenu: fileMenu())
        addMenu(mainMenu, title: "编辑", submenu: editMenu())
        addMenu(mainMenu, title: "段落", submenu: paragraphMenu())
        addMenu(mainMenu, title: "格式", submenu: formatMenu())
        addMenu(mainMenu, title: "视图", submenu: viewMenu())
        addMenu(mainMenu, title: "外观", submenu: appearanceMenu())
        addMenu(mainMenu, title: "帮助", submenu: helpMenu())
        return mainMenu
    }

    static func refreshIfNeeded() {
        guard NSApp.mainMenu != nil else { return }
        NSApp.mainMenu = NativeMenuBuilder().build()
    }

    private func addMenu(_ menu: NSMenu, title: String, submenu: NSMenu) {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.submenu = submenu
        menu.addItem(item)
    }

    // MARK: - App 菜单（macOS 惯例：关于/偏好设置/退出）

    private func appMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("关于 MarkLeaf", "showAbout"))
        menu.addItem(.separator())
        menu.addItem(commandItem("偏好设置…", "showPreferences", key: ","))
        menu.addItem(.separator())
        menu.addItem(item("隐藏 MarkLeaf", #selector(NSApplication.hide(_:)), target: NSApp, key: "h"))
        menu.addItem(item("隐藏其他", #selector(NSApplication.hideOtherApplications(_:)), target: NSApp, key: "h", mask: [.command, .option]))
        menu.addItem(item("全部显示", #selector(NSApplication.unhideAllApplications(_:)), target: NSApp))
        menu.addItem(.separator())
        menu.addItem(item("退出 MarkLeaf", #selector(NSApplication.terminate(_:)), target: NSApp, key: "q"))
        return menu
    }

    // MARK: - 文件（对应 Windows BuildFileMenu）

    private func fileMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("新建", "new", key: "n"))
        menu.addItem(commandItem("新建窗口", "newWindow"))
        menu.addItem(commandItem("打开…", "open", key: "o"))
        menu.addItem(commandItem("在新窗口中打开…", "openInNewWindow"))
        menu.addItem(commandItem("打开文件夹…", "openFolder"))

        // 最近项目：最近文件 + 最近文件夹（动态刷新）
        let recent = NSMenu(title: "最近项目")
        recent.delegate = RecentMenuDelegate.shared
        let recentParent = NSMenuItem(title: "最近项目", action: nil, keyEquivalent: "")
        recentParent.submenu = recent
        menu.addItem(recentParent)

        menu.addItem(.separator())
        menu.addItem(commandItem("保存", "save", key: "s"))
        menu.addItem(commandItem("另存为…", "saveAs", key: "S"))
        menu.addItem(commandItem("导出…", "export", key: "e", mask: [.command, .shift]))
        menu.addItem(commandItem("恢复未保存的文件…", "recoverUnsavedFiles"))
        menu.addItem(.separator())
        menu.addItem(commandItem("关闭文件夹", "closeFolder"))
        menu.addItem(.separator())
        menu.addItem(item("关闭窗口", #selector(NSWindow.performClose(_:)), target: nil, key: "w"))
        return menu
    }

    // MARK: - 编辑（对应 Windows BuildEditMenu）

    private func editMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("撤销", "undo", key: "z"))
        menu.addItem(commandItem("重做", "redo", key: "Z"))
        menu.addItem(.separator())
        menu.addItem(commandItem("剪切", "cut", key: "x"))
        menu.addItem(commandItem("拷贝", "copy", key: "c"))
        menu.addItem(commandItem("复制为 Markdown 源码", "copyMarkdown"))
        menu.addItem(commandItem("复制为纯文本", "copyPlain"))
        menu.addItem(commandItem("粘贴", "paste", key: "v"))
        menu.addItem(.separator())
        menu.addItem(commandItem("查找", "find", key: "f"))
        menu.addItem(commandItem("替换", "replace", key: "f", mask: [.command, .option]))
        return menu
    }

    // MARK: - 段落（对应 Windows BuildParagraphMenu）

    private func paragraphMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("正文", "setParagraph"))

        let headings = NSMenu(title: "标题")
        for level in 1...6 {
            headings.addItem(commandItem("\(Self.levelName(level))级标题", "setHeading\(level)", key: "\(level)"))
        }
        menu.addItem(popup("标题", headings))

        menu.addItem(.separator())
        menu.addItem(commandItem("提升标题级别", "promoteHeading", key: "."))
        menu.addItem(commandItem("降低标题级别", "demoteHeading", key: ","))
        menu.addItem(.separator())
        menu.addItem(commandItem("引用", "toggleBlockquote"))
        menu.addItem(commandItem("代码块", "toggleCodeBlock"))
        menu.addItem(commandItem("水平线", "insertHorizontalRule"))

        let lists = NSMenu(title: "列表")
        lists.addItem(commandItem("无序列表", "toggleBulletList"))
        lists.addItem(commandItem("有序列表", "toggleOrderedList"))
        lists.addItem(commandItem("任务列表", "toggleTaskList"))
        menu.addItem(popup("列表", lists))

        menu.addItem(popup("表格", tableMenu()))
        return menu
    }

    private func tableMenu() -> NSMenu {
        let menu = NSMenu(title: "表格")
        menu.addItem(commandItem("插入表格", "insertTable"))
        menu.addItem(.separator())
        menu.addItem(commandItem("在上方添加行", "addRowBefore"))
        menu.addItem(commandItem("在下方添加行", "addRowAfter"))
        menu.addItem(commandItem("删除当前行", "deleteRow"))
        menu.addItem(.separator())
        menu.addItem(commandItem("在左侧添加列", "addColumnBefore"))
        menu.addItem(commandItem("在右侧添加列", "addColumnAfter"))
        menu.addItem(commandItem("删除当前列", "deleteColumn"))
        menu.addItem(.separator())
        menu.addItem(commandItem("左对齐", "alignTableLeft"))
        menu.addItem(commandItem("居中对齐", "alignTableCenter"))
        menu.addItem(commandItem("右对齐", "alignTableRight"))
        menu.addItem(.separator())
        menu.addItem(commandItem("删除表格", "deleteTable"))
        return menu
    }

    // MARK: - 格式（对应 Windows BuildFormatMenu）

    private func formatMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("加粗", "toggleBold", key: "b"))
        menu.addItem(commandItem("斜体", "toggleItalic", key: "i"))
        menu.addItem(commandItem("下划线", "toggleUnderline", key: "u"))
        menu.addItem(commandItem("删除线", "toggleStrike"))
        menu.addItem(.separator())
        menu.addItem(commandItem("行内代码", "toggleCode"))
        menu.addItem(.separator())
        menu.addItem(commandItem("插入超链接…", "insertLink", key: "k"))
        menu.addItem(commandItem("插入本地图片…", "insertImage"))
        menu.addItem(commandItem("插入来自互联网的图片…", "insertImageFromUrl"))
        menu.addItem(commandItem("顺时针旋转图片", "rotateImage"))
        return menu
    }

    // MARK: - 视图（对应 Windows BuildViewMenu）

    private func viewMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("显示侧栏", "toggleSidebar"))
        menu.addItem(.separator())
        menu.addItem(commandItem("工作区", "workspaceTab"))
        menu.addItem(commandItem("大纲", "outlineTab"))
        menu.addItem(.separator())
        menu.addItem(commandItem("树结构", "treeView"))
        menu.addItem(commandItem("文档列表", "listView"))
        menu.addItem(.separator())
        menu.addItem(commandItem("显示状态栏", "toggleStatusBar"))
        menu.addItem(commandItem("源码模式", "sourceMode", key: "u", mask: [.command, .option]))
        return menu
    }

    // MARK: - 外观（对应 Windows BuildAppearanceMenu）

    private func appearanceMenu() -> NSMenu {
        let menu = NSMenu()
        let session = AppWindowManager.shared.activeSession
        let styles = session?.styles ?? []
        let themes = session?.colorThemes ?? []

        // 排版样式（动态）
        let styleMenu = NSMenu(title: "排版样式")
        for style in styles {
            let item = styleItem(style.displayName, #selector(MenuRouter.chooseStyle(_:)), style.id)
            item.state = style.id == session?.currentStyleId ? .on : .off
            styleMenu.addItem(item)
        }
        menu.addItem(popup("排版样式", styleMenu))

        // 颜色主题（浅色 / 深色分组，对应 Windows RefreshColorMenu）
        let themeMenu = NSMenu(title: "颜色主题")
        let lightThemes = themes.filter { !$0.isDark }
        let darkThemes = themes.filter { $0.isDark }
        for theme in lightThemes {
            let item = styleItem(theme.displayName, #selector(MenuRouter.chooseTheme(_:)), theme.id)
            item.state = theme.id == session?.currentThemeId ? .on : .off
            themeMenu.addItem(item)
        }
        if !lightThemes.isEmpty && !darkThemes.isEmpty {
            themeMenu.addItem(.separator())
        }
        for theme in darkThemes {
            let item = styleItem(theme.displayName, #selector(MenuRouter.chooseTheme(_:)), theme.id)
            item.state = theme.id == session?.currentThemeId ? .on : .off
            themeMenu.addItem(item)
        }
        menu.addItem(popup("颜色主题", themeMenu))

        menu.addItem(.separator())

        // 设置缩放（对应 Windows RefreshZoomMenu）
        let zoomMenu = NSMenu(title: "设置缩放")
        for percent in Self.zoomOptions {
            let item = NSMenuItem(title: "\(percent)%", action: #selector(MenuRouter.setZoom(_:)), keyEquivalent: "")
            item.target = MenuRouter.shared
            item.representedObject = percent
            item.state = percent == session?.zoomPercent ? .on : .off
            zoomMenu.addItem(item)
        }
        menu.addItem(popup("设置缩放", zoomMenu))
        menu.addItem(commandItem("放大", "zoomIn", key: "="))
        menu.addItem(commandItem("缩小", "zoomOut", key: "-"))
        menu.addItem(commandItem("重置为100%", "resetZoom", key: "0"))
        menu.addItem(.separator())
        menu.addItem(commandItem("打开主题文件夹…", "revealThemeFolder"))
        return menu
    }

    // MARK: - 帮助（对应 Windows BuildHelpMenu；偏好设置/关于按 macOS 惯例在 App 菜单）

    private func helpMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem("快捷键", "showShortcuts"))
        menu.addItem(.separator())
        menu.addItem(commandItem("MarkLeaf 项目主页", "openHomepage"))
        menu.addItem(commandItem("MarkLeaf 帮助", "openHelp"))
        return menu
    }

    // MARK: - Helpers

    private static func levelName(_ level: Int) -> String {
        ["一", "二", "三", "四", "五", "六"][level - 1]
    }

    private func commandItem(_ title: String, _ command: String, key: String = "", mask: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(MenuRouter.performCommand(_:)), keyEquivalent: key)
        item.target = MenuRouter.shared
        item.keyEquivalentModifierMask = mask
        item.representedObject = command
        return item
    }

    private func styleItem(_ title: String, _ action: Selector, _ id: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = MenuRouter.shared
        item.representedObject = id
        return item
    }

    private func popup(_ title: String, _ submenu: NSMenu) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.submenu = submenu
        return item
    }

    private func item(_ title: String, _ action: Selector?, target: AnyObject?, key: String = "", mask: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let menuItem = NSMenuItem(title: title, action: action, keyEquivalent: key)
        menuItem.target = target
        menuItem.keyEquivalentModifierMask = mask
        return menuItem
    }
}

/// 菜单动作路由：把菜单项转发到当前活跃窗口的会话。
final class MenuRouter: NSObject, NSMenuItemValidation {
    static let shared = MenuRouter()

    private var session: EditorSession? { AppWindowManager.shared.activeSession }

    /// 视图菜单勾选状态（对应 Windows RefreshStates）。
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        guard let command = menuItem.representedObject as? String else { return true }
        let s = session
        switch command {
        case "toggleSidebar": menuItem.state = s?.sidebarVisible == true ? .on : .off
        case "workspaceTab": menuItem.state = s?.sidebarTabIndex == 0 ? .on : .off
        case "outlineTab": menuItem.state = s?.sidebarTabIndex == 1 ? .on : .off
        case "treeView": menuItem.state = s?.workspaceListMode == false ? .on : .off
        case "listView": menuItem.state = s?.workspaceListMode == true ? .on : .off
        case "toggleStatusBar": menuItem.state = s?.statusBarVisible == true ? .on : .off
        case "sourceMode": menuItem.state = s?.isSourceMode == true ? .on : .off

        // 无选中图片时置灰（对应 Windows 命令状态）
        case "rotateImage": return s?.imageSelected == true
        // 降低标题级别：仅当光标在标题内可用（非标题保持原样 → 置灰）
        case "demoteHeading": return s?.headingLevel != nil
        // 表格命令：仅当光标在表格内可用
        case "addRowBefore", "addRowAfter", "deleteRow",
             "addColumnBefore", "addColumnAfter", "deleteColumn",
             "alignTableLeft", "alignTableCenter", "alignTableRight", "deleteTable":
            return s?.inTable == true
        // 插入表格：仅在表格外可用
        case "insertTable": return s?.inTable == false
        // 撤销/重做
        case "undo": return s?.canUndo == true
        case "redo": return s?.canRedo == true
        default: break
        }
        return true
    }

    @objc func performCommand(_ sender: NSMenuItem) {
        guard let command = sender.representedObject as? String else { return }
        switch command {
        case "showPreferences":
            AppWindowManager.shared.showPreferences()
        case "showAbout":
            AppWindowManager.shared.showAbout()
        case "newWindow":
            AppWindowManager.shared.newWindow()
        case "openInNewWindow":
            AppWindowManager.shared.openDocumentInNewWindow()
        case "openFolder":
            guard let session else { return }
            let panel = NSOpenPanel()
            panel.title = "打开文件夹"
            panel.canChooseFiles = false
            panel.canChooseDirectories = true
            panel.allowsMultipleSelection = false
            guard let window = session.webView?.window else { return }
            panel.beginSheetModal(for: window) { response in
                if response == .OK, let url = panel.url {
                    session.loadWorkspace(url.path)
                }
            }
        case "recoverUnsavedFiles":
            AppWindowManager.shared.showRecoveryDialog()
        case "openHomepage":
            if let url = URL(string: "https://github.com/zhuanshunjishi2017/markleaf") {
                NSWorkspace.shared.open(url)
            }
        case "openHelp":
            if let url = URL(string: "https://github.com/zhuanshunjishi2017/markleaf/blob/main/README.md") {
                NSWorkspace.shared.open(url)
            }
        default:
            session?.performMenuCommand(command)
        }
    }

    @objc func chooseStyle(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        session?.setStyle(id)
        NativeMenuBuilder.refreshIfNeeded()
    }

    @objc func chooseTheme(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        session?.setTheme(id)
        NativeMenuBuilder.refreshIfNeeded()
    }

    @objc func setZoom(_ sender: NSMenuItem) {
        guard let percent = sender.representedObject as? Int else { return }
        session?.setZoom(percent)
        NativeMenuBuilder.refreshIfNeeded()
    }
}

/// 最近项目子菜单动态刷新。
final class RecentMenuDelegate: NSObject, NSMenuDelegate {
    static let shared = RecentMenuDelegate()

    func menuNeedsUpdate(_ menu: NSMenu) {
        for item in menu.items where item.tag >= 100 {
            menu.removeItem(item)
        }
        let settings = SettingsService.shared.settings

        if settings.recordRecentFiles {
            let filesHeader = disabledItem("最近文件")
            menu.addItem(filesHeader)
            let files = settings.recentFiles.prefix(10)
            if files.isEmpty {
                menu.addItem(disabledItem("(暂无)"))
            }
            for (index, path) in files.enumerated() {
                menu.addItem(recentItem("\(index + 1)  \((path as NSString).lastPathComponent)", "file", path))
            }
        }
        if settings.recordRecentFolders {
            if settings.recordRecentFiles { menu.addItem(.separator()) }
            let foldersHeader = disabledItem("最近文件夹")
            menu.addItem(foldersHeader)
            let folders = settings.recentFolders.prefix(10)
            if folders.isEmpty {
                menu.addItem(disabledItem("(暂无)"))
            }
            for (index, path) in folders.enumerated() {
                menu.addItem(recentItem("\(index + 1)  \((path as NSString).lastPathComponent)", "folder", path))
            }
        }
        if !settings.recordRecentFiles && !settings.recordRecentFolders {
            menu.addItem(disabledItem("(未启用记录)"))
        }
    }

    private func recentItem(_ title: String, _ kind: String, _ path: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(openRecent(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = [kind, path]
        item.tag = 100
        return item
    }

    private func disabledItem(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    @objc private func openRecent(_ sender: NSMenuItem) {
        guard let payload = sender.representedObject as? [String],
              let session = AppWindowManager.shared.activeSession else { return }
        let kind = payload[0]
        let path = payload[1]
        if kind == "file" {
            session.openRecentFile(path)
        } else {
            session.openRecentFolder(path)
        }
    }
}

/// 窗口菜单动态列表。
final class WindowMenuDelegate: NSObject, NSMenuDelegate {
    static let shared = WindowMenuDelegate()

    func menuNeedsUpdate(_ menu: NSMenu) {
        let dynamic = menu.items.filter { $0.tag >= 100 }
        for item in dynamic {
            menu.removeItem(item)
        }
        let controllers = AppWindowManager.shared.windowControllers
        for (index, controller) in controllers.enumerated() {
            let title = controller.window?.title ?? "窗口 \(index + 1)"
            let menuItem = NSMenuItem(
                title: title,
                action: #selector(selectWindow(_:)),
                keyEquivalent: index < 9 ? "\(index + 1)" : "")
            menuItem.keyEquivalentModifierMask = [.command]
            menuItem.tag = 100 + index
            menuItem.target = self
            menuItem.representedObject = controller
            if controller.window?.isKeyWindow == true {
                menuItem.state = .on
            }
            menu.addItem(menuItem)
        }
    }

    @objc private func selectWindow(_ sender: NSMenuItem) {
        if let controller = sender.representedObject as? EditorWindowController {
            controller.window?.makeKeyAndOrderFront(nil)
        }
    }
}

// MARK: - 会话命令路由（EditorSession 扩展）

extension EditorSession {
    /// 菜单命令分派（对应 Windows CommandRouter）。
    func performMenuCommand(_ command: String) {
        switch command {
        case "new": newDocument()
        case "open": openDocument()
        case "save": saveDocument()
        case "saveAs": saveDocumentAs()
        case "export": exportDocument()
        case "closeFolder": closeWorkspace()
        case "undo": execute("undo")
        case "redo": execute("redo")
        case "cut":
            copySelectionAs(.formatted)
            execute("deleteSelection")
        case "copy": copySelectionAs(.formatted)
        case "copyMarkdown": copySelectionAs(.markdown)
        case "copyPlain": copySelectionAs(.plainText)
        case "paste": pasteFromClipboard()
        case "toggleBold", "toggleItalic", "toggleStrike": executeInlineFormat(command)
        case "find": showFind(showReplace: false)
        case "replace": showFind(showReplace: true)
        case "toggleSidebar": toggleSidebar()
        case "workspaceTab": showWorkspaceTab()
        case "outlineTab": showOutlineTab()
        case "treeView": setWorkspaceListMode(false)
        case "listView": setWorkspaceListMode(true)
        case "toggleStatusBar": toggleStatusBar()
        case "sourceMode": toggleSourceMode()
        case "zoomIn": zoomIn()
        case "zoomOut": zoomOut()
        case "resetZoom": resetZoom()
        case "insertLink": insertLink()
        case "insertImage": insertImageFromPicker()
        case "insertImageFromUrl": insertImageFromUrl()
        case "rotateImage": execute("rotateImageClockwise")
        case "showShortcuts": showShortcuts()
        case "revealThemeFolder": revealThemeFolder()
        case "promoteHeading": execute("promoteHeading")
        case "demoteHeading": execute("demoteHeading")
        case "toggleUnderline": executeInlineFormat("toggleUnderline")
        case "toggleCode": executeInlineFormat("toggleCode")
        case "toggleBlockquote": execute("toggleBlockquote")
        case "toggleCodeBlock": execute("toggleCodeBlock")
        case "insertHorizontalRule": execute("insertHorizontalRule")
        case "insertTable": execute("insertTable")
        case "addRowBefore": execute("addRowBefore")
        case "addRowAfter": execute("addRowAfter")
        case "deleteRow": execute("deleteRow")
        case "addColumnBefore": execute("addColumnBefore")
        case "addColumnAfter": execute("addColumnAfter")
        case "deleteColumn": execute("deleteColumn")
        case "alignTableLeft": execute("alignTableLeft")
        case "alignTableCenter": execute("alignTableCenter")
        case "alignTableRight": execute("alignTableRight")
        case "deleteTable": execute("deleteTable")
        case "setParagraph": execute("setParagraph")
        default:
            if command.hasPrefix("setHeading") {
                execute(command)
            }
        }
    }
}

extension EditorSession {
    /// 上下文菜单兼容入口：把 representedObject 命令转发到 performMenuCommand。
    @objc func handleCommand(_ sender: NSMenuItem) {
        guard let command = sender.representedObject as? String else { return }
        performMenuCommand(command)
    }
}
