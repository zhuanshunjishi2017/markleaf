import Foundation

enum L10n {
    static func translate(_ text: String, language: String) -> String { text }
}

enum UnsafeEmphasisAction: String {
    case literal
}

struct PersistedExportSettings: Codable {
    mutating func normalize() {}
}

enum DocumentEncodingPolicy: String {
    case utf8 = "UTF-8"
}

enum AppLog {
    static func warning(_ message: String) {}
    static func info(_ message: String) {}
    static func error(_ message: String) {}
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let decoder = JSONDecoder()
let legacyJSON = Data(#"{"displayLanguage":"en","visualFontSize":19,"showParagraphBlockHandle":false}"#.utf8)
let legacy = try decoder.decode(AppSettings.self, from: legacyJSON)
expect(!legacy.showCodeHighlight, "legacy settings without showCodeHighlight should default to false")
expect(legacy.visualCjkAutoSpacing, "legacy settings should enable CJK auto spacing by default")
expect(legacy.displayLanguage == "en", "adding the optional key must preserve unrelated language settings")
expect(legacy.visualFontSize == 19, "adding the optional key must preserve unrelated numeric settings")
expect(!legacy.showParagraphBlockHandle, "adding the optional key must preserve unrelated Boolean settings")

var enabled = legacy
enabled.showCodeHighlight = true
enabled.visualCjkAutoSpacing = false
let encoded = try JSONEncoder().encode(enabled)
let roundTrip = try decoder.decode(AppSettings.self, from: encoded)
expect(roundTrip.showCodeHighlight, "an explicit true value should survive an encode/decode round trip")
expect(!roundTrip.visualCjkAutoSpacing, "an explicit CJK auto spacing value should survive round trip")
expect(roundTrip.visualFontSize == 19, "round-tripping code highlighting must not alter unrelated settings")

var editing = legacy
editing.exitBlockOnEmptyEnter = true
editing.useShiftEnterHardBreak = false
editing.escapeLiteralSymbols = true
editing.escapeMarkdownLiteralSymbols = false
editing.markdownCodeFence = "tilde"
editing.markdownEmphasisMarker = "underscore"
editing.markdownBulletMarker = "plus"
let editingEncoded = try JSONEncoder().encode(editing)
let editingRoundTrip = try decoder.decode(AppSettings.self, from: editingEncoded)
expect(editingRoundTrip.exitBlockOnEmptyEnter, "exit-block setting should survive persistence")
expect(!editingRoundTrip.useShiftEnterHardBreak, "Shift+Enter behavior should survive persistence")
expect(editingRoundTrip.escapeLiteralSymbols, "literal symbol escaping should survive persistence")
expect(!editingRoundTrip.escapeMarkdownLiteralSymbols, "Markdown literal escaping should survive persistence")
expect(editingRoundTrip.markdownCodeFence == "tilde", "code fence preference should survive persistence")
expect(editingRoundTrip.markdownEmphasisMarker == "underscore", "emphasis preference should survive persistence")
expect(editingRoundTrip.markdownBulletMarker == "plus", "bullet preference should survive persistence")

let explicitFalse = try decoder.decode(AppSettings.self, from: Data(#"{"showCodeHighlight":false}"#.utf8))
expect(!explicitFalse.showCodeHighlight, "an explicit false value should remain false")

print("PASS")
