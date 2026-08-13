import AppKit

/// 原生 macOS/Office 风格“是否保存”提示。
/// 使用 NSAlert sheet：按钮从右往左排列，index 0 为默认按钮（回车），
/// index 1 为取消（Esc），index 2 为最左侧的破坏性按钮。
enum DocumentDispositionSheetPresenter {
    static func presentSaved(
        for parentWindow: NSWindow,
        filename: String,
        completion: @escaping (SavedDocumentChoice) -> Void
    ) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.icon = NSApp.applicationIconImage
        alert.messageText = L10n.f("是否保存对“%@”的修改？", filename)
        alert.informativeText = L10n.t("如果不保存，您的更改将会丢失。")
        alert.addButton(withTitle: L10n.t("保存"))
        alert.addButton(withTitle: L10n.t("取消"))
        alert.addButton(withTitle: L10n.t("不保存"))
        // 第一个按钮自动获得回车；Esc 需要手动指定到“取消”。
        alert.buttons[1].keyEquivalent = "\u{1b}"
        present(alert, for: parentWindow) { response in
            switch response {
            case .alertFirstButtonReturn:
                completion(.save)
            case .alertSecondButtonReturn:
                completion(.cancel)
            default:
                completion(.discard)
            }
        }
    }

    static func presentUntitled(
        for parentWindow: NSWindow,
        completion: @escaping (UntitledDocumentChoice) -> Void
    ) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.icon = NSApp.applicationIconImage
        alert.messageText = L10n.t("是否保存此文档？")
        alert.informativeText = L10n.t("如果不保存，这个文档将被删除。")
        alert.addButton(withTitle: L10n.t("保存…"))
        alert.addButton(withTitle: L10n.t("取消"))
        alert.addButton(withTitle: L10n.t("删除"))
        alert.buttons[1].keyEquivalent = "\u{1b}"
        present(alert, for: parentWindow) { response in
            switch response {
            case .alertFirstButtonReturn:
                completion(.saveAs)
            case .alertSecondButtonReturn:
                completion(.cancel)
            default:
                completion(.delete)
            }
        }
    }

    private static func present(
        _ alert: NSAlert,
        for parentWindow: NSWindow,
        completion: @escaping (NSApplication.ModalResponse) -> Void
    ) {
        var responded = false
        let finish: (NSApplication.ModalResponse) -> Void = { response in
            guard !responded else { return }
            responded = true
            completion(response)
        }
        alert.beginSheetModal(for: parentWindow) { response in
            finish(response)
        }
    }
}
