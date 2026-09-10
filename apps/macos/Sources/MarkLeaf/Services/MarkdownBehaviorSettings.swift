import Foundation

struct MarkdownBehaviorSettingsModel {
    var escapeLiteralSymbols: Bool
    var escapeMarkdownLiteralSymbols: Bool
    var exitBlockOnEmptyEnter: Bool
    var useShiftEnterHardBreak: Bool
    var markdownCodeFenceIsTilde: Bool
    var markdownEmphasisMarkerIsUnderscore: Bool
    var markdownBulletMarker: String

    init(settings: AppSettings) {
        escapeLiteralSymbols = settings.escapeLiteralSymbols
        escapeMarkdownLiteralSymbols = settings.escapeMarkdownLiteralSymbols
        exitBlockOnEmptyEnter = settings.exitBlockOnEmptyEnter
        useShiftEnterHardBreak = settings.useShiftEnterHardBreak
        markdownCodeFenceIsTilde = settings.markdownCodeFence == "tilde"
        markdownEmphasisMarkerIsUnderscore = settings.markdownEmphasisMarker == "underscore"
        markdownBulletMarker = settings.markdownBulletMarker
    }

    func apply(to settings: inout AppSettings) {
        settings.escapeLiteralSymbols = escapeLiteralSymbols
        settings.escapeMarkdownLiteralSymbols = escapeMarkdownLiteralSymbols
        settings.exitBlockOnEmptyEnter = exitBlockOnEmptyEnter
        settings.useShiftEnterHardBreak = useShiftEnterHardBreak
        settings.markdownCodeFence = markdownCodeFenceIsTilde ? "tilde" : "backtick"
        settings.markdownEmphasisMarker = markdownEmphasisMarkerIsUnderscore ? "underscore" : "asterisk"
        settings.markdownBulletMarker = markdownBulletMarker
    }
}
