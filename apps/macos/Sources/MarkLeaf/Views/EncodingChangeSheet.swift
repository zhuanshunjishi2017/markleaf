import AppKit

struct EncodingChangeSheetStrings {
    let title: String
    let message: String
    let directReadButton: String
    let convertEncodingButton: String
    let cancelButton: String
}

enum EncodingChangeSheetPresenter {
    static func choice(for response: NSApplication.ModalResponse) -> DocumentEncodingChangeChoice {
        switch response {
        case .alertFirstButtonReturn:
            return .directRead
        case .alertSecondButtonReturn:
            return .convertEncoding
        default:
            return .cancel
        }
    }

    static func present(
        for window: NSWindow,
        strings: EncodingChangeSheetStrings,
        completion: @escaping (DocumentEncodingChangeChoice) -> Void
    ) {
        let alert = NSAlert()
        alert.messageText = strings.title
        alert.informativeText = strings.message
        alert.alertStyle = .warning
        alert.addButton(withTitle: strings.directReadButton)
        alert.addButton(withTitle: strings.convertEncodingButton)
        let cancelButton = alert.addButton(withTitle: strings.cancelButton)
        cancelButton.keyEquivalent = "\u{1b}"
        alert.beginSheetModal(for: window) { response in
            completion(choice(for: response))
        }
    }
}
