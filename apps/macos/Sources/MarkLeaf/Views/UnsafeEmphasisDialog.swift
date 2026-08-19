import AppKit

final class UnsafeEmphasisDialog: NSObject {
    private static let markdownHelpURL = URL(string: "https://spec.commonmark.org/current/#emphasis-and-strong-emphasis")!

    static func resolve(prompt: UnsafeEmphasisPrompt) -> (action: UnsafeEmphasisAction, suppressPrompt: Bool) {
        let dialog = UnsafeEmphasisDialog()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = L10n.t(prompt.titleKey)
        alert.informativeText = L10n.t(prompt.messageKey)
        alert.addButton(withTitle: L10n.t("保留 Markdown 字面量"))
        alert.addButton(withTitle: L10n.t("转换为 HTML"))
        alert.showsSuppressionButton = true
        alert.suppressionButton?.title = L10n.t("不再提示并记住此选择")

        let helpButton = NSButton(title: L10n.t("了解 Markdown 强调规则…"), target: dialog, action: #selector(openHelp(_:)))
        helpButton.bezelStyle = .inline
        helpButton.controlSize = .small
        alert.accessoryView = helpButton

        let response = alert.runModal()
        let action: UnsafeEmphasisAction = response == .alertSecondButtonReturn ? .html : .literal
        return (action, alert.suppressionButton?.state == .on)
    }

    @objc private func openHelp(_ sender: Any?) {
        NSWorkspace.shared.open(Self.markdownHelpURL)
    }
}
