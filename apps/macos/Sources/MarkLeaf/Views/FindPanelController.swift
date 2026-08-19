import AppKit

/// 查找与替换面板：原生弹出式窗口（NSPanel），控件美观、出现带淡入动画，文案随语言切换。
/// 通过命令驱动前端查找逻辑：findText / replaceOne / replaceAll / findClose，结果经 onFindResult 回显。
final class FindPanelController: NSWindowController {
    private weak var session: EditorSession?
    private var replaceMode: Bool

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
    private var closeObserver: NSObjectProtocol?

    init(session: EditorSession?, replaceMode: Bool) {
        self.session = session
        self.replaceMode = replaceMode
        let window = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 96),
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
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func buildContent() {
        guard let window else { return }

        searchField.placeholderString = L10n.t("查找")
        searchField.controlSize = .regular
        searchField.target = self
        searchField.action = #selector(searchChanged)
        searchField.sendsSearchStringImmediately = true

        replaceField.placeholderString = L10n.t("替换为")
        replaceField.controlSize = .regular
        replaceField.bezelStyle = .roundedBezel
        replaceField.target = self
        replaceField.action = #selector(replaceClicked)

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

        // 布局：查找行（查找框 + 上一个/下一个 + 关闭），替换行（替换框 + 替换/全部替换），选项行
        let findRow = NSStackView(views: [searchField, prevButton, nextButton, closeButton])
        findRow.orientation = .horizontal
        findRow.spacing = 6
        findRow.translatesAutoresizingMaskIntoConstraints = false

        let replaceRow = NSStackView(views: [replaceField, replaceButton, replaceAllButton])
        replaceRow.orientation = .horizontal
        replaceRow.spacing = 6
        replaceRow.translatesAutoresizingMaskIntoConstraints = false

        let optionsRow = NSStackView(views: [caseCheck, wholeCheck, NSView(), resultLabel])
        optionsRow.orientation = .horizontal
        optionsRow.spacing = 12
        optionsRow.translatesAutoresizingMaskIntoConstraints = false

        let root = NSStackView(views: [findRow, replaceRow, optionsRow])
        root.orientation = .vertical
        root.alignment = .leading
        root.spacing = 8
        root.edgeInsets = NSEdgeInsets(top: 12, left: 14, bottom: 12, right: 14)
        root.translatesAutoresizingMaskIntoConstraints = false

        window.contentView = root
        NSLayoutConstraint.activate([
            searchField.widthAnchor.constraint(equalToConstant: 200),
            replaceField.widthAnchor.constraint(equalToConstant: 200),
            root.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor),
            root.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor),
            root.topAnchor.constraint(equalTo: window.contentView!.topAnchor),
            root.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor),
        ])

        if let contentView = window.contentView {
            contentView.widthAnchor.constraint(equalToConstant: 420).isActive = true
            contentView.heightAnchor.constraint(equalToConstant: replaceMode ? 104 : 74).isActive = true
        }
        replaceRow.isHidden = !replaceMode
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
