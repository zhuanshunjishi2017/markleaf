import AppKit

struct ThemeDefaultsSelectionModel {
    let lightThemes: [ColorThemeInfo]
    let darkThemes: [ColorThemeInfo]
    let selectedLightIndex: Int
    let selectedDarkIndex: Int

    init(themes: [ColorThemeInfo], selectedLightID: String, selectedDarkID: String) {
        lightThemes = themes.filter { !$0.isDark }
        darkThemes = themes.filter(\.isDark)
        selectedLightIndex = lightThemes.firstIndex(where: { $0.id == selectedLightID }) ?? 0
        selectedDarkIndex = darkThemes.firstIndex(where: { $0.id == selectedDarkID }) ?? 0
    }
}

/// 偏好设置窗口：LyricsX 式顶部标签页（NSTabViewController + 工具栏样式）。
/// 完整对应 Windows PreferencesDialog 的 5 个分类（文件/编辑器/外观/通用/图片），即时生效。
final class PreferencesWindowController: NSWindowController {
    var onSettingsChanged: (() -> Void)?

    // 文件
    private let startupPopup = NSPopUpButton()
    private let autoSaveCheck = NSButton(checkboxWithTitle: L10n.t("自动保存文件"), target: nil, action: nil)
    private let saveOnSwitchCheck = NSButton(checkboxWithTitle: L10n.t("切换文档时自动保存"), target: nil, action: nil)
    private let snapshotIntervalField = NSTextField(string: "30")
    private let newLinePopup = NSPopUpButton()
    private let recordRecentFilesCheck = NSButton(checkboxWithTitle: L10n.t("记录最近文件"), target: nil, action: nil)
    private let recordRecentFoldersCheck = NSButton(checkboxWithTitle: L10n.t("记录最近文件夹"), target: nil, action: nil)

    // 编辑器
    private let lineHeightField = NSTextField(string: "1.6")
    private let fontSizeField = NSTextField(string: "16")
    private let maxWidthField = NSTextField(string: "820")
    private let sourceFontSizeField = NSTextField(string: "14")
    private var sourceFontField: FontField!
    private var sourceCjkFontField: FontField!
    private let sourceIndentField = NSTextField(string: "2")
    private let cjkLanguagePopup = NSPopUpButton()
    private let fontSettingsButton = NSButton(title: L10n.t("字体设置…"), target: nil, action: nil)
    private let fontSettingsSummary = NSTextField(labelWithString: "")

    // 外观
    private let stylePopup = NSPopUpButton()
    private let themePopup = NSPopUpButton()
    private let restoreZoomCheck = NSButton(checkboxWithTitle: L10n.t("打开时还原上次的缩放比例"), target: nil, action: nil)
    // 触控板捏合始终可用；此开关仅控制 ⌘ + 滚轮缩放
    private let ctrlWheelZoomCheck = NSButton(checkboxWithTitle: L10n.t("使用 ⌘ + 滚轮进行缩放"), target: nil, action: nil)
    private let topMostCheck = NSButton(checkboxWithTitle: L10n.t("将窗口置于顶层"), target: nil, action: nil)
    private let followSystemCheck = NSButton(checkboxWithTitle: L10n.t("与操作系统同步"), target: nil, action: nil)
    private let defaultLightThemePopup = NSPopUpButton()
    private let defaultDarkThemePopup = NSPopUpButton()
    private let themeRow: NSStackView = {
        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = 8
        return stack
    }()

    // 通用
    private let languagePopup = NSPopUpButton()
    private let associateMDCheck = NSButton(checkboxWithTitle: L10n.t("Markdown 文件 (.md / .markdown)"), target: nil, action: nil)
    private let associateTextCheck = NSButton(checkboxWithTitle: L10n.t("纯文本文件 (.txt)"), target: nil, action: nil)

    // 图片
    private let clipboardImagePopup = NSPopUpButton()
    private let fileImagePopup = NSPopUpButton()
    private let imageDirectoryField = NSTextField(string: "")
    private let useRelativePathsCheck = NSButton(checkboxWithTitle: L10n.t("在可用时使用相对路径"), target: nil, action: nil)
    private let prefixDotSlashCheck = NSButton(checkboxWithTitle: L10n.t("相对路径前加 \"./\""), target: nil, action: nil)

    private var styleIDs: [String] = []
    private var themeIDs: [String] = []
    private var defaultLightThemeIDs: [String] = []
    private var defaultDarkThemeIDs: [String] = []


    /// 表单网格行：组标题 / 提示 / 标签+控件
    private enum FormRow {
        case header(String)
        case hint(String)
        case field(String, NSView)
    }

    init(styles: [StyleDefinition], themes: [ColorThemeInfo]) {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 640, height: 540),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("偏好设置")
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)

        let settings = SettingsService.shared.settings
        styleIDs = styles.map(\.id)
        themeIDs = themes.map(\.id)

        // ---- 控件初值 ----
        startupPopup.addItems(withTitles: [L10n.t("新建文档"), L10n.t("打开上次工作区"), L10n.t("打开上次工作区及文件")])
        startupPopup.selectItem(at: settings.startupAction == .newDocument ? 0
                                : settings.startupAction == .openLastWorkspace ? 1 : 2)
        autoSaveCheck.state = settings.autoSaveEnabled ? .on : .off
        saveOnSwitchCheck.state = settings.saveOnDocumentSwitch ? .on : .off
        snapshotIntervalField.stringValue = "\(settings.snapshotIntervalSeconds)"
        newLinePopup.addItems(withTitles: ["CRLF", "LF"])
        newLinePopup.selectItem(at: settings.newLineStyle == "crlf" ? 0 : 1)
        recordRecentFilesCheck.state = settings.recordRecentFiles ? .on : .off
        recordRecentFoldersCheck.state = settings.recordRecentFolders ? .on : .off

        lineHeightField.stringValue = String(format: "%.2f", settings.visualLineHeight)
        fontSizeField.stringValue = "\(settings.visualFontSize)"
        maxWidthField.stringValue = "\(settings.visualMaxContentWidth)"
        sourceFontSizeField.stringValue = "\(settings.sourceFontSize)"
        sourceFontField = FontField(fontName: settings.sourceFontFamily) { [weak self] _ in
            self?.controlChanged()
        }
        sourceCjkFontField = FontField(fontName: settings.sourceCjkFontFamily) { [weak self] _ in
            self?.controlChanged()
        }
        sourceIndentField.stringValue = "\(settings.sourceIndentWidth)"
        cjkLanguagePopup.addItems(withTitles: [
            L10n.t("简体中文"), L10n.t("繁体中文"), L10n.t("日文字形"), L10n.t("韩文字形"),
        ])
        cjkLanguagePopup.selectItem(at: CJKLanguageTag.allCases.firstIndex(of: settings.cjkLanguageTag) ?? 0)
        updateFontSettingsSummary()
        fontSettingsButton.target = self
        fontSettingsButton.action = #selector(openFontSettings)

        stylePopup.addItems(withTitles: styles.map { L10n.t($0.displayName) })
        if let idx = styles.firstIndex(where: { $0.id == settings.markdownStyle }) {
            stylePopup.selectItem(at: idx)
        }
        themePopup.addItems(withTitles: themes.map { L10n.t($0.displayName) })
        if let idx = themes.firstIndex(where: { $0.id == settings.colorTheme }) {
            themePopup.selectItem(at: idx)
        }
        themeRow.addView(themePopup, in: .leading)
        themeRow.addView(followSystemCheck, in: .trailing)
        restoreZoomCheck.state = settings.restoreZoomOnOpen ? .on : .off
        ctrlWheelZoomCheck.state = settings.ctrlWheelZoom ? .on : .off
        topMostCheck.state = settings.topMostWindow ? .on : .off
        followSystemCheck.state = settings.followSystemTheme ? .on : .off
        themePopup.isEnabled = !settings.followSystemTheme
        let themeDefaults = ThemeDefaultsSelectionModel(
            themes: themes,
            selectedLightID: settings.defaultLightThemeID,
            selectedDarkID: settings.defaultDarkThemeID
        )
        defaultLightThemeIDs = themeDefaults.lightThemes.map(\.id)
        defaultDarkThemeIDs = themeDefaults.darkThemes.map(\.id)
        defaultLightThemePopup.addItems(withTitles: themeDefaults.lightThemes.map { L10n.t($0.displayName) })
        defaultDarkThemePopup.addItems(withTitles: themeDefaults.darkThemes.map { L10n.t($0.displayName) })
        if !defaultLightThemeIDs.isEmpty { defaultLightThemePopup.selectItem(at: themeDefaults.selectedLightIndex) }
        if !defaultDarkThemeIDs.isEmpty { defaultDarkThemePopup.selectItem(at: themeDefaults.selectedDarkIndex) }
        defaultLightThemePopup.isEnabled = settings.followSystemTheme
        defaultDarkThemePopup.isEnabled = settings.followSystemTheme

        // i18n：简体中文 / 繁體中文 / English
        let currentLang = settings.displayLanguage
        let languageCodes = ["zh-Hans", "zh-Hant", "en", "ja"]
        languagePopup.removeAllItems()
        for code in languageCodes {
            languagePopup.addItem(withTitle: L10n.langDisplayName(code, currentLang: currentLang))
        }
        if let idx = languageCodes.firstIndex(of: currentLang) {
            languagePopup.selectItem(at: idx)
        }
        associateMDCheck.state = settings.associateMarkdownFiles ? .on : .off
        associateTextCheck.state = settings.associateTextFiles ? .on : .off

        // 与 Windows 版一致：「上传图片」未实现，从列表移除
        clipboardImagePopup.addItems(withTitles: [L10n.t("保存到默认目录"), L10n.t("复制到文档资源")])
        clipboardImagePopup.selectItem(at: settings.clipboardImageHandling == "copyToAssets" ? 1 : 0)
        fileImagePopup.addItems(withTitles: [L10n.t("引用原位置"), L10n.t("复制到文档资源")])
        fileImagePopup.selectItem(at: settings.fileImageHandling == "copyToAssets" ? 1 : 0)
        imageDirectoryField.stringValue = settings.imageDefaultDirectory
        imageDirectoryField.placeholderString = L10n.t("默认 ~/Pictures/MarkLeaf")
        useRelativePathsCheck.state = settings.useRelativePaths ? .on : .off
        prefixDotSlashCheck.state = settings.prefixRelativeWithDotSlash ? .on : .off

        // 控件样式：文本框圆角，数字框居中
        for field in [snapshotIntervalField, lineHeightField, fontSizeField, maxWidthField,
                      sourceFontSizeField, sourceIndentField] {
            field.bezelStyle = .roundedBezel
            field.alignment = .center
            field.widthAnchor.constraint(equalToConstant: 70).isActive = true
        }
        imageDirectoryField.bezelStyle = .roundedBezel

        // ---- 标签页（LyricsX 式：NSTabViewController + 工具栏样式） ----
        let tabViewController = NSTabViewController()
        tabViewController.tabStyle = .toolbar
        let tabs: [(String, String, NSView)] = [
            (L10n.t("文件"), "doc.text", filePage()),
            (L10n.t("编辑器"), "textformat", editorPage()),
            (L10n.t("外观"), "paintbrush", appearancePage()),
            (L10n.t("通用"), "gearshape", generalPage()),
            (L10n.t("图片"), "photo", imagePage()),
        ]
        for (title, icon, view) in tabs {
            let page = NSViewController()
            page.title = title
            page.view = view
            let item = NSTabViewItem(viewController: page)
            item.image = NSImage(systemSymbolName: icon, accessibilityDescription: title)
            tabViewController.addTabViewItem(item)
        }
        window.contentViewController = tabViewController

        // 数值字段：数字格式化并限制到 Windows 对应范围
        Self.configureNumberField(snapshotIntervalField, min: Double(AppSettings.snapshotIntervalRange.lowerBound), max: Double(AppSettings.snapshotIntervalRange.upperBound))
        Self.configureNumberField(lineHeightField, min: AppSettings.visualLineHeightRange.lowerBound, max: AppSettings.visualLineHeightRange.upperBound, fractionDigits: 2)
        Self.configureNumberField(fontSizeField, min: Double(AppSettings.visualFontSizeRange.lowerBound), max: Double(AppSettings.visualFontSizeRange.upperBound))
        Self.configureNumberField(maxWidthField, min: Double(AppSettings.visualMaxContentWidthRange.lowerBound), max: Double(AppSettings.visualMaxContentWidthRange.upperBound))
        Self.configureNumberField(sourceFontSizeField, min: Double(AppSettings.sourceFontSizeRange.lowerBound), max: Double(AppSettings.sourceFontSizeRange.upperBound))
        Self.configureNumberField(sourceIndentField, min: Double(AppSettings.sourceIndentWidthRange.lowerBound), max: Double(AppSettings.sourceIndentWidthRange.upperBound))

        // 绑定
        let controls: [NSControl] = [startupPopup, autoSaveCheck, saveOnSwitchCheck, newLinePopup, recordRecentFilesCheck,
                                     recordRecentFoldersCheck, stylePopup, themePopup,
                                     defaultLightThemePopup, defaultDarkThemePopup,
                                     cjkLanguagePopup,
                                     restoreZoomCheck, ctrlWheelZoomCheck, topMostCheck, followSystemCheck, languagePopup,
                                     associateMDCheck, associateTextCheck, clipboardImagePopup, fileImagePopup,
                                     useRelativePathsCheck, prefixDotSlashCheck]
        for control in controls {
            control.target = self
            control.action = #selector(controlChanged)
        }
        for field in [snapshotIntervalField, lineHeightField, fontSizeField, maxWidthField,
                      sourceFontSizeField, sourceIndentField, imageDirectoryField] {
            field.target = self
            field.action = #selector(controlChanged)
        }
    }

    private static func configureNumberField(_ field: NSTextField, min: Double, max: Double, fractionDigits: Int = 0) {
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        formatter.minimum = NSNumber(value: min)
        formatter.maximum = NSNumber(value: max)
        formatter.maximumFractionDigits = fractionDigits
        formatter.minimumFractionDigits = fractionDigits
        field.formatter = formatter
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    // MARK: - 分类页（NSGridView 网格排布）

    private func filePage() -> NSView {
        formPage(rows: [
            .header(L10n.t("启动")),
            .field(L10n.t("启动操作"), startupPopup),
            .header(L10n.t("保存选项")),
            .field("", autoSaveCheck),
            .field("", saveOnSwitchCheck),
            .field(L10n.t("快照保存间隔"), fieldRow(snapshotIntervalField, unit: L10n.t("秒"))),
            .field("", linkButton(L10n.t("恢复未保存的文档…"), #selector(recoverUnsavedFiles))),
            .header(L10n.t("换行风格")),
            .field(L10n.t("文件换行风格"), newLinePopup),
            .hint(L10n.t("此设置项仅控制新建文件的换行符，打开的文件将保留其原有换行风格。")),
            .header(L10n.t("历史记录")),
            .field("", recordRecentFilesCheck),
            .field("", recordRecentFoldersCheck),
            .field("", linkButton(L10n.t("清除历史记录…"), #selector(clearHistory))),
        ])
    }

    private func editorPage() -> NSView {
        formPage(rows: [
            .header(L10n.t("可视化")),
            .field(L10n.t("基础行高"), lineHeightField),
            .field(L10n.t("基础字号"), fontSizeField),
            .field(L10n.t("最大内容宽度"), fieldRow(maxWidthField, unit: "px")),
            .header(L10n.t("源码模式")),
            .field(L10n.t("字体设置"), NSStackView(views: [fontSettingsSummary, fontSettingsButton])),
            .field(L10n.t("汉字优先字型"), cjkLanguagePopup),
            .field(L10n.t("默认缩进宽度"), sourceIndentField),
            .header(L10n.t("缩放视图")),
            .field("", restoreZoomCheck),
            .field("", ctrlWheelZoomCheck),
            .hint(L10n.t("部分排版设置可能由当前的排版样式接管，可到「外观」更改。")),
        ])
    }

    private func appearancePage() -> NSView {
        formPage(rows: [
            .header(L10n.t("文档外观")),
            .field(L10n.t("排版样式"), stylePopup),
            .field(L10n.t("颜色主题"), themeRow),
            .field(L10n.t("默认浅色主题"), defaultLightThemePopup),
            .field(L10n.t("默认深色主题"), defaultDarkThemePopup),
            .field("", linkButton(L10n.t("添加主题…"), #selector(importTheme))),
            .field("", linkButton(L10n.t("打开主题文件夹…"), #selector(openThemeFolder))),
            .header(L10n.t("窗口设置")),
            .field("", topMostCheck),
        ])
    }

    private func generalPage() -> NSView {
        formPage(rows: [
            .header(L10n.t("显示")),
            .field(L10n.t("显示语言"), languagePopup),
            .header(L10n.t("文件关联")),
            .field("", associateMDCheck),
            .field("", associateTextCheck),
            .header(L10n.t("储存管理")),
            .field("", linkButton(L10n.t("打开设置/缓存目录…"), #selector(openSettingsFolder))),
            .header(L10n.t("日志管理")),
            .field("", linkButton(L10n.t("打开日志目录…"), #selector(openLogFolder))),
            .field("", linkButton(L10n.t("清除日志"), #selector(clearLogs))),
            .header(L10n.t("高级")),
            .field("", linkButton(L10n.t("配置 JSON 文件…"), #selector(revealSettingsJSON))),
            .field("", linkButton(L10n.t("恢复默认设置…"), #selector(resetAll))),
        ])
    }

    private func imagePage() -> NSView {
        formPage(rows: [
            .header(L10n.t("剪贴板图片")),
            .field(L10n.t("处理方式"), clipboardImagePopup),
            .header(L10n.t("文件图片")),
            .field(L10n.t("处理方式"), fileImagePopup),
            .header(L10n.t("默认目录")),
            .field("", imageDirectoryField),
            .field("", linkButton(L10n.t("浏览…"), #selector(browseImageDirectory))),
            .field("", useRelativePathsCheck),
            .field("", prefixDotSlashCheck),
            .hint(L10n.t("相对路径仅在文档已保存到本地时生效；文档未保存时“复制到文档资源”会回退到默认目录。")),
        ])
    }

    // MARK: - Actions

    /// 同步「与操作系统同步」开关状态（外观菜单切换后保持偏好设置一致）。
    func syncFollowSystemThemeState() {
        followSystemCheck.state = SettingsService.shared.settings.followSystemTheme ? .on : .off
        themePopup.isEnabled = !SettingsService.shared.settings.followSystemTheme
        defaultLightThemePopup.isEnabled = SettingsService.shared.settings.followSystemTheme
        defaultDarkThemePopup.isEnabled = SettingsService.shared.settings.followSystemTheme
    }

    @objc private func controlChanged() {
        let oldLanguage = SettingsService.shared.settings.displayLanguage
        SettingsService.shared.update { settings in
            if startupPopup.indexOfSelectedItem >= 0 {
                settings.startupAction = switch startupPopup.indexOfSelectedItem {
                case 1: .openLastWorkspace
                case 2: .openLastWorkspaceAndFiles
                default: .newDocument
                }
            }
            settings.autoSaveEnabled = autoSaveCheck.state == .on
            settings.saveOnDocumentSwitch = saveOnSwitchCheck.state == .on
            settings.snapshotIntervalSeconds = max(5, Int(snapshotIntervalField.stringValue) ?? 30)
            settings.newLineStyle = newLinePopup.indexOfSelectedItem == 0 ? "crlf" : "lf"
            settings.recordRecentFiles = recordRecentFilesCheck.state == .on
            settings.recordRecentFolders = recordRecentFoldersCheck.state == .on

            settings.visualLineHeight = Double(lineHeightField.stringValue) ?? 1.6
            settings.visualFontSize = Int(fontSizeField.stringValue) ?? 16
            settings.visualMaxContentWidth = Int(maxWidthField.stringValue) ?? 820
            settings.sourceFontSize = Int(sourceFontSizeField.stringValue) ?? 14
            settings.sourceFontFamily = sourceFontField.fontName
            settings.sourceCjkFontFamily = sourceCjkFontField.fontName
            if cjkLanguagePopup.indexOfSelectedItem >= 0,
               cjkLanguagePopup.indexOfSelectedItem < CJKLanguageTag.allCases.count {
                settings.cjkLanguageTag = CJKLanguageTag.allCases[cjkLanguagePopup.indexOfSelectedItem]
            }
            settings.sourceIndentWidth = Int(sourceIndentField.stringValue) ?? 2

            if stylePopup.indexOfSelectedItem >= 0, stylePopup.indexOfSelectedItem < styleIDs.count {
                settings.markdownStyle = styleIDs[stylePopup.indexOfSelectedItem]
            }
            if themePopup.indexOfSelectedItem >= 0, themePopup.indexOfSelectedItem < themeIDs.count {
                settings.colorTheme = themeIDs[themePopup.indexOfSelectedItem]
            }
            settings.restoreZoomOnOpen = restoreZoomCheck.state == .on
            settings.ctrlWheelZoom = ctrlWheelZoomCheck.state == .on
            settings.topMostWindow = topMostCheck.state == .on
            settings.followSystemTheme = followSystemCheck.state == .on
            if defaultLightThemePopup.indexOfSelectedItem >= 0,
               defaultLightThemePopup.indexOfSelectedItem < defaultLightThemeIDs.count {
                settings.defaultLightThemeID = defaultLightThemeIDs[defaultLightThemePopup.indexOfSelectedItem]
            }
            if defaultDarkThemePopup.indexOfSelectedItem >= 0,
               defaultDarkThemePopup.indexOfSelectedItem < defaultDarkThemeIDs.count {
                settings.defaultDarkThemeID = defaultDarkThemeIDs[defaultDarkThemePopup.indexOfSelectedItem]
            }

            settings.associateMarkdownFiles = associateMDCheck.state == .on
            settings.associateTextFiles = associateTextCheck.state == .on

            settings.clipboardImageHandling = clipboardImagePopup.indexOfSelectedItem == 1 ? "copyToAssets" : "saveToDefault"
            settings.fileImageHandling = fileImagePopup.indexOfSelectedItem == 1 ? "copyToAssets" : "referenceOriginal"
            settings.imageDefaultDirectory = imageDirectoryField.stringValue.trimmingCharacters(in: .whitespaces)
            settings.useRelativePaths = useRelativePathsCheck.state == .on
            settings.prefixRelativeWithDotSlash = prefixDotSlashCheck.state == .on

            // 显示语言（i18n）
            let languageCodes = ["zh-Hans", "zh-Hant", "en", "ja"]
            if languagePopup.indexOfSelectedItem >= 0, languagePopup.indexOfSelectedItem < languageCodes.count {
                settings.displayLanguage = languageCodes[languagePopup.indexOfSelectedItem]
            }
            // 数值设置夹紧到 Windows 对应范围（对齐 NumericUpDown 边界）
            settings.clampSettingRanges()
        }
        let languageChanged = SettingsService.shared.settings.displayLanguage != oldLanguage
        // 跟随系统开关变化：更新下拉可用性并立即刷新各窗口主题
        themePopup.isEnabled = followSystemCheck.state != .on
        defaultLightThemePopup.isEnabled = followSystemCheck.state == .on
        defaultDarkThemePopup.isEnabled = followSystemCheck.state == .on
        AppWindowManager.shared.applyThemeModeToAll()
        onSettingsChanged?()
        // 文件关联开关变更 → 立即应用（绑定/还原默认打开程序）
        FileAssociationService.shared.apply(settings: SettingsService.shared.settings)
        // 语言变更 → 立即生效（重建菜单/偏好设置/各窗口 + 前端）
        if languageChanged {
            DispatchQueue.main.async {
                AppWindowManager.shared.applyLanguage()
            }
        }
    }

    @objc private func importTheme() {
        AppWindowManager.shared.activeSession?.importTheme()
    }

    @objc private func openFontSettings() {
        let dialog = FontSettingsWindowController(
            cjkFontFamily: sourceCjkFontField.fontName,
            westernFontFamily: sourceFontField.fontName,
            fontSize: Int(sourceFontSizeField.stringValue) ?? 14
        )
        guard dialog.runModal() else { return }
        sourceCjkFontField = FontField(fontName: dialog.cjkFontFamily) { _ in }
        sourceFontField = FontField(fontName: dialog.westernFontFamily) { _ in }
        sourceFontSizeField.stringValue = "\(dialog.fontSize)"
        updateFontSettingsSummary()
        controlChanged()
    }

    private func updateFontSettingsSummary() {
        fontSettingsSummary.stringValue = "\(sourceCjkFontField.fontName) / \(sourceFontField.fontName) / \(sourceFontSizeField.stringValue)px"
        fontSettingsSummary.textColor = .secondaryLabelColor
    }

    @objc private func recoverUnsavedFiles() {
        AppWindowManager.shared.showRecoveryDialog()
    }

    @objc private func clearHistory() {
        SettingsService.shared.update {
            $0.recentFiles = []
            $0.recentFolders = []
            $0.lastFile = nil
            $0.lastFolder = nil
        }
        infoAlert(L10n.t("历史记录已清除"))
    }

    @objc private func openThemeFolder() {
        // 打开用户主题目录（可写，放入自定义 colors-*.css 后重启生效）
        if let dir = ResourceLocator.userThemesDirectory {
            NSWorkspace.shared.open(dir)
        }
    }

    @objc private func openSettingsFolder() {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        let dir = base.appendingPathComponent("MarkLeaf", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        NSWorkspace.shared.activateFileViewerSelecting([dir])
    }

    @objc private func openLogFolder() {
        let logURL = URL(fileURLWithPath: "/tmp/markleaf-app.log")
        if FileManager.default.fileExists(atPath: logURL.path) {
            NSWorkspace.shared.activateFileViewerSelecting([logURL])
        } else {
            infoAlert(L10n.t("尚未产生日志文件"))
        }
    }

    @objc private func clearLogs() {
        try? FileManager.default.removeItem(atPath: "/tmp/markleaf-app.log")
        infoAlert(L10n.t("日志已清除"))
    }

    @objc private func revealSettingsJSON() {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        let url = base.appendingPathComponent("MarkLeaf/settings.json")
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        NSWorkspace.shared.activateFileViewerSelecting([url])
    }

    @objc private func browseImageDirectory() {
        let panel = NSOpenPanel()
        panel.title = L10n.t("选择图片默认目录")
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window!) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.imageDirectoryField.stringValue = url.path
            self?.controlChanged()
        }
    }

    @objc private func resetAll() {
        let alert = NSAlert()
        alert.messageText = L10n.t("重置所有设置为默认值？")
        alert.informativeText = L10n.t("此操作会恢复默认设置，包括外观、缩放与图片设置。")
        alert.alertStyle = .warning
        alert.addButton(withTitle: L10n.t("重置"))
        alert.addButton(withTitle: L10n.t("取消"))
        // 重置为破坏性操作：按钮标红（macOS 11+）
        alert.buttons.first?.hasDestructiveAction = true
        alert.beginSheetModal(for: window!) { [weak self] response in
            guard response == .alertFirstButtonReturn else { return }
            SettingsService.shared.update { $0 = AppSettings() }
            self?.onSettingsChanged?()
            self?.window?.close()
        }
    }

    // MARK: - UI helpers

    private func infoAlert(_ message: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("好"))
        alert.beginSheetModal(for: window!)
    }

    private func formPage(rows: [FormRow]) -> NSView {
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 28, bottom: 20, right: 28)
        stack.translatesAutoresizingMaskIntoConstraints = false

        for row in rows {
            switch row {
            case .header(let title):
                stack.addArrangedSubview(paddedHeader(title))
            case .hint(let text):
                stack.addArrangedSubview(hintLabel(text))
            case .field(let title, let control):
                stack.addArrangedSubview(fieldRow(title, control))
            }
        }

        let container = NSView()
        container.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: container.bottomAnchor),
        ])
        return container
    }

    private func fieldRow(_ title: String, _ control: NSView) -> NSView {
        let row = NSStackView()
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 12

        let label = NSTextField(labelWithString: title)
        label.font = .systemFont(ofSize: 13)
        label.alignment = .right
        label.textColor = .labelColor
        label.focusRingType = .none
        label.widthAnchor.constraint(equalToConstant: 140).isActive = true
        row.addArrangedSubview(label)

        row.addArrangedSubview(control)
        return row
    }

    private func sectionHeader(_ title: String) -> NSTextField {
        let label = NSTextField(labelWithString: title)
        label.font = .boldSystemFont(ofSize: 12)
        label.textColor = .secondaryLabelColor
        return label
    }

    /// 组标题：上方加内边距，与上一组分隔。
    private func paddedHeader(_ title: String) -> NSView {
        let wrapper = NSView()
        let header = sectionHeader(title)
        header.translatesAutoresizingMaskIntoConstraints = false
        wrapper.addSubview(header)
        NSLayoutConstraint.activate([
            header.leadingAnchor.constraint(equalTo: wrapper.leadingAnchor),
            header.trailingAnchor.constraint(lessThanOrEqualTo: wrapper.trailingAnchor),
            header.topAnchor.constraint(equalTo: wrapper.topAnchor, constant: 14),
            header.bottomAnchor.constraint(equalTo: wrapper.bottomAnchor),
        ])
        return wrapper
    }

    private func hintLabel(_ text: String) -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: text)
        label.font = .systemFont(ofSize: 11)
        label.textColor = .tertiaryLabelColor
        label.maximumNumberOfLines = 2
        label.preferredMaxLayoutWidth = 420
        return label
    }

    private func fieldLabel(_ title: String) -> NSTextField {
        guard !title.isEmpty else {
            return NSTextField(labelWithString: "")
        }
        let label = NSTextField(labelWithString: title)
        label.font = .systemFont(ofSize: 13)
        label.alignment = .right
        label.textColor = .labelColor
        return label
    }

    private func linkButton(_ title: String, _ action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.bezelStyle = .rounded
        return button
    }

    private func fieldRow(_ field: NSTextField, unit: String) -> NSView {
        field.widthAnchor.constraint(equalToConstant: 80).isActive = true
        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.spacing = 6
        stack.alignment = .centerY
        stack.addArrangedSubview(field)
        let unitLabel = NSTextField(labelWithString: unit)
        unitLabel.font = .systemFont(ofSize: 12)
        unitLabel.textColor = .secondaryLabelColor
        stack.addArrangedSubview(unitLabel)
        return stack
    }
}
