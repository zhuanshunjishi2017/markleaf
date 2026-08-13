import Foundation

@main
enum ContextMenuFormatPainterStateProbe {
    static func main() {
        precondition(EditorContextMenuState.formatPainterEnabled(
            isSourceMode: false,
            canStartFormatPainter: true,
            isFormatPainterArmed: false
        ))
        precondition(EditorContextMenuState.formatPainterEnabled(
            isSourceMode: false,
            canStartFormatPainter: false,
            isFormatPainterArmed: true
        ))
        precondition(!EditorContextMenuState.formatPainterEnabled(
            isSourceMode: true,
            canStartFormatPainter: true,
            isFormatPainterArmed: false
        ))
    }
}
