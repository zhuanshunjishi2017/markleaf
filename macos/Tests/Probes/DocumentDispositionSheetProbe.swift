import AppKit
import Foundation

enum SavedDocumentChoice { case save, discard, cancel }
enum UntitledDocumentChoice { case saveAs, delete, cancel }

enum L10n {
    static func t(_ text: String) -> String { text }
    static func f(_ format: String, _ args: CVarArg...) -> String {
        String(format: format, arguments: args)
    }
}

@main
enum DocumentDispositionSheetProbe {
    static func main() {
        _ = NSApplication.shared

        let saved = DocumentDispositionSheetPresenter.makeSavedAlert(filename: "notes.md")
        precondition(saved.buttons.map(\.title) == ["保存", "取消", "不保存"])
        precondition(saved.buttons.map(\.hasDestructiveAction) == [false, false, true])
        precondition(saved.buttons[1].keyEquivalent == "\u{1b}")

        let untitled = DocumentDispositionSheetPresenter.makeUntitledAlert()
        precondition(untitled.buttons.map(\.title) == ["保存…", "取消", "删除"])
        precondition(untitled.buttons.map(\.hasDestructiveAction) == [false, false, true])
        precondition(untitled.buttons[1].keyEquivalent == "\u{1b}")
    }
}
