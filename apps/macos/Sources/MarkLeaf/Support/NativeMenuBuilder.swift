import AppKit

/// 原生菜单栏：与 Windows 版（NativeMenuService）保持一致的菜单结构。
/// 菜单 → AppMenu / 文件 / 编辑 / 段落 / 格式 / 视图 / 外观 / 帮助。
/// macOS 惯例调整：关于/偏好设置/退出放在 App 菜单（Windows 在帮助/文件菜单）。
final class NativeMenuBuilder {
    private static let zoomOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200]

    func build() -> NSMenu {
        let mainMenu = NSMenu()
        addMenu(mainMenu, title: ProcessInfo.processInfo.processName, submenu: appMenu())
        addMenu(mainMenu, title: L10n.t("文件"), submenu: fileMenu())
        addMenu(mainMenu, title: L10n.t("编辑"), submenu: editMenu())
        addMenu(mainMenu, title: L10n.t("段落"), submenu: paragraphMenu())
        addMenu(mainMenu, title: L10n.t("格式"), submenu: formatMenu())
        addMenu(mainMenu, title: L10n.t("视图"), submenu: viewMenu())
        addMenu(mainMenu, title: L10n.t("外观"), submenu: appearanceMenu())
        addMenu(mainMenu, title: L10n.t("帮助"), submenu: helpMenu())
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
        menu.addItem(commandItem(L10n.t("关于 MarkLeaf"), "showAbout"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("偏好设置…"), "showPreferences", key: ","))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("隐藏 MarkLeaf"), #selector(NSApplication.hide(_:)), target: NSApp, key: "h"))
        menu.addItem(item(L10n.t("隐藏其他"), #selector(NSApplication.hideOtherApplications(_:)), target: NSApp, key: "h", mask: [.command, .option]))
        menu.addItem(item(L10n.t("全部显示"), #selector(NSApplication.unhideAllApplications(_:)), target: NSApp))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("退出 MarkLeaf"), #selector(NSApplication.terminate(_:)), target: NSApp, key: "q"))
        return menu
    }

    // MARK: - 文件（对应 Windows BuildFileMenu）

    private func fileMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("新建"), "new", key: "n"))
        menu.addItem(commandItem(L10n.t("新建窗口"), "newWindow"))
        menu.addItem(commandItem(L10n.t("打开…"), "open", key: "o"))
        menu.addItem(commandItem(L10n.t("在新窗口中打开…"), "openInNewWindow"))
        menu.addItem(commandItem(L10n.t("打开文件夹…"), "openFolder"))

        // 最近项目：最近文件 + 最近文件夹（动态刷新）
        let recent = NSMenu(title: L10n.t("最近项目"))
        recent.delegate = RecentMenuDelegate.shared
        let recentParent = NSMenuItem(title: L10n.t("最近项目"), action: nil, keyEquivalent: "")
        recentParent.submenu = recent
        menu.addItem(recentParent)

        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("保存"), "save", key: "s"))
        menu.addItem(commandItem(L10n.t("另存为…"), "saveAs", key: "S"))
        menu.addItem(commandItem(L10n.t("导出…"), "export", key: "e", mask: [.command, .shift]))
        menu.addItem(commandItem(L10n.t("打印…"), "print", key: "p"))
        menu.addItem(commandItem(L10n.t("恢复未保存的文件…"), "recoverUnsavedFiles"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("关闭文件夹"), "closeFolder"))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("关闭窗口"), #selector(NSWindow.performClose(_:)), target: nil, key: "w"))
        return menu
    }

    // MARK: - 编辑（对应 Windows BuildEditMenu）

    private func editMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("撤销"), "undo", key: "z"))
        menu.addItem(commandItem(L10n.t("重做"), "redo", key: "Z"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("剪切"), "cut", key: "x"))
        menu.addItem(commandItem(L10n.t("拷贝"), "copy", key: "c"))
        menu.addItem(commandItem(L10n.t("复制为 Markdown 源码"), "copyMarkdown"))
        menu.addItem(commandItem(L10n.t("复制为纯文本"), "copyPlain"))
        menu.addItem(commandItem(L10n.t("粘贴"), "paste", key: "v"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("查找"), "find", key: "f"))
        menu.addItem(commandItem(L10n.t("替换"), "replace", key: "f", mask: [.command, .option]))
        return menu
    }

    // MARK: - 段落（对应 Windows BuildParagraphMenu）

    private func paragraphMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("正文"), "setParagraph"))

        let headings = NSMenu(title: L10n.t("标题"))
        for level in 1...6 {
            headings.addItem(commandItem(L10n.f("%@级标题", Self.levelName(level)), "setHeading\(level)", key: "\(level)"))
        }
        menu.addItem(popup(L10n.t("标题"), headings))

        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("提升标题级别"), "promoteHeading", key: "."))
        menu.addItem(commandItem(L10n.t("降低标题级别"), "demoteHeading", key: ","))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("引用"), "toggleBlockquote"))
        menu.addItem(commandItem(L10n.t("段间公式"), "insertMathBlock"))
        menu.addItem(commandItem(L10n.t("代码块"), "toggleCodeBlock"))
        menu.addItem(commandItem(L10n.t("水平线"), "insertHorizontalRule"))
        menu.addItem(commandItem(L10n.t("段前插入行"), "insertLineBefore"))
        menu.addItem(commandItem(L10n.t("段后插入行"), "insertLineAfter"))

        let lists = NSMenu(title: L10n.t("列表"))
        lists.addItem(commandItem(L10n.t("无序列表"), "toggleBulletList"))
        lists.addItem(commandItem(L10n.t("有序列表"), "toggleOrderedList"))
        lists.addItem(commandItem(L10n.t("任务列表"), "toggleTaskList"))
        menu.addItem(popup(L10n.t("列表"), lists))

        menu.addItem(popup(L10n.t("表格"), tableMenu()))
        return menu
    }

    private func tableMenu() -> NSMenu {
        let menu = NSMenu(title: L10n.t("表格"))
        menu.addItem(tableSizePickerMenuItem { size in
            AppWindowManager.shared.activeSession?.insertTable(rows: size.rows, columns: size.columns)
        })
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("在上方添加行"), "addRowBefore"))
        menu.addItem(commandItem(L10n.t("在下方添加行"), "addRowAfter"))
        menu.addItem(commandItem(L10n.t("删除当前行"), "deleteRow"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("在左侧添加列"), "addColumnBefore"))
        menu.addItem(commandItem(L10n.t("在右侧添加列"), "addColumnAfter"))
        menu.addItem(commandItem(L10n.t("删除当前列"), "deleteColumn"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("左对齐"), "alignTableLeft"))
        menu.addItem(commandItem(L10n.t("居中对齐"), "alignTableCenter"))
        menu.addItem(commandItem(L10n.t("右对齐"), "alignTableRight"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("删除表格"), "deleteTable"))
        return menu
    }

    // MARK: - 格式（对应 Windows BuildFormatMenu）

    private func formatMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("加粗"), "toggleBold", key: "b"))
        menu.addItem(commandItem(L10n.t("斜体"), "toggleItalic", key: "i"))
        menu.addItem(commandItem(L10n.t("下划线"), "toggleUnderline", key: "u"))
        menu.addItem(commandItem(L10n.t("删除线"), "toggleStrike"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("行内代码"), "toggleCode"))
        menu.addItem(commandItem(L10n.t("行内公式"), "insertMathInline"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("格式刷"), "formatPainter", key: "c", mask: [.command, .shift]))
        menu.addItem(commandItem(L10n.t("应用格式刷"), "formatPainterApply", key: "v", mask: [.command, .shift]))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("插入超链接…"), "insertLink", key: "k"))
        menu.addItem(commandItem(L10n.t("插入本地图片…"), "insertImage"))
        menu.addItem(commandItem(L10n.t("插入来自互联网的图片…"), "insertImageFromUrl"))
        menu.addItem(commandItem(L10n.t("顺时针旋转图片"), "rotateImage"))
        return menu
    }

    // MARK: - 视图（对应 Windows BuildViewMenu）

    private func viewMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("显示侧栏"), "toggleSidebar"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("工作区"), "workspaceTab"))
        menu.addItem(commandItem(L10n.t("大纲"), "outlineTab"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("树结构"), "treeView"))
        menu.addItem(commandItem(L10n.t("文档列表"), "listView"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("显示状态栏"), "toggleStatusBar"))
        menu.addItem(commandItem(L10n.t("源码模式"), "sourceMode", key: "u", mask: [.command, .option]))
        menu.addItem(commandItem(
            L10n.t("专注模式"),
            "toggleFocusMode",
            key: "f",
            mask: [.command, .shift]
        ))
        menu.addItem(.separator())

        // 缩放（对齐 Windows fccc7ad：缩放菜单从外观移到视图）
        let session = AppWindowManager.shared.activeSession
        let zoomMenu = NSMenu(title: L10n.t("设置缩放"))
        for percent in Self.zoomOptions {
            let item = NSMenuItem(title: "\(percent)%", action: #selector(MenuRouter.setZoom(_:)), keyEquivalent: "")
            item.target = MenuRouter.shared
            item.representedObject = percent
            item.state = percent == session?.zoomPercent ? .on : .off
            zoomMenu.addItem(item)
        }
        menu.addItem(popup(L10n.t("设置缩放"), zoomMenu))
        menu.addItem(commandItem(L10n.t("放大"), "zoomIn", key: "="))
        menu.addItem(commandItem(L10n.t("缩小"), "zoomOut", key: "-"))
        menu.addItem(commandItem(L10n.t("重置为100%"), "resetZoom", key: "0"))
        return menu
    }

    // MARK: - 外观（对应 Windows BuildAppearanceMenu）

    private func appearanceMenu() -> NSMenu {
        let menu = NSMenu()
        let session = AppWindowManager.shared.activeSession
        let styles = session?.styles ?? []
        let themes = session?.colorThemes ?? []

        // 排版样式（动态）
        let styleMenu = NSMenu(title: L10n.t("排版样式"))
        for style in styles {
            let item = styleItem(L10n.t(style.displayName), #selector(MenuRouter.chooseStyle(_:)), style.id)
            item.state = style.id == session?.currentStyleId ? .on : .off
            styleMenu.addItem(item)
        }
        menu.addItem(popup(L10n.t("排版样式"), styleMenu))

        // 颜色主题（浅色 / 深色分组，对应 Windows RefreshColorMenu）
        let themeMenu = NSMenu(title: L10n.t("颜色主题"))
        let lightThemes = themes.filter { !$0.isDark }
        let darkThemes = themes.filter { $0.isDark }
        for theme in lightThemes {
            let item = styleItem(L10n.t(theme.displayName), #selector(MenuRouter.chooseTheme(_:)), theme.id)
            item.state = theme.id == session?.currentThemeId ? .on : .off
            themeMenu.addItem(item)
        }
        if !lightThemes.isEmpty && !darkThemes.isEmpty {
            themeMenu.addItem(.separator())
        }
        for theme in darkThemes {
            let item = styleItem(L10n.t(theme.displayName), #selector(MenuRouter.chooseTheme(_:)), theme.id)
            item.state = theme.id == session?.currentThemeId ? .on : .off
            themeMenu.addItem(item)
        }
        themeMenu.addItem(.separator())
        themeMenu.addItem(commandItem(L10n.t("与操作系统同步"), "toggleFollowSystemTheme"))
        menu.addItem(popup(L10n.t("颜色主题"), themeMenu))

        menu.addItem(.separator())

        // 主题文件（对齐 Windows fccc7ad：添加主题导入 CSS）
        menu.addItem(commandItem(L10n.t("添加主题…"), "importTheme"))
        menu.addItem(commandItem(L10n.t("打开主题文件夹…"), "revealThemeFolder"))
        return menu
    }

    // MARK: - 帮助（对应 Windows BuildHelpMenu；偏好设置/关于按 macOS 惯例在 App 菜单）

    private func helpMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("快捷键"), "showShortcuts"))
        menu.addItem(commandItem(L10n.t("更新内容"), "openChangelog"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("MarkLeaf 项目主页"), "openHomepage"))
        menu.addItem(commandItem(L10n.t("MarkLeaf 帮助"), "openHelp"))
        return menu
    }

    // MARK: - Helpers

    private static func levelName(_ level: Int) -> String {
        // 英文/日文直接用数字（Heading 1 / 見出し 1）；中文用「一/二/…」
        let lang = SettingsService.shared.settings.displayLanguage
        if lang == "en" || lang == "ja" { return "\(level)" }
        return ["一", "二", "三", "四", "五", "六"][level - 1]
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
        // 跟随系统外观时禁用主题选择（对应偏好设置置灰）
        if menuItem.action == #selector(chooseTheme(_:)) {
            return !SettingsService.shared.settings.followSystemTheme
        }
        guard let command = menuItem.representedObject as? String else { return true }
        let s = session
        if s?.isReadOnly == true, EditorSession.readOnlyBlockedCommands.contains(command) {
            return false
        }
        switch command {
        case "print": return session != nil
        case "toggleSidebar": menuItem.state = s?.sidebarVisible == true ? .on : .off
        case "workspaceTab": menuItem.state = s?.sidebarTabIndex == 0 ? .on : .off
        case "outlineTab": menuItem.state = s?.sidebarTabIndex == 1 ? .on : .off
        case "treeView": menuItem.state = s?.workspaceListMode == false ? .on : .off
        case "listView": menuItem.state = s?.workspaceListMode == true ? .on : .off
        case "toggleStatusBar": menuItem.state = s?.statusBarVisible == true ? .on : .off
        case "toggleFocusMode":
            menuItem.state = AppWindowManager.shared.activeWindowController?.isFocusMode == true ? .on : .off
        case "toggleFollowSystemTheme":
            menuItem.state = SettingsService.shared.settings.followSystemTheme ? .on : .off
        case "sourceMode":
            // 纯文本文档固定为源码模式，无法切换回可视化，直接置灰。
            if s?.isPlainText == true { return false }
            menuItem.state = s?.isSourceMode == true ? .on : .off

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
        // 格式刷：可视化模式下，可吸附来源或已激活时均可点（再次点击取消，对齐 Word 按钮切换）
        case "formatPainter":
            menuItem.state = s?.isFormatPainterArmed == true ? .on : .off
            return EditorContextMenuState.formatPainterShortcutEnabled(isSourceMode: s?.isSourceMode ?? true)
        case "formatPainterApply":
            return s?.isSourceMode == false && s?.isFormatPainterArmed == true
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
            panel.title = L10n.t("打开文件夹")
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
        case "openChangelog":
            AppWindowManager.shared.openChangelog()
        case "toggleFollowSystemTheme":
            let newValue = !SettingsService.shared.settings.followSystemTheme
            SettingsService.shared.update { $0.followSystemTheme = newValue }
            AppWindowManager.shared.applyThemeModeToAll()
            NativeMenuBuilder.refreshIfNeeded()
        case "toggleFocusMode":
            AppWindowManager.shared.activeWindowController?.toggleFocusMode()
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
            let filesHeader = disabledItem(L10n.t("最近文件"))
            menu.addItem(filesHeader)
            let files = settings.recentFiles.prefix(10)
            if files.isEmpty {
                menu.addItem(disabledItem(L10n.t("(暂无)")))
            }
            for (index, path) in files.enumerated() {
                menu.addItem(recentItem("\(index + 1)  \((path as NSString).lastPathComponent)", "file", path))
            }
        }
        if settings.recordRecentFolders {
            if settings.recordRecentFiles { menu.addItem(.separator()) }
            let foldersHeader = disabledItem(L10n.t("最近文件夹"))
            menu.addItem(foldersHeader)
            let folders = settings.recentFolders.prefix(10)
            if folders.isEmpty {
                menu.addItem(disabledItem(L10n.t("(暂无)")))
            }
            for (index, path) in folders.enumerated() {
                menu.addItem(recentItem("\(index + 1)  \((path as NSString).lastPathComponent)", "folder", path))
            }
        }
        if !settings.recordRecentFiles && !settings.recordRecentFolders {
            menu.addItem(disabledItem(L10n.t("(未启用记录)")))
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
        if isReadOnly && Self.readOnlyBlockedCommands.contains(command) {
            return
        }
        switch command {
        case "new": newDocument()
        case "open": openDocument()
        case "save": saveDocument()
        case "saveAs": saveDocumentAs()
        case "export": exportDocument()
        case "print": printDocument()
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
        case "importTheme": importTheme()
        case "promoteHeading": execute("promoteHeading")
        case "demoteHeading": execute("demoteHeading")
        case "toggleUnderline": executeInlineFormat("toggleUnderline")
        case "toggleCode": executeInlineFormat("toggleCode")
        case "insertMathInline": insertMath(isBlock: false)
        case "insertMathBlock": insertMath(isBlock: true)
        case "editMath": editMath()
        case "convertMath": execute("convertMath")
        case "deleteMath": execute("deleteMath")
        case "selectAll": execute("selectAll")
        case "exitCode": execute("exitCode")
        case "formatPainter", "formatPainterArm", "formatPainterApply": execute(command)
        case "toggleBlockquote": execute("toggleBlockquote")
        case "toggleCodeBlock": execute("toggleCodeBlock")
        case "insertHorizontalRule": execute("insertHorizontalRule")
        case "insertTable": execute("insertTable")
        case "insertLineBefore": execute("insertLineBefore")
        case "insertLineAfter": execute("insertLineAfter")
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
