import Foundation

enum EditorContextMenuState {
    static func formatPainterEnabled(
        isSourceMode: Bool,
        canStartFormatPainter: Bool,
        isFormatPainterArmed: Bool
    ) -> Bool {
        !isSourceMode && (canStartFormatPainter || isFormatPainterArmed)
    }
}
