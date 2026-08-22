import AppKit

/// 查找与替换面板：原生弹出式窗口（NSPanel），控件美观、出现带淡入动画，文案随语言切换。
/// 通过命令驱动前端查找逻辑：findText / replaceOne / replaceAll / findClose，结果经 onFindResult 回显。
final class FindPanelController: NSWindowController, NSTextFieldDelegate, NSSearchFieldDelegate {
    private weak var session: EditorSession?
    private var isReplaceExpanded = false

    private let disclosureButton = NSButton()
    private let searchField = NSSearchField()
    private let replaceField = NSTextField()
    private let caseCheck = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let wholeCheck = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let prevButton = NSButton(title: "", target: nil, action: nil)
    private let nextButton = NSButton(title: "", target: nil, action: nil)
    private let replaceButton = NSButton(title: "", target: nil, action: nil)
    private let replaceAllButton = NSButton(title: "", target: nil, action: nil)
    private let closeButton = NSButton(title: "", target: nil, action: nil)
    private let resultLabel = NSTextField(labelWithString: "0/0")
    private lazy var replaceRow = NSStackView(views: [replaceField, replaceButton, replaceAllButton])
    private var replaceRowHeightConstraint: NSLayoutConstraint?
    private var replaceTopSpacingConstraint: NSLayoutConstraint?
    private var closeObserver: NSObjectProtocol?
    private var keyEventMonitor: Any?
    private var mouseEventMonitor: Any?

    init(session: EditorSession?) {
        self.session = session
        let window = NSPanel(
            contentRect: NSRect(
                x: 0,
                y: 0,
                width: 444,
                height: FindPanelLayout.contentHeight(isReplaceExpanded: false)
            ),
            styleMask: [.titled, .closable, .utilityWindow],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("查找与替换")
        window.isFloatingPanel = true
        window.level = .floating
        window.hidesOnDeactivate = false
        window.collectionBehavior = [.fullScreenAuxiliary]
        super.init(window: window)
        // 标题栏关闭按钮与 ⌘W 走窗口自身的关闭路径，不会触发 closeClicked；
        // 统一监听 willClose 通知前端清理查找高亮，避免关闭面板后蓝色高亮残留。
        closeObserver = NotificationCenter.default.addObserver(
            forName: NSWindow.willCloseNotification,
            object: window,
            queue: .main
        ) { [weak self] _ in
            self?.session?.execute("findClose")
        }
        buildContent()
        applyLanguage()
        window.center()
    }

    deinit {
        if let closeObserver {
            NotificationCenter.default.removeObserver(closeObserver)
        }
        if let keyEventMonitor {
            NSEvent.removeMonitor(keyEventMonitor)
        }
        if let mouseEventMonitor {
            NSEvent.removeMonitor(mouseEventMonitor)
        }
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func buildContent() {
        guard let window else { return }

        disclosureButton.isBordered = false
        disclosureButton.imagePosition = .imageOnly
        disclosureButton.controlSize = .small
        disclosureButton.target = self
        disclosureButton.action = #selector(toggleReplaceExpanded)
        disclosureButton.setButtonType(.momentaryChange)
        updateDisclosureAppearance()

        searchField.placeholderString = L10n.t("查找")
        searchField.controlSize = .regular
        searchField.target = self
        searchField.action = #selector(searchChanged)
        searchField.sendsSearchStringImmediately = true
        searchField.delegate = self

        replaceField.placeholderString = L10n.t("替换为")
        replaceField.controlSize = .regular
        replaceField.bezelStyle = .roundedBezel
        replaceField.target = self
        replaceField.action = #selector(replaceClicked)
        replaceField.delegate = self
        installTextEditingSupport()

        for button in [prevButton, nextButton, replaceButton, replaceAllButton] {
            button.bezelStyle = .rounded
            button.controlSize = .regular
            button.target = self
        }
        prevButton.action = #selector(prevClicked)
        nextButton.action = #selector(nextClicked)
        replaceButton.action = #selector(replaceClicked)
        replaceAllButton.action = #selector(replaceAllClicked)

        caseCheck.target = self
        caseCheck.action = #selector(optionChanged)
        wholeCheck.target = self
        wholeCheck.action = #selector(optionChanged)

        resultLabel.font = .monospacedDigitSystemFont(ofSize: 12, weight: .regular)
        resultLabel.textColor = .secondaryLabelColor
        resultLabel.alignment = .right

        closeButton.bezelStyle = .rounded
        closeButton.controlSize = .regular
        closeButton.target = self
        closeButton.action = #selector(closeClicked)

        // 布局：折叠按钮 + 查找行；展开后以动画显示替换行；底部为查找选项。
        let findRow = NSStackView(views: [disclosureButton, searchField, prevButton, nextButton, closeButton])
        findRow.orientation = .horizontal
        findRow.spacing = 6
        findRow.translatesAutoresizingMaskIntoConstraints = false

        replaceRow.orientation = .horizontal
        replaceRow.spacing = 6
        replaceRow.translatesAutoresizingMaskIntoConstraints = false

        let optionsRow = NSStackView(views: [caseCheck, wholeCheck, NSView(), resultLabel])
        optionsRow.orientation = .horizontal
        optionsRow.spacing = 12
        optionsRow.translatesAutoresizingMaskIntoConstraints = false

        let contentView = NSView()
        contentView.addSubview(findRow)
        contentView.addSubview(replaceRow)
        contentView.addSubview(optionsRow)
        window.contentView = contentView

        let replaceTop = replaceRow.topAnchor.constraint(
            equalTo: findRow.bottomAnchor,
            constant: FindPanelLayout.replaceTopSpacing(isExpanded: false)
        )
        let replaceHeight = replaceRow.heightAnchor.constraint(
            equalToConstant: FindPanelLayout.replaceRowHeight(isExpanded: false)
        )
        NSLayoutConstraint.activate([
            disclosureButton.widthAnchor.constraint(equalToConstant: 18),
            searchField.widthAnchor.constraint(equalToConstant: 190),
            replaceField.widthAnchor.constraint(equalToConstant: 214),
            findRow.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 14),
            findRow.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -14),
            findRow.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 12),
            replaceRow.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 38),
            replaceRow.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -14),
            replaceTop,
            replaceHeight,
            optionsRow.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 14),
            optionsRow.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -14),
            optionsRow.topAnchor.constraint(equalTo: replaceRow.bottomAnchor, constant: 8),
            optionsRow.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -12),
        ])

        replaceRowHeightConstraint = replaceHeight
        replaceTopSpacingConstraint = replaceTop
        replaceRow.alphaValue = 0
        replaceRow.isHidden = true
    }

    /// 查找面板不是 WKWebView，必须把编辑快捷键明确交给当前字段编辑器。
    /// 同时提供完整、足够宽的上下文菜单，避免系统紧凑菜单把粘贴等操作折叠掉。
    private func installTextEditingSupport() {
        let menu = makeTextEditingMenu()
        searchField.menu = menu
        replaceField.menu = makeTextEditingMenu()

        keyEventMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self.window else { return event }
            guard let editor = self.focusedFieldEditor else { return event }
            let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            guard modifiers.contains(.command),
                  let key = event.charactersIgnoringModifiers?.lowercased() else { return event }
            let usesShift = modifiers.contains(.shift)
            switch key {
            case "x" where !usesShift:
                editor.cut(nil)
            case "c" where !usesShift:
                editor.copy(nil)
            case "v" where !usesShift:
                editor.paste(nil)
            case "v" where usesShift:
                editor.pasteAsPlainText(nil)
            case "a" where !usesShift:
                editor.selectAll(nil)
            case "z" where usesShift:
                editor.undoManager?.redo()
            case "z" where !usesShift:
                editor.undoManager?.undo()
            default:
                return event
            }
            return nil
        }

        // AppKit 的字段编辑器可能在第一次右键后重建上下文菜单；每次右键按下前
        // 重新绑定完整菜单，避免后续右键退回系统的紧凑“更多”菜单。
        mouseEventMonitor = NSEvent.addLocalMonitorForEvents(matching: .rightMouseDown) { [weak self] event in
            guard let self, event.window === self.window else { return event }
            self.focusedFieldEditor?.menu = self.makeTextEditingMenu()
            return event
        }
    }

    private var focusedFieldEditor: NSTextView? {
        if let editor = window?.firstResponder as? NSTextView {
            editor.menu = makeTextEditingMenu()
            return editor
        }
        guard let field = window?.firstResponder as? NSTextField,
              let editor = window?.fieldEditor(true, for: field) as? NSTextView else { return nil }
        editor.menu = makeTextEditingMenu()
        return editor
    }

    func controlTextDidBeginEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField,
              let editor = window?.fieldEditor(false, for: field) as? NSTextView else { return }
        editor.menu = makeTextEditingMenu()
    }

    private func makeTextEditingMenu() -> NSMenu {
        let menu = NSMenu()
        menu.autoenablesItems = true
        menu.minimumWidth = 220
        for (title, selector, key, modifiers) in [
            (L10n.t("剪切"), #selector(NSText.cut(_:)), "x", NSEvent.ModifierFlags.command),
            (L10n.t("拷贝"), #selector(NSText.copy(_:)), "c", NSEvent.ModifierFlags.command),
            (L10n.t("粘贴"), #selector(NSText.paste(_:)), "v", NSEvent.ModifierFlags.command),
            (L10n.t("粘贴为纯文本"), #selector(NSTextView.pasteAsPlainText(_:)), "V", NSEvent.ModifierFlags([.command, .shift])),
            (L10n.t("全选"), #selector(NSText.selectAll(_:)), "a", NSEvent.ModifierFlags.command),
        ] as [(String, Selector, String, NSEvent.ModifierFlags)] {
            let item = NSMenuItem(title: title, action: selector, keyEquivalent: key)
            item.target = nil
            item.keyEquivalentModifierMask = modifiers
            menu.addItem(item)
        }
        return menu
    }

    func applyLanguage() {
        window?.title = L10n.t("查找与替换")
        searchField.placeholderString = L10n.t("查找")
        replaceField.placeholderString = L10n.t("替换为")
        prevButton.title = L10n.t("上一个")
        nextButton.title = L10n.t("下一个")
        replaceButton.title = L10n.t("替换")
        replaceAllButton.title = L10n.t("全部替换")
        closeButton.title = L10n.t("关闭")
        caseCheck.title = L10n.t("区分大小写")
        wholeCheck.title = L10n.t("全词")
        updateDisclosureAppearance()
    }

    func updateSession(_ session: EditorSession) {
        if self.session !== session {
            self.session?.onFindResult = nil
            self.session = session
            updateResult(current: 0, total: 0)
        }
    }

    func showPanel() {
        guard let window else { return }
        // 定位到主窗口顶部居中，淡入出现
        if let mainWindow = session?.webView?.window {
            let mainFrame = mainWindow.frame
            let x = mainFrame.midX - window.frame.width / 2
            let y = mainFrame.maxY - window.frame.height - 60
            window.setFrameOrigin(NSPoint(x: x, y: y))
        }
        window.alphaValue = 0
        window.makeKeyAndOrderFront(nil)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.18
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            window.animator().alphaValue = 1
        }
        searchField.becomeFirstResponder()
        DispatchQueue.main.async { [weak self] in
            guard let self,
                  let editor = self.window?.fieldEditor(false, for: self.searchField) as? NSTextView else { return }
            editor.menu = self.makeTextEditingMenu()
        }
        if !searchField.stringValue.isEmpty {
            runFind(backwards: false)
        }
    }

    func updateResult(current: Int, total: Int) {
        resultLabel.stringValue = "\(current)/\(total)"
    }

    // MARK: - Actions

    private func runFind(backwards: Bool) {
        // 前端 findNext/findPrev 接收 "query\tcase\twhole"，并在其内部执行查找并回显 findResult
        let text = "\(searchField.stringValue)\t\(caseCheck.state == .on ? 1 : 0)\t\(wholeCheck.state == .on ? 1 : 0)"
        session?.execute(backwards ? "findPrev" : "findNext", text: text)
    }

    @objc private func searchChanged() {
        runFind(backwards: false)
    }

    @objc private func nextClicked() { runFind(backwards: false) }
    @objc private func prevClicked() { runFind(backwards: true) }
    @objc private func optionChanged() { runFind(backwards: false) }

    @objc private func toggleReplaceExpanded() {
        setReplaceExpanded(!isReplaceExpanded, animated: true)
    }

    private func setReplaceExpanded(_ expanded: Bool, animated: Bool) {
        guard expanded != isReplaceExpanded,
              let window,
              let contentView = window.contentView,
              let replaceRowHeightConstraint,
              let replaceTopSpacingConstraint else { return }

        isReplaceExpanded = expanded
        updateDisclosureAppearance()
        if expanded {
            replaceRow.isHidden = false
        }

        let targetContentHeight = FindPanelLayout.contentHeight(isReplaceExpanded: expanded)
        let targetWindowHeight = window.frameRect(forContentRect: NSRect(
            x: 0,
            y: 0,
            width: contentView.bounds.width,
            height: targetContentHeight
        )).height
        let targetFrame = FindPanelLayout.frameKeepingTop(
            currentFrame: window.frame,
            targetHeight: targetWindowHeight
        )
        replaceRowHeightConstraint.constant = FindPanelLayout.replaceRowHeight(isExpanded: expanded)
        replaceTopSpacingConstraint.constant = FindPanelLayout.replaceTopSpacing(isExpanded: expanded)

        let changes = {
            window.animator().setFrame(targetFrame, display: true)
            self.replaceRow.animator().alphaValue = expanded ? 1 : 0
            contentView.animator().layoutSubtreeIfNeeded()
        }
        if animated {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.18
                context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                changes()
            } completionHandler: { [weak self] in
                if !expanded {
                    self?.replaceRow.isHidden = true
                }
            }
        } else {
            window.setFrame(targetFrame, display: true)
            replaceRow.alphaValue = expanded ? 1 : 0
            contentView.layoutSubtreeIfNeeded()
            if !expanded {
                replaceRow.isHidden = true
            }
        }
    }

    private func updateDisclosureAppearance() {
        let title = isReplaceExpanded ? L10n.t("隐藏替换") : L10n.t("显示替换")
        disclosureButton.image = NSImage(
            systemSymbolName: isReplaceExpanded ? "chevron.down" : "chevron.right",
            accessibilityDescription: title
        )
        disclosureButton.toolTip = title
        disclosureButton.setAccessibilityLabel(title)
    }

    @objc private func replaceClicked() {
        guard let session else { return }
        let text = "\(searchField.stringValue)\t\(replaceField.stringValue)\t\(caseCheck.state == .on ? 1 : 0)\t\(wholeCheck.state == .on ? 1 : 0)"
        session.execute("replaceOne", text: text)
    }

    @objc private func replaceAllClicked() {
        guard let session else { return }
        let text = "\(searchField.stringValue)\t\(replaceField.stringValue)\t\(caseCheck.state == .on ? 1 : 0)\t\(wholeCheck.state == .on ? 1 : 0)"
        session.execute("replaceAll", text: text)
    }

    @objc private func closeClicked() {
        session?.execute("findClose")
        window?.orderOut(nil)
    }
}
