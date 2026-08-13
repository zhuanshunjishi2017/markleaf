import AppKit

enum DocumentDispositionSheetActionRole: Equatable {
    case `default`
    case destructive
    case cancel
}

struct DocumentDispositionSheetAction: Equatable {
    let title: String
    let role: DocumentDispositionSheetActionRole
}

struct DocumentDispositionSheetSpec {
    let title: String
    let informativeText: String
    let actions: [DocumentDispositionSheetAction]
    let defaultActionIndex: Int
    let cancelActionIndex: Int

    static func saved(filename: String) -> DocumentDispositionSheetSpec {
        DocumentDispositionSheetSpec(
            title: L10n.f("是否保存对“%@”的修改？", filename),
            informativeText: L10n.t("如果不保存，您的更改将会丢失。"),
            actions: [
                .init(title: L10n.t("保存"), role: .default),
                .init(title: L10n.t("不保存"), role: .destructive),
                .init(title: L10n.t("取消"), role: .cancel)
            ],
            defaultActionIndex: 0,
            cancelActionIndex: 2
        )
    }

    static func untitled() -> DocumentDispositionSheetSpec {
        DocumentDispositionSheetSpec(
            title: L10n.t("是否保存此文档？"),
            informativeText: L10n.t("如果不保存，这个文档将被删除。"),
            actions: [
                .init(title: L10n.t("保存…"), role: .default),
                .init(title: L10n.t("删除"), role: .destructive),
                .init(title: L10n.t("取消"), role: .cancel)
            ],
            defaultActionIndex: 0,
            cancelActionIndex: 2
        )
    }

    func savedChoice(forActionIndex index: Int) -> SavedDocumentChoice {
        switch index {
        case 0: return .save
        case 1: return .discard
        default: return .cancel
        }
    }

    func untitledChoice(forActionIndex index: Int) -> UntitledDocumentChoice {
        switch index {
        case 0: return .saveAs
        case 1: return .delete
        default: return .cancel
        }
    }
}

final class DocumentDispositionSheetPresenter {
    static func presentSaved(
        for parentWindow: NSWindow,
        filename: String,
        deferCompletion: Bool = true,
        completion: @escaping (SavedDocumentChoice) -> Void
    ) {
        let spec = DocumentDispositionSheetSpec.saved(filename: filename)
        present(spec: spec, for: parentWindow, deferCompletion: deferCompletion) { index in
            completion(spec.savedChoice(forActionIndex: index))
        }
    }

    static func presentUntitled(
        for parentWindow: NSWindow,
        deferCompletion: Bool = true,
        completion: @escaping (UntitledDocumentChoice) -> Void
    ) {
        let spec = DocumentDispositionSheetSpec.untitled()
        present(spec: spec, for: parentWindow, deferCompletion: deferCompletion) { index in
            completion(spec.untitledChoice(forActionIndex: index))
        }
    }

    private static func present(
        spec: DocumentDispositionSheetSpec,
        for parentWindow: NSWindow,
        deferCompletion: Bool,
        completion: @escaping (Int) -> Void
    ) {
        let controller = DocumentDispositionSheetController(spec: spec, deferCompletion: deferCompletion, completion: completion)
        // contentViewController 会把窗口缩放到 preferredContentSize；不设置会塌陷成 0 高度，
        // 导致保存提示看不见（退出/关闭时看起来“卡死”）。
        controller.preferredContentSize = NSSize(width: 460, height: 286)
        let sheet = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 460, height: 286),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        sheet.titleVisibility = .hidden
        sheet.titlebarAppearsTransparent = true
        sheet.isMovable = false
        sheet.isReleasedWhenClosed = false
        sheet.contentViewController = controller
        parentWindow.beginSheet(sheet)
    }
}

private final class DocumentDispositionSheetController: NSViewController {
    private let spec: DocumentDispositionSheetSpec
    private let deferCompletion: Bool
    private var completion: ((Int) -> Void)?

    init(spec: DocumentDispositionSheetSpec, deferCompletion: Bool, completion: @escaping (Int) -> Void) {
        self.spec = spec
        self.deferCompletion = deferCompletion
        self.completion = completion
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func loadView() {
        let root = NSView()
        root.translatesAutoresizingMaskIntoConstraints = false

        let icon = NSImageView(image: NSApp.applicationIconImage ?? NSImage())
        icon.imageScaling = .scaleProportionallyDown
        icon.translatesAutoresizingMaskIntoConstraints = false

        let title = NSTextField(wrappingLabelWithString: spec.title)
        title.font = .systemFont(ofSize: 14, weight: .semibold)
        title.maximumNumberOfLines = 2

        let detail = NSTextField(wrappingLabelWithString: spec.informativeText)
        detail.textColor = .secondaryLabelColor
        detail.maximumNumberOfLines = 2

        let copy = NSStackView(views: [title, detail])
        copy.orientation = .vertical
        copy.alignment = .leading
        copy.spacing = 4
        copy.translatesAutoresizingMaskIntoConstraints = false

        let header = NSStackView(views: [icon, copy])
        header.orientation = .horizontal
        header.alignment = .top
        header.spacing = 14
        header.translatesAutoresizingMaskIntoConstraints = false

        let actions = NSStackView()
        actions.orientation = .vertical
        actions.alignment = .width
        actions.spacing = 10
        actions.translatesAutoresizingMaskIntoConstraints = false
        for (index, action) in spec.actions.enumerated() {
            let button = NSButton(title: action.title, target: self, action: #selector(selectAction(_:)))
            button.tag = index
            button.bezelStyle = .rounded
            button.controlSize = .large
            button.heightAnchor.constraint(equalToConstant: 38).isActive = true
            if index == spec.defaultActionIndex {
                button.keyEquivalent = "\r"
                button.bezelColor = .controlAccentColor
            }
            if index == spec.cancelActionIndex {
                button.keyEquivalent = "\u{1b}"
            }
            if action.role == .destructive {
                button.bezelColor = .systemRed
                button.contentTintColor = .white
            }
            actions.addArrangedSubview(button)
        }

        root.addSubview(header)
        root.addSubview(actions)
        NSLayoutConstraint.activate([
            icon.widthAnchor.constraint(equalToConstant: 42),
            icon.heightAnchor.constraint(equalToConstant: 42),
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            header.topAnchor.constraint(equalTo: root.topAnchor, constant: 26),
            actions.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 28),
            actions.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -28),
            actions.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -22)
        ])

        view = root
    }

    override var acceptsFirstResponder: Bool { true }

    override func viewDidAppear() {
        super.viewDidAppear()
        view.window?.makeFirstResponder(self)
    }

    override func cancelOperation(_ sender: Any?) {
        finish(with: spec.cancelActionIndex)
    }

    @objc private func selectAction(_ sender: NSButton) {
        finish(with: sender.tag)
    }

    private func finish(with actionIndex: Int) {
        guard let completion else { return }
        self.completion = nil
        guard let sheet = view.window, let parent = sheet.sheetParent else {
            completion(spec.cancelActionIndex)
            return
        }
        parent.endSheet(sheet)
        if deferCompletion {
            // 关闭窗口/替换文档时，等 sheet 收起后再回调，避免下滑动画期间重入。
            DispatchQueue.main.async {
                completion(actionIndex)
            }
        } else {
            // 退出应用时，主队列不会被终止循环排空，必须同步回调以正确 reply。
            completion(actionIndex)
        }
    }
}
