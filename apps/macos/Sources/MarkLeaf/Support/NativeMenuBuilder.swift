import AppKit

/// 原生菜单栏：与 Windows 版（NativeMenuService）保持一致的菜单结构。
/// 菜单 → AppMenu / 文件 / 编辑 / 插入 / 格式 / 视图 / 帮助。
/// macOS 惯例调整：关于/偏好设置/退出放在 App 菜单（Windows 在帮助/文件菜单）。
final class NativeMenuBuilder {
    static let zoomOptions = EditorSession.zoomOptions

    func build() -> NSMenu {
        let mainMenu = NSMenu()
        let appMenu = appMenu()
        addMenu(mainMenu, title: ProcessInfo.processInfo.processName, submenu: appMenu)
        addMenu(mainMenu, title: L10n.t("文件"), submenu: fileMenu())
        addMenu(mainMenu, title: L10n.t("编辑"), submenu: editMenu())
        addMenu(mainMenu, title: L10n.t("插入"), submenu: insertMenu())
        addMenu(mainMenu, title: L10n.t("格式"), submenu: formatMenu())
        addMenu(mainMenu, title: L10n.t("视图"), submenu: viewMenu())
        addMenu(mainMenu, title: L10n.t("帮助"), submenu: helpMenu())
        if let servicesItem = appMenu.items.first(
            where: { $0.representedObject as? String == "servicesMenu" }
        ) {
            NSApp.servicesMenu = servicesItem.submenu
        }
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
        menu.addItem(commandItem(L10n.t("检查更新…"), "checkForUpdates"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("偏好设置…"), "showPreferences", key: ","))
        menu.addItem(.separator())
        menu.addItem(servicesMenuItem())
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("隐藏 MarkLeaf"), #selector(NSApplication.hide(_:)), target: NSApp, key: "h"))
        menu.addItem(item(L10n.t("隐藏其他"), #selector(NSApplication.hideOtherApplications(_:)), target: NSApp, key: "h", mask: [.command, .option]))
        menu.addItem(item(L10n.t("全部显示"), #selector(NSApplication.unhideAllApplications(_:)), target: NSApp))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("退出 MarkLeaf"), #selector(NSApplication.terminate(_:)), target: NSApp, key: "q"))
        return menu
    }

    private func servicesMenuItem() -> NSMenuItem {
        let services = NSMenu(title: L10n.t("服务"))
        let item = NSMenuItem(title: L10n.t("服务"), action: nil, keyEquivalent: "")
        item.submenu = services
        item.representedObject = "servicesMenu"
        return item
    }

    // MARK: - 文件（对应 Windows BuildFileMenu）

    private func fileMenu() -> NSMenu {
        let menu = NSMenu()
        let newMenu = NSMenu(title: L10n.t("新建"))
        newMenu.addItem(commandItem(L10n.t("Markdown 文件"), "new", key: "n"))
        newMenu.addItem(commandItem(L10n.t("文本文件"), "newPlainText", key: "n", mask: [.command, .option]))
        menu.addItem(popup(L10n.t("新建"), newMenu))
        menu.addItem(commandItem(L10n.t("新建窗口"), "newWindow", key: "N", mask: [.command, .shift]))
        menu.addItem(commandItem(L10n.t("打开…"), "open", key: "o"))
        menu.addItem(commandItem(L10n.t("以只读方式打开…"), "openReadOnly"))
        menu.addItem(commandItem(L10n.t("在新窗口中打开…"), "openInNewWindow"))
        menu.addItem(commandItem(L10n.t("打开文件夹…"), "openFolder"))
        menu.addItem(.separator())

        // 最近项目：最近文件 + 最近文件夹（动态刷新）
        let recent = NSMenu(title: L10n.t("最近项目"))
        recent.autoenablesItems = false
        recent.delegate = RecentMenuDelegate.shared
        let recentParent = NSMenuItem(title: L10n.t("最近项目"), action: nil, keyEquivalent: "")
        recentParent.submenu = recent
        menu.addItem(recentParent)
        menu.addItem(commandItem(L10n.t("恢复未保存的文件…"), "recoverUnsavedFiles"))
        menu.addItem(.separator())

        menu.addItem(commandItem(L10n.t("保存"), "save", key: "s"))
        menu.addItem(commandItem(L10n.t("保存全部"), "saveAll", key: "s", mask: [.command, .option]))
        menu.addItem(commandItem(L10n.t("另存为…"), "saveAs", key: "S"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("导出…"), "export", key: "e", mask: [.command, .shift]))
        menu.addItem(commandItem(L10n.t("打印…"), "print", key: "p"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("在工作区定位"), "revealActiveTabInWorkspace"))
        menu.addItem(commandItem(L10n.t("复制文件路径"), "copyActiveTabPath"))
        menu.addItem(commandItem(L10n.t("复制文件内容"), "copyActiveFileContents"))
        menu.addItem(commandItem(L10n.t("在 Finder 中显示"), "revealActiveTabInFinder"))
        menu.addItem(commandItem(L10n.t("重命名"), "renameActiveTab"))
        menu.addItem(commandItem(L10n.t("分享"), "shareActiveTab"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("关闭当前标签页"), "closeCurrentTab", key: "w"))
        menu.addItem(commandItem(L10n.t("关闭文件夹"), "closeFolder"))
        return menu
    }

    // MARK: - 编辑（对应 Windows BuildEditMenu）

    private func editMenu() -> NSMenu {
        let menu = NSMenu()
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        menu.delegate = MenuRouter.shared
        menu.addItem(commandItem(L10n.t("撤销"), "undo", key: "z"))
        menu.addItem(commandItem(L10n.t("重做"), "redo", key: "Z"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("剪切"), "cut", key: "x"))
        menu.addItem(commandItem(L10n.t("拷贝"), "copy", key: "c"))
        let copyAs = NSMenu(title: L10n.t("复制为"))
        copyAs.addItem(commandItem(L10n.t("纯文本"), "copyPlain"))
        copyAs.addItem(commandItem(L10n.t("Markdown"), "copyMarkdown"))
        copyAs.addItem(commandItem(L10n.t("HTML"), "copyHtml"))
        let copyAsItem = popup(L10n.t("复制为"), copyAs, requiresDocument: true, validationCommand: "copyAsParent")
        copyAsItem.representedObject = "copyAs"
        menu.addItem(copyAsItem)
        menu.addItem(commandItem(L10n.t("粘贴"), "paste", key: "v"))
        menu.addItem(commandItem(L10n.t("粘贴为纯文本"), "pastePlainText", key: "V", mask: [.command, .shift]))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("全选"), "selectAll", key: "a"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("查找与替换"), "find", key: "f"))
        return menu
    }

    // MARK: - 插入

    private func insertMenu() -> NSMenu {
        let menu = NSMenu()
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        menu.delegate = MenuRouter.shared

        let images = NSMenu(title: L10n.t("图片"))
        images.addItem(commandItem(L10n.t("插入本地图片…"), "insertImage"))
        images.addItem(commandItem(L10n.t("插入来自互联网的图片…"), "insertImageFromUrl"))
        menu.addItem(popup(L10n.t("图片"), images, requiresDocument: true, validationCommand: "insertImageParent"))
        menu.addItem(tableSizePickerSubmenu { size in
            AppWindowManager.shared.activeSession?.insertTable(rows: size.rows, columns: size.columns)
        })
        menu.addItem(commandItem(L10n.t("插入 Mermaid 图表"), "insertMermaid"))
        menu.addItem(commandItem(L10n.t("插入超链接…"), "insertLink", key: "k"))
        menu.addItem(commandItem(L10n.t("插入脚注…"), "insertFootnote"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("行内公式"), "insertMathInline"))
        menu.addItem(commandItem(L10n.t("块级公式"), "insertMathBlock"))
        menu.addItem(commandItem(L10n.t("水平线"), "insertHorizontalRule"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("YAML 前置元数据"), "showFrontMatter"))
        return menu
    }

    private func paragraphActionsMenu() -> NSMenu {
        let paragraphActions = NSMenu(title: L10n.t("段落操作"))
        paragraphActions.addItem(commandItem(L10n.t("段前插入行"), "insertLineBefore"))
        paragraphActions.addItem(commandItem(L10n.t("段后插入行"), "insertLineAfter"))
        paragraphActions.addItem(.separator())
        paragraphActions.addItem(commandItem(L10n.t("重复该段"), "duplicateParagraph"))
        paragraphActions.addItem(commandItem(L10n.t("删除该段"), "deleteParagraph"))
        return paragraphActions
    }

    private func mermaidMenu() -> NSMenu {
        let menu = NSMenu(title: L10n.t("Mermaid"))
        menu.addItem(commandItem(L10n.t("插入 Mermaid 图表"), "insertMermaid"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("重新渲染所有 Mermaid 图表"), "rerenderAllMermaid"))
        return menu
    }

    // MARK: - 格式

    private func paragraphStyleMenu() -> NSMenu {
        let menu = NSMenu(title: L10n.t("段落样式"))
        menu.addItem(commandItem(L10n.t("正文"), "setParagraph"))
        let headings = NSMenu(title: L10n.t("标题"))
        for level in 1...6 {
            headings.addItem(commandItem(L10n.f("%@级标题", Self.levelName(level)), "setHeading\(level)", key: "\(level)"))
        }
        menu.addItem(popup(L10n.t("标题"), headings, requiresDocument: true, validationCommand: "headingParent"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("提升标题级别"), "promoteHeading", key: ".", mask: [.command, .option]))
        menu.addItem(commandItem(L10n.t("降低标题级别"), "demoteHeading", key: ",", mask: [.command, .option]))
        return menu
    }

    private func alertsMenu() -> NSMenu {
        let menu = NSMenu(title: L10n.t("提示框"))
        menu.addItem(commandItem(L10n.t("备注"), "insertAlertNote"))
        menu.addItem(commandItem(L10n.t("提示"), "insertAlertTip"))
        menu.addItem(commandItem(L10n.t("重要"), "insertAlertImportant"))
        menu.addItem(commandItem(L10n.t("警告"), "insertAlertWarning"))
        menu.addItem(commandItem(L10n.t("注意"), "insertAlertCaution"))
        return menu
    }

    private func listsMenu() -> NSMenu {
        let menu = NSMenu(title: L10n.t("列表"))
        menu.addItem(commandItem(L10n.t("无序列表"), "toggleBulletList"))
        menu.addItem(commandItem(L10n.t("有序列表"), "toggleOrderedList"))
        menu.addItem(commandItem(L10n.t("任务列表"), "toggleTaskList"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("增加列表缩进"), "indentListItem", key: "]", mask: [.command]))
        menu.addItem(commandItem(L10n.t("减少列表缩进"), "outdentListItem", key: "[", mask: [.command]))
        return menu
    }

    private func formatMenu() -> NSMenu {
        let menu = NSMenu()
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        menu.delegate = MenuRouter.shared
        menu.addItem(commandItem(L10n.t("加粗"), "toggleBold", key: "b"))
        menu.addItem(commandItem(L10n.t("斜体"), "toggleItalic", key: "i"))
        menu.addItem(commandItem(L10n.t("下划线"), "toggleUnderline", key: "u"))
        menu.addItem(commandItem(L10n.t("删除线"), "toggleStrike"))
        menu.addItem(commandItem(L10n.t("高亮"), "toggleHighlight"))
        menu.addItem(commandItem(L10n.t("行内代码"), "toggleCode", key: "`", mask: [.command, .option]))
        menu.addItem(.separator())
        menu.addItem(popup(L10n.t("段落样式"), paragraphStyleMenu(), requiresDocument: true, validationCommand: "paragraphStyleParent"))
        menu.addItem(popup(L10n.t("列表"), listsMenu(), requiresDocument: true, validationCommand: "listParent"))
        menu.addItem(commandItem(L10n.t("引用"), "toggleBlockquote"))
        menu.addItem(commandItem(L10n.t("代码块"), "toggleCodeBlock"))
        menu.addItem(popup(L10n.t("提示框"), alertsMenu(), requiresDocument: true, validationCommand: "alertParent"))
        menu.addItem(.separator())
        menu.addItem(popup(L10n.t("段落操作"), paragraphActionsMenu(), requiresDocument: true, validationCommand: "paragraphActionsParent"))
        menu.addItem(popup(L10n.t("表格"), tableEditingMenu(), requiresDocument: true, validationCommand: "tableParent"))
        menu.addItem(popup(L10n.t("图片"), imageEditingMenu(), requiresDocument: true, validationCommand: "imageParent"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("格式刷"), "formatPainter", key: "c", mask: [.command, .shift]))
        menu.addItem(commandItem(L10n.t("应用格式刷"), "formatPainterApply", key: "v", mask: [.command, .control]))
        menu.addItem(commandItem(L10n.t("格式化代码块"), "formatCodeBlock", key: "f", mask: [.shift, .option]))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("清除格式"), "clearFormat"))
        return menu
    }

    private func tableEditingMenu() -> NSMenu {
        let menu = NSMenu()
        for (title, command) in [("编辑标题…", "editTableCaption"), ("上方插入行", "addRowBefore"), ("下方插入行", "addRowAfter"), ("删除行", "deleteRow"),
                                  ("左侧插入列", "addColumnBefore"), ("右侧插入列", "addColumnAfter"), ("删除列", "deleteColumn"),
                                  ("左对齐", "alignTableLeft"), ("居中对齐", "alignTableCenter"), ("右对齐", "alignTableRight"), ("删除表格", "deleteTable")] {
            menu.addItem(commandItem(L10n.t(title), command))
        }
        return menu
    }

    private func imageEditingMenu() -> NSMenu {
        let menu = NSMenu()
        for (title, command) in [("更换图片…", "changeImage"), ("编辑标题…", "editImageCaption"),
                                  ("50%", "resizeImage50"), ("75%", "resizeImage75"), ("90%", "resizeImage90"), ("100%", "resizeImage100"),
                                  ("顺时针旋转90°", "rotateImage"), ("将图片另存为…", "saveImageAs")] {
            menu.addItem(commandItem(L10n.t(title), command))
        }
        return menu
    }

    // MARK: - 视图（对应 Windows BuildViewMenu）

    private func viewMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("显示侧栏"), "toggleSidebar"))
        let sidebarSettings = NSMenu(title: L10n.t("侧栏设置"))
        sidebarSettings.addItem(commandItem(L10n.t("工作区"), "workspaceTab"))
        sidebarSettings.addItem(commandItem(L10n.t("大纲"), "outlineTab"))
        sidebarSettings.addItem(commandItem(L10n.t("树结构"), "treeView"))
        sidebarSettings.addItem(commandItem(L10n.t("文档列表"), "listView"))
        sidebarSettings.addItem(.separator())
        sidebarSettings.addItem(commandItem(L10n.t("在右侧显示大纲"), "toggleDetachedOutline"))
        menu.addItem(popup(L10n.t("侧栏设置"), sidebarSettings))
        menu.addItem(commandItem(L10n.t("显示状态栏"), "toggleStatusBar"))
        let tabManagement = NSMenu(title: L10n.t("标签页管理"))
        tabManagement.addItem(commandItem(L10n.t("上一个标签"), "tabPrevious", key: "\t", mask: [.control, .shift]))
        tabManagement.addItem(commandItem(L10n.t("下一个标签"), "tabNext", key: "\t", mask: [.control]))
        tabManagement.addItem(commandItem(L10n.t("关闭当前标签页"), "closeCurrentTab"))
        tabManagement.addItem(commandItem(L10n.t("关闭其他标签"), "closeOtherTabs"))
        tabManagement.addItem(commandItem(L10n.t("重新打开关闭的标签"), "restoreClosedTab"))
        tabManagement.delegate = MenuRouter.TabManagementMenuDelegate.shared
        menu.addItem(popup(L10n.t("标签页管理"), tabManagement, requiresDocument: true, validationCommand: "tabManagementParent"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("源码模式"), "sourceMode", key: "u", mask: [.command, .option]))
        menu.addItem(commandItem(L10n.t("只读模式"), "toggleReadOnlyMode"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("段落专注模式"), "toggleEditorFocusMode"))
        menu.addItem(commandItem(L10n.t("打字机模式"), "toggleTypewriterMode"))
        menu.addItem(commandItem(
            L10n.t("最简模式"),
            "toggleFocusMode",
            key: "f",
            mask: [.command, .shift]
        ))
        menu.addItem(commandItem(L10n.t("全屏幕模式"), "toggleFullScreen", key: "f", mask: [.control, .command]))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("显示代码高亮"), "toggleCodeHighlight"))
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
        zoomMenu.delegate = MenuRouter.ZoomMenuDelegate.shared
        menu.addItem(popup(L10n.t("设置缩放"), zoomMenu, requiresDocument: true, validationCommand: "zoomParent"))
        menu.addItem(commandItem(L10n.t("放大"), "zoomIn", key: "="))
        menu.addItem(commandItem(L10n.t("缩小"), "zoomOut", key: "-"))
        menu.addItem(commandItem(L10n.t("重置为100%"), "resetZoom", key: "0"))
        menu.addItem(.separator())

        menu.addItem(commandItem(L10n.t("主题设置…"), "showThemeSettings", key: "t", mask: [.option, .shift]))
        menu.addItem(commandItem(L10n.t("与操作系统同步"), "toggleFollowSystemTheme"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("重新渲染所有 Mermaid 图表"), "rerenderAllMermaid"))
        menu.addItem(commandItem(L10n.t("重启编辑器"), "restartEditor"))
        return menu
    }

    // MARK: - 帮助（对应 Windows BuildHelpMenu；偏好设置/关于按 macOS 惯例在 App 菜单）

    private func helpMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(commandItem(L10n.t("欢迎"), "openWelcome"))
        let samples = NSMenu(title: L10n.t("示例文档"))
        for sample in SampleDocumentResource.allCases {
            samples.addItem(commandItem(L10n.t(sample.titleKey), sample.command))
        }
        menu.addItem(popup(L10n.t("示例文档"), samples))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("快捷键"), "showShortcuts"))
        menu.addItem(commandItem(L10n.t("更新内容"), "openChangelog"))
        menu.addItem(commandItem(L10n.t("学习 Markdown…"), "learnMarkdown"))
        menu.addItem(commandItem(L10n.t("在线帮助"), "openHelp"))
        menu.addItem(.separator())
        menu.addItem(commandItem(L10n.t("安装可选字体…"), "installOptionalFonts"))
        menu.addItem(commandItem(L10n.t("代码格式化器…"), "codeFormatterManager"))
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
        if let entry = ShortcutCatalog.entry(for: command) {
            if let (effectiveKey, effectiveMask) = ShortcutSettings.shared.effectiveKey(for: entry) {
                item.keyEquivalent = effectiveKey
                item.keyEquivalentModifierMask = effectiveMask
            } else {
                // 用户已“清除”该命令的快捷键。
                item.keyEquivalent = ""
                item.keyEquivalentModifierMask = []
            }
        }
        return item
    }

    private func styleItem(_ title: String, _ action: Selector, _ id: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = MenuRouter.shared
        item.representedObject = id
        return item
    }

    /// 子菜单父项。action 为 nil 的父项不会参与 AppKit 校验，空标签时仍显示可用；
    /// requiresDocument: true 时挂上校验钩子，无文档会话时随子项一并置灰。
    private func popup(
        _ title: String,
        _ submenu: NSMenu,
        requiresDocument: Bool = false,
        validationCommand: String? = nil
    ) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.submenu = submenu
        if requiresDocument {
            item.representedObject = validationCommand ?? "submenuParent"
            item.target = MenuRouter.shared
            item.action = #selector(MenuRouter.validateSubmenuParent(_:))
        }
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
final class MenuRouter: NSObject, NSMenuItemValidation, NSMenuDelegate {
    static let shared = MenuRouter()

    /// 子菜单父项的勾选/可用性规则。
    /// 父项本身不是命令：勾选由分组内的命令聚合（例如“段落样式”在正文或任一标题下都显示勾选），
    /// 可用性沿用组内代表命令，避免父项借用某一个子命令的 checked 状态。
    private struct SubmenuParent {
        let checkedCommands: [String]
        let enablementCommand: String?

        func isChecked(in session: EditorSession?) -> Bool {
            guard !checkedCommands.isEmpty else { return false }
            return checkedCommands.contains { session?.editorAction($0)?.checked == true }
        }
    }

    private static let submenuParents: [String: SubmenuParent] = [
        "paragraphStyleParent": SubmenuParent(
            checkedCommands: ["setParagraph"] + (1...6).map { "setHeading\($0)" },
            enablementCommand: "setParagraph"),
        "headingParent": SubmenuParent(
            checkedCommands: (1...6).map { "setHeading\($0)" },
            enablementCommand: "setHeading1"),
        "listParent": SubmenuParent(
            checkedCommands: ["toggleBulletList", "toggleOrderedList", "toggleTaskList"],
            enablementCommand: "toggleBulletList"),
        "alertParent": SubmenuParent(checkedCommands: [], enablementCommand: "insertAlertNote"),
        "paragraphActionsParent": SubmenuParent(checkedCommands: [], enablementCommand: "insertLineBefore"),
        "tableParent": SubmenuParent(checkedCommands: [], enablementCommand: "addRowAfter"),
        "imageParent": SubmenuParent(checkedCommands: [], enablementCommand: "saveImageAs"),
        "insertImageParent": SubmenuParent(checkedCommands: [], enablementCommand: "insertImage"),
        "copyAsParent": SubmenuParent(checkedCommands: [], enablementCommand: "copyPlain"),
        "zoomParent": SubmenuParent(checkedCommands: [], enablementCommand: "zoomIn"),
    ]

    private var session: EditorSession? { AppWindowManager.shared.activeSession }
    private var viewStateSession: EditorSession? { AppWindowManager.shared.activeViewStateSession }

    /// 原生字段编辑器和多行输入优先处理编辑命令；WebKit 内部响应者仍走编辑器桥接。
    private var nativeTextFieldEditor: NSTextView? {
        NativeTextEditingPolicy.textEditor(for: NSApp.keyWindow?.firstResponder)
    }

    private func routeNativeTextCommand(_ command: String) -> Bool {
        NativeTextEditingPolicy.perform(command, responder: NSApp.keyWindow?.firstResponder)
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        for item in menu.items where !item.isSeparatorItem {
            item.isEnabled = validateMenuItem(item)
        }
    }

    /// 视图菜单勾选状态（对应 Windows RefreshStates）。
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        // 跟随系统外观时禁用主题选择（对应偏好设置置灰）；无会话时一并置灰。
        if menuItem.action == #selector(chooseTheme(_:)) {
            return !SettingsService.shared.settings.followSystemTheme && session != nil
        }
        // 缩放/排版样式跟随文档会话，无会话时置灰。
        if menuItem.action == #selector(setZoom(_:)) || menuItem.action == #selector(chooseStyle(_:)) {
            return session != nil
        }
        guard var command = menuItem.representedObject as? String else { return true }
        if command == "closeCurrentTab" {
            menuItem.title = L10n.t(SettingsService.shared.settings.multiTabEnabled ? "关闭当前标签页" : "关闭当前文件")
        }
        let submenuParent = Self.submenuParents[command]
        if let submenuParent {
            menuItem.state = submenuParent.isChecked(in: session) ? .on : .off
            if let enablementCommand = submenuParent.enablementCommand {
                command = enablementCommand
            }
        }
        // 原生文本框（查找面板/偏好设置输入框等）编辑命令优先路由，不受空标签置灰影响。
        if let editor = nativeTextFieldEditor,
           NativeTextEditingPolicy.shouldRoute(command: command, toNativeTextFieldEditor: true) {
            return NativeTextEditingPolicy.isEnabled(
                command: command,
                editable: editor.isEditable,
                hasSelection: editor.selectedRange.length > 0,
                hasClipboard: NSPasteboard.general.string(forType: .string)?.isEmpty == false
            )
        }
        // 关闭所有标签页后：仅白名单命令保持可用，其余（保存/导出/编辑/插入/格式等）全部置灰。
        if command.hasPrefix("activateTab:") {
            let tabID = DocumentTabID(String(command.dropFirst("activateTab:".count)))
            return AppWindowManager.shared.activeWindowController?.windowSession?.tabStore.tab(withID: tabID) != nil
        }
        let isEmptyWindow: Bool?
        if AppWindowManager.shared.activeSession == nil {
            let hasWorkspace = (session ?? viewStateSession)?.workspaceRoot != nil
            let hasClosedTabs = AppWindowManager.shared.activeWindowSession?.closedTabHistory.hasRecords == true
            isEmptyWindow = MenuCommandAvailabilityPolicy.isEmptyWindowCommandEnabled(
                command: command,
                hasWindow: AppWindowManager.shared.activeWindowController != nil,
                hasWorkspace: hasWorkspace,
                hasClosedTabs: hasClosedTabs
            )
            if isEmptyWindow == false {
                menuItem.state = .off
                return false
            }
        } else {
            isEmptyWindow = nil
        }
        switch command {
        case "new", "newPlainText", "open":
            return AppWindowManager.shared.activeWindowController != nil
        case "openReadOnly":
            return AppWindowManager.shared.activeWindowController != nil
        case "openFolder":
            return AppWindowManager.shared.activeViewStateSession != nil
        case "closeFolder":
            // 没有打开工作区时禁用。
            return (session ?? viewStateSession)?.workspaceRoot != nil
        case "toggleFocusMode":
            menuItem.state = AppWindowManager.shared.activeWindowController?.isFocusMode == true ? .on : .off
            return AppWindowManager.shared.activeWindowController != nil
        case "toggleEditorFocusMode":
            guard let session else { return false }
            menuItem.state = session.isEditorFocusMode ? .on : .off
            return true
        case "toggleTypewriterMode":
            guard let session else { return false }
            menuItem.state = session.isTypewriterMode ? .on : .off
            return true
        case "toggleFullScreen":
            menuItem.state = AppWindowManager.shared.activeWindowController?.window?.styleMask.contains(.fullScreen) == true
                ? .on : .off
            return AppWindowManager.shared.activeWindowController != nil
        case "toggleReadOnlyMode":
            guard let session else { return false }
            menuItem.state = session.isReadOnly ? .on : .off
            let fileURLIsWritable = session.documentURL.map { url in
                FileManager.default.isWritableFile(atPath: url.path)
            } ?? true
            return ReadOnlyModePolicy.action(
                currentReadOnly: session.isReadOnly,
                isDirty: session.isDirty,
                fileURLIsWritable: fileURLIsWritable && !session.isReadOnlyLocked
            ) != .blockedFileNotWritable
        case "restoreClosedTab":
            return AppWindowManager.shared.canRestoreClosedTab
        case "tabManagementParent":
            guard let windowSession = AppWindowManager.shared.activeWindowSession else { return false }
            return !windowSession.tabStore.tabs.isEmpty || windowSession.closedTabHistory.hasRecords
        case "tabNext", "tabPrevious", "closeCurrentTab", "closeOtherTabs",
            "revealActiveTabInWorkspace", "copyActiveTabPath", "revealActiveTabInFinder",
            "copyActiveFileContents", "shareActiveTab":
            guard let windowSession = AppWindowManager.shared.activeWindowSession else { return false }
            return MenuCommandAvailabilityPolicy.isTabCommandEnabled(
                command: command,
                state: MenuCommandAvailabilityState(
                    tabCount: windowSession.tabStore.tabs.count,
                    activeTabPath: windowSession.tabStore.activeTab?.path,
                    workspaceRoot: windowSession.controller?.session.workspaceRoot,
                    hasContent: windowSession.activeTabSession?.hasContent ?? false
                )
            )
        case "renameActiveTab":
            let activeTabSession = AppWindowManager.shared.activeWindowSession?.activeTabSession
            return DocumentRenamePolicy.canRenameActiveDocument(
                hasFileURL: activeTabSession?.documentURL != nil,
                isReadOnly: activeTabSession?.isReadOnly == true
            )
        default:
            break
        }
        let s = session
        if let action = s?.editorAction(command) {
            if submenuParent == nil {
                menuItem.state = action.checked ? .on : .off
            }
            return s?.editorCommandEnabled(command) == true
        }
        switch command {
        case "print": return session != nil
        case "save":
            return SaveMenuPolicy.isSaveEnabled(
                hasSession: session != nil,
                isDirty: session?.isDirty == true,
                isReadOnly: session?.isReadOnly == true,
                hasFileURL: session?.documentURL != nil
            )
        case "saveAll":
            return SaveMenuPolicy.isSaveAllEnabled(
                hasSavableTarget: AppWindowManager.shared.activeWindowSession.map { windowSession in
                    SaveAllPolicy.hasTarget(tabs: windowSession.tabStore.tabs)
                } ?? false
            )
        case "saveAs": return SaveMenuPolicy.isSaveAsEnabled(hasSession: session != nil)
        case "toggleSidebar":
            menuItem.state = viewStateSession?.sidebarVisible == true ? .on : .off
            return viewStateSession != nil
        case "workspaceTab":
            menuItem.state = viewStateSession?.sidebarTabIndex == 0 ? .on : .off
            return viewStateSession != nil
        case "outlineTab":
            menuItem.state = viewStateSession?.sidebarTabIndex == 1 ? .on : .off
            return viewStateSession != nil
        case "toggleDetachedOutline":
            menuItem.state = viewStateSession?.outlineDetached == true ? .on : .off
            return viewStateSession != nil
        case "treeView":
            menuItem.state = viewStateSession?.workspaceListMode == false ? .on : .off
            return viewStateSession != nil
        case "listView":
            menuItem.state = viewStateSession?.workspaceListMode == true ? .on : .off
            return viewStateSession != nil
        case "toggleStatusBar":
            menuItem.state = viewStateSession?.statusBarVisible == true ? .on : .off
            return viewStateSession != nil
        case "toggleFollowSystemTheme":
            menuItem.state = SettingsService.shared.settings.followSystemTheme ? .on : .off
            return true
        case "toggleCodeHighlight":
            menuItem.state = SettingsService.shared.settings.showCodeHighlight ? .on : .off
        case "zoomIn", "zoomOut", "resetZoom":
            return session != nil
        default: break
        }
        if let isEmptyWindow {
            return isEmptyWindow
        }
        return s?.editorActions.isEmpty != true
    }

    @objc func performCommand(_ sender: NSMenuItem) {
        guard let command = sender.representedObject as? String else { return }
        if routeNativeTextCommand(command) { return }
        if command.hasPrefix("activateTab:") {
            let rawID = String(command.dropFirst("activateTab:".count))
            AppWindowManager.shared.activeWindowController?.activateTab(DocumentTabID(rawID), animated: true)
            return
        }
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
            // 空标签窗口也能打开/切换工作区（侧栏跟随窗口级会话）。
            guard let target = session ?? AppWindowManager.shared.activeViewStateSession else { return }
            let panel = NSOpenPanel()
            panel.title = L10n.t("打开文件夹")
            panel.canChooseFiles = false
            panel.canChooseDirectories = true
            panel.allowsMultipleSelection = false
            guard let window = target.webView?.window ?? AppWindowManager.shared.activeWindowController?.window else { return }
            panel.beginSheetModal(for: window) { response in
                if response == .OK, let url = panel.url {
                    target.loadWorkspace(url.path)
                }
            }
        case "closeFolder":
            (session ?? AppWindowManager.shared.activeViewStateSession)?.closeWorkspace()
        case "showShortcuts":
            AppWindowManager.shared.showShortcuts()
        case "new", "newPlainText":
            if let session {
                session.performMenuCommand(command)
            } else if let controller = AppWindowManager.shared.activeWindowController {
                // 空标签窗口：由窗口层直接开新标签。
                controller.newUntitledTab(kind: command == "newPlainText" ? .plainText : .markdown)
            }
        case "open":
            if let controller = AppWindowManager.shared.activeWindowController {
                controller.openDocumentPanel()
            }
        case "recoverUnsavedFiles":
            AppWindowManager.shared.showRecoveryDialog()
        case "openChangelog":
            AppWindowManager.shared.openChangelog()
        case "openWelcome":
            AppWindowManager.shared.openWelcome()
        case "showThemeSettings":
            AppWindowManager.shared.showThemeSettings()
        case "installOptionalFonts":
            AppWindowManager.shared.showOptionalFonts()
        case "codeFormatterManager":
            AppWindowManager.shared.showCodeFormatterManager()
        case "checkForUpdates":
            AppWindowManager.shared.checkForUpdates()
        case "toggleFollowSystemTheme":
            let newValue = !SettingsService.shared.settings.followSystemTheme
            SettingsService.shared.update { $0.followSystemTheme = newValue }
            AppWindowManager.shared.applyThemeModeToAll()
            NativeMenuBuilder.refreshIfNeeded()
        case "toggleCodeHighlight":
            let newValue = !SettingsService.shared.settings.showCodeHighlight
            SettingsService.shared.update { $0.showCodeHighlight = newValue }
            AppWindowManager.shared.applyPreferencesToAll()
            NativeMenuBuilder.refreshIfNeeded()
        case "toggleFocusMode":
            AppWindowManager.shared.activeWindowController?.toggleFocusMode()
        case "toggleReadOnlyMode":
            session?.toggleReadOnlyMode()
        case "learnMarkdown":
            if let url = URL(string: "https://markdown.com.cn/basic-syntax/index.html") {
                NSWorkspace.shared.open(url)
            }
        case "openSampleAlert", "openSampleYamlBasic", "openSampleYamlAdvanced":
            guard let sample = SampleDocumentResource(command: command) else { return }
            AppWindowManager.shared.openSample(sample)
        case "treeView", "listView":
            viewStateSession?.showWorkspaceView(listMode: command == "listView")
        case "toggleSidebar", "toggleStatusBar", "workspaceTab", "outlineTab", "toggleDetachedOutline":
            viewStateSession?.performMenuCommand(command)
        case "tabNext":
            AppWindowManager.shared.activeWindowController?.selectNextTab()
        case "tabPrevious":
            AppWindowManager.shared.activeWindowController?.selectNextTab(reverse: true)
        case "toggleFullScreen":
            AppWindowManager.shared.activeWindowController?.window?.toggleFullScreen(nil)
        case "closeCurrentTab":
            AppWindowManager.shared.activeWindowController?.closeCurrentTab()
        case "closeOtherTabs":
            AppWindowManager.shared.activeWindowController?.closeOtherActiveTabs()
        case "restoreClosedTab":
            AppWindowManager.shared.activeWindowController?.restoreLastClosedTab()
        case "revealActiveTabInWorkspace":
            AppWindowManager.shared.activeWindowController?.revealActiveTabInWorkspace()
        case "copyActiveTabPath":
            AppWindowManager.shared.activeWindowController?.copyActiveTabPath()
        case "revealActiveTabInFinder":
            AppWindowManager.shared.activeWindowController?.revealActiveTabInFinder()
        case "copyActiveFileContents":
            AppWindowManager.shared.activeWindowController?.copyActiveFileContents()
        case "shareActiveTab":
            AppWindowManager.shared.activeWindowController?.shareActiveTab()
        case "renameActiveTab":
            if let session, DocumentRenamePolicy.canRenameActiveDocument(
                hasFileURL: session.documentURL != nil,
                isReadOnly: session.isReadOnly
            ) {
                session.renameActiveDocument()
            }
        case "openHelp":
            if let url = URL(string: "https://github.com/zhuanshunjishi2017/markleaf/blob/main/README.md") {
                NSWorkspace.shared.open(url)
            }
        default:
            session?.performMenuCommand(command)
        }
    }

    /// “标签页管理”里由 delegate 每轮追加的动态项：末尾分隔线 + 每个打开的标签行。
    /// 两者必须在刷新时整体回收，只匹配分隔线会漏掉标签行，导致每打开一次菜单就重复一组。
    enum TabManagementDynamicItem {
        static func matches(_ item: NSMenuItem, key: String) -> Bool {
            if item.isSeparatorItem { return item.representedObject as? String == key }
            guard let identifier = item.representedObject as? String else { return false }
            return identifier.hasPrefix("activateTab:")
        }
    }

    /// 动态维护“标签页管理”中的打开标签列表。
    final class TabManagementMenuDelegate: NSObject, NSMenuDelegate {
        static let shared = TabManagementMenuDelegate()
        private let dynamicItemKey = "tabManagement.dynamicTabs"

        func menuNeedsUpdate(_ menu: NSMenu) {
            // 上一轮追加的动态项（分隔线 + 标签行）必须整体移除，否则每次打开菜单都会再追加一组标签。
            while let item = menu.items.last, TabManagementDynamicItem.matches(item, key: dynamicItemKey) {
                menu.removeItem(item)
            }

            guard let controller = AppWindowManager.shared.activeWindowController,
                  let windowSession = controller.windowSession else { return }

            let separator = NSMenuItem.separator()
            separator.representedObject = dynamicItemKey
            menu.addItem(separator)

            for tab in windowSession.tabStore.tabs {
                let item = NSMenuItem(
                title: Self.tabMenuItemTitle(
                    title: tab.title,
                    path: tab.path,
                    isDirty: tab.isDirty,
                    siblingPaths: windowSession.tabStore.tabs.map(\.path)
                ),
                    action: #selector(MenuRouter.performCommand(_:)),
                    keyEquivalent: ""
                )
                item.target = MenuRouter.shared
                item.representedObject = "activateTab:\(tab.tabID.rawValue)"
                item.state = windowSession.tabStore.activeTabID == tab.tabID ? .on : .off
                item.isEnabled = true
                menu.addItem(item)
            }
        }

    static func tabMenuItemTitle(
        title: String,
        path: String?,
        isDirty: Bool,
        siblingPaths: [String?]
    ) -> String {
        let baseTitle = isDirty ? "● " + title : title
        let duplicateCount = siblingPaths.compactMap { $0 }.count { path in
            (path as NSString).lastPathComponent == title
        }
        guard duplicateCount > 1, let path else { return baseTitle }
        let parent = (path as NSString).deletingLastPathComponent
        guard !parent.isEmpty, parent != "/" else { return baseTitle }
        return baseTitle + " — " + (parent as NSString).lastPathComponent
    }
    }

    /// “设置缩放”子菜单每次打开时按当前缩放刷新勾选，避免沿用构建菜单时的旧状态。
    final class ZoomMenuDelegate: NSObject, NSMenuDelegate {
        static let shared = ZoomMenuDelegate()

        func menuNeedsUpdate(_ menu: NSMenu) {
            guard let session = AppWindowManager.shared.activeSession else { return }
            for item in menu.items {
                guard let percent = item.representedObject as? Int else { continue }
                item.state = percent == session.zoomPercent ? .on : .off
            }
        }
    }

    /// 为仅用于展示子菜单的父级菜单项提供 target+action，使 AppKit 自动校验得以触发。
    /// 点击父级只会展开子菜单，不会执行这里的动作。
    @objc func validateSubmenuParent(_ sender: NSMenuItem) {}

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
        // NSMenu 会保留上一次动态构建的标题、分隔线和空态；必须整体重建，
        // 否则每次打开“最近项目”都会追加一组“最近文件/最近文件夹”。
        menu.removeAllItems()
        let settings = SettingsService.shared.settings
        let manager = AppWindowManager.shared
        // 最近文件：无标签时由窗口层兜底打开；最近文件夹：跟随窗口级会话。
        let canOpenFile = manager.activeSession != nil || manager.activeWindowController != nil
        let canOpenFolder = (manager.activeSession ?? manager.activeViewStateSession) != nil
        let recentFiles = settings.recordRecentFiles
            ? Array(settings.recentFiles.prefix(10))
            : []
        let recentFolders = settings.recordRecentFolders
            ? Array(settings.recentFolders.prefix(10))
            : []
        let allRecentPaths = recentFiles + recentFolders

        if settings.recordRecentFiles {
            let filesHeader = disabledItem(L10n.t("最近文件"))
            menu.addItem(filesHeader)
            let files = recentFiles
            if files.isEmpty {
                menu.addItem(disabledItem(L10n.t("(暂无)")))
            }
            for (index, path) in files.enumerated() {
                let item = recentItem("\(index + 1)  \(recentMenuTitle(path, allPaths: allRecentPaths))", "file", path)
                item.isEnabled = canOpenFile
                menu.addItem(item)
            }
        }
        if settings.recordRecentFolders {
            if settings.recordRecentFiles { menu.addItem(.separator()) }
            let foldersHeader = disabledItem(L10n.t("最近文件夹"))
            menu.addItem(foldersHeader)
            let folders = recentFolders
            if folders.isEmpty {
                menu.addItem(disabledItem(L10n.t("(暂无)")))
            }
            for (index, path) in folders.enumerated() {
                let item = recentItem("\(index + 1)  \(recentMenuTitle(path, allPaths: allRecentPaths))", "folder", path)
                item.isEnabled = canOpenFolder
                menu.addItem(item)
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

    private func recentMenuTitle(_ path: String, allPaths: [String]) -> String {
        let fileName = (path as NSString).lastPathComponent
        let duplicate = allPaths.filter {
            ($0 as NSString).lastPathComponent.caseInsensitiveCompare(fileName) == .orderedSame
        }.count > 1
        guard duplicate else { return fileName }
        let parent = ((path as NSString).deletingLastPathComponent as NSString).lastPathComponent
        return parent.isEmpty ? fileName : "\(fileName) — \(parent)"
    }

    private func disabledItem(_ title: String) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.isEnabled = false
        return item
    }

    @objc private func openRecent(_ sender: NSMenuItem) {
        guard let payload = sender.representedObject as? [String] else { return }
        let kind = payload[0]
        let path = payload[1]
        let manager = AppWindowManager.shared
        if kind == "file" {
            if let session = manager.activeSession {
                session.openRecentFile(path)
            } else if let controller = manager.activeWindowController {
                // 空标签窗口：按标签去重打开。
                controller.openFileInTab(URL(fileURLWithPath: path))
            }
        } else {
            (manager.activeSession ?? manager.activeViewStateSession)?.openRecentFolder(path)
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
        if editorAction(command)?.enabled == false
            || (isReadOnly && command == "save") {
            return
        }
        switch command {
        case "new":
            if let newTabRequest { newTabRequest(.markdown) } else { newDocument() }
        case "newPlainText":
            if let newTabRequest { newTabRequest(.plainText) } else { newDocument(kind: .plainText) }
        case "open": openDocument()
        case "openReadOnly": openDocumentReadOnly()
        case "save": saveDocument()
        case "saveAll": saveAllRequest?()
        case "saveAs": saveDocumentAs()
        case "export": exportDocument()
        case "exportWithLastSettings": exportWithLastSettings()
        case "print": printDocument()
        case "closeFolder": closeWorkspace()
        case "undo": execute("undo")
        case "redo": execute("redo")
        case "cut":
            copySelectionAs(.formatted, cut: true)
        case "copy": copySelectionAs(.formatted)
        case "copyMarkdown": copySelectionAs(.markdown)
        case "copyPlain": copySelectionAs(.plainText)
        case "copyHtml": copySelectionAs(.html)
        case "paste": pasteFromClipboard()
        case "pastePlainText": pastePlainTextFromClipboard()
        case "toggleBold", "toggleItalic", "toggleUnderline", "toggleStrike":
            execute(command)
        case "toggleHighlight": execute("toggleHighlight")
        case "find": showFind()
        case "toggleSidebar": toggleSidebar()
        case "workspaceTab": showWorkspaceTab()
        case "outlineTab": showOutlineTab()
        case "toggleDetachedOutline": toggleDetachedOutline()
        case "treeView": setWorkspaceListMode(false)
        case "listView": setWorkspaceListMode(true)
        case "toggleStatusBar": toggleStatusBar()
        case "sourceMode": toggleSourceMode()
        case "toggleReadOnlyMode": toggleReadOnlyMode()
        case "zoomIn": zoomIn()
        case "zoomOut": zoomOut()
        case "resetZoom": resetZoom()
        case "insertLink": insertLink()
        case "insertImage": insertImageFromPicker()
        case "insertImageFromUrl": insertImageFromUrl()
        case "rotateImage": execute("rotateImageClockwise")
        case "resizeImage100": execute("resizeImage", text: "100")
        case "resizeImage75": execute("resizeImage", text: "75")
        case "resizeImage90": execute("resizeImage", text: "90")
        case "resizeImage50": execute("resizeImage", text: "50")
        case "changeImage": changeImage()
        case "saveImageAs": saveImageAs()
        case "saveMermaidAs": saveMermaidAs()
        case "viewMathSource", "viewMermaidSource", "removeLink", "cancelAlert": execute(command)
        case "openLink": if let linkHref { execute("openLink", text: linkHref) }
        case "copyLinkAddress":
            if let linkHref { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(linkHref, forType: .string) }
        case "editLink": if let linkHref { insertLink(initialAddress: linkHref) }
        case "goToFootnoteDefinition", "deleteFootnoteReference": execute(command, text: footnoteReferenceLabel)
        case "clearFormat": execute("clearFormat")
        case "showShortcuts": showShortcuts()
        case "revealThemeFolder": revealThemeFolder()
        case "importTheme": importTheme()
        case "promoteHeading": execute("promoteHeading")
        case "demoteHeading": execute("demoteHeading")
        case "toggleCode":
            executeInlineFormat("toggleCode")
        case "insertMathInline": insertMath(isBlock: false)
        case "insertMathBlock": insertMath(isBlock: true)
        case "insertFootnote": insertFootnote()
        case "resetFootnoteLabel": resetFootnoteLabel()
        case "goToFootnoteReference": goToFootnoteReference()
        case "clearFootnoteReferences": clearFootnoteReferences()
        case "deleteFootnote": deleteFootnoteDefinition()
        case "insertMermaid": insertMermaid()
        case "editMermaid": editSelectedMermaid()
        case "rerenderMermaid": rerenderSelectedMermaid()
        case "rerenderAllMermaid": rerenderAllMermaid()
        case "deleteMermaid": deleteSelectedMermaid()
        case "declareCodeLanguage": declareCodeBlockLanguage()
        case "copyCodeBlock": copyEntireCodeBlock()
        case "formatCodeBlock": execute("formatCodeBlock")
        case "normalizeInlineCode": execute("normalizeInlineCode")
        case "editMath": editMath()
        case "setMathNumber": setMathNumber()
        case "convertMath": execute("convertMath")
        case "deleteMath": execute("deleteMath")
        case "editTableCaption": editTableCaption()
        case "editImageCaption": editImageCaption()
        case "selectAll": execute("selectAll")
        case "exitCode": execute("exitCode")
        case "formatPainter", "formatPainterArm", "formatPainterApply": execute(command)
        case "insertAlertNote", "insertAlertTip", "insertAlertImportant",
             "insertAlertWarning", "insertAlertCaution", "showFrontMatter":
            execute(command)
        case "toggleEditorFocusMode": toggleEditorFocusMode()
        case "toggleTypewriterMode": toggleTypewriterMode()
        case "restartEditor": restartEditor()
        case "toggleBlockquote": execute("toggleBlockquote")
        case "toggleCodeBlock": toggleCodeBlockWithLanguage()
        case "toggleBulletList", "toggleOrderedList", "toggleTaskList": execute(command)
        case "indentListItem", "outdentListItem": execute(command)
        case "insertHorizontalRule": execute("insertHorizontalRule")
        case "insertTable": execute("insertTable")
        case "insertLineBefore": execute("insertLineBefore")
        case "insertLineAfter": execute("insertLineAfter")
        case "duplicateParagraph": execute("duplicateParagraph")
        case "deleteParagraph": execute("deleteParagraph")
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
