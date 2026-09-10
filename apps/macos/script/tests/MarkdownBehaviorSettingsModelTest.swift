import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

var settings = AppSettings()
settings.escapeLiteralSymbols = false
settings.escapeMarkdownLiteralSymbols = true
settings.exitBlockOnEmptyEnter = false
settings.useShiftEnterHardBreak = true
settings.markdownCodeFence = "backtick"
settings.markdownEmphasisMarker = "asterisk"
settings.markdownBulletMarker = "dash"

var model = MarkdownBehaviorSettingsModel(settings: settings)
model.escapeLiteralSymbols = true
model.escapeMarkdownLiteralSymbols = false
model.exitBlockOnEmptyEnter = true
model.useShiftEnterHardBreak = false
model.markdownCodeFenceIsTilde = true
model.markdownEmphasisMarkerIsUnderscore = true
model.markdownBulletMarker = "plus"

model.apply(to: &settings)
expect(settings.escapeLiteralSymbols, "literal-symbol escaping should persist")
expect(!settings.escapeMarkdownLiteralSymbols, "markdown-literal escaping should persist")
expect(settings.exitBlockOnEmptyEnter, "exit block setting should persist")
expect(!settings.useShiftEnterHardBreak, "hard-break setting should persist")
expect(settings.markdownCodeFence == "tilde", "code fence should persist")
expect(settings.markdownEmphasisMarker == "underscore", "emphasis marker should persist")
expect(settings.markdownBulletMarker == "plus", "bullet marker should persist")

print("MarkdownBehaviorSettingsModel tests passed")
