import Foundation

enum L10n {
    static func translate(_ text: String, language: String) -> String { text }
}

enum UnsafeEmphasisAction: String {
    case literal
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
expect(legacy.visualLineHeight == 1.75, "new installs should use the Windows 1.7.1 line height")
expect(legacy.autoHideScrollbars, "new installs should auto-hide scrollbars")
expect(legacy.showCodeHighlight, "new installs should show code highlighting")
expect(legacy.colorTheme == "apple-blue", "new installs should use Apple Blue")
expect(legacy.defaultLightThemeID == "apple-blue", "new installs should use Apple Blue for light mode")
expect(legacy.defaultDarkThemeID == "apple-dark", "new installs should use Apple Dark for dark mode")
expect(!legacy.escapeLiteralSymbols, "literal symbol escaping should stay off by default")
expect(!legacy.escapeMarkdownLiteralSymbols, "Markdown literal escaping should stay off by default")
expect(legacy.clipboardImageHandling == "copyToAssets", "clipboard images should copy into assets by default")
expect(legacy.fileImageHandling == "copyToAssets", "file images should copy into assets by default")
expect(legacy.showCodeHighlight, "legacy settings without showCodeHighlight should use the 1.7.1 default")
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

let themeMigrationCases = [
    ("colors-white", "colors-default-light"),
    ("colors-white-only", "colors-default-light"),
    ("colors-apple-note", "colors-memo"),
    ("colors-default-light", "colors-default-light"),
    ("colors-memo", "colors-memo"),
    ("colors-dark", "colors-dark"),
    ("colors-custom-MiXeD", "colors-custom-MiXeD"),
    (" custom-theme ", " custom-theme "),
]
for (input, expected) in themeMigrationCases {
    expect(ThemeIDNormalizer.normalize(input) == expected, "theme ID \(input) should normalize to \(expected)")
    expect(ThemeIDNormalizer.normalize(ThemeIDNormalizer.normalize(input)) == expected, "theme ID normalization should be idempotent for \(input)")
}

let legacyThemes = try decoder.decode(
    AppSettings.self,
    from: Data(#"{"colorTheme":"colors-white-only","defaultLightThemeID":"colors-white","defaultDarkThemeID":"colors-apple-note","exportSettings":{"colorTheme":"colors-apple-note"}}"#.utf8)
)
expect(legacyThemes.colorTheme == "colors-default-light", "current legacy theme should migrate")
expect(legacyThemes.defaultLightThemeID == "colors-default-light", "legacy light default should migrate")
expect(legacyThemes.defaultDarkThemeID == "colors-memo", "legacy dark-default field should migrate byte-for-byte by ID")
expect(legacyThemes.exportSettings.colorTheme == "colors-memo", "persisted export theme should migrate")

let migratedJSON = try JSONSerialization.jsonObject(with: JSONEncoder().encode(legacyThemes)) as! [String: Any]
expect(migratedJSON["colorTheme"] as? String == "colors-default-light", "encoding migrated settings should persist the canonical current theme")
expect(migratedJSON["defaultLightThemeID"] as? String == "colors-default-light", "encoding migrated settings should persist the canonical light default")
expect(migratedJSON["defaultDarkThemeID"] as? String == "colors-memo", "encoding migrated settings should persist the canonical dark-default field")
let migratedExport = migratedJSON["exportSettings"] as? [String: Any]
expect(migratedExport?["colorTheme"] as? String == "colors-memo", "encoding migrated settings should persist the canonical export theme")

let saveRoot = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
defer { try? FileManager.default.removeItem(at: saveRoot) }
let service = SettingsService(environment: ["MARKLEAF_APP_SUPPORT_DIR": saveRoot.path])
service.update {
    $0.colorTheme = "colors-white"
    $0.defaultLightThemeID = "colors-white-only"
    $0.defaultDarkThemeID = "colors-apple-note"
    $0.exportSettings.colorTheme = "colors-apple-note"
}
let savedData = try Data(contentsOf: saveRoot.appendingPathComponent("MarkLeaf/settings.json"))
let savedJSON = try JSONSerialization.jsonObject(with: savedData) as! [String: Any]
expect(savedJSON["colorTheme"] as? String == "colors-default-light", "settings save should canonicalize a legacy current theme")
expect(savedJSON["defaultLightThemeID"] as? String == "colors-default-light", "settings save should canonicalize a legacy light default")
expect(savedJSON["defaultDarkThemeID"] as? String == "colors-memo", "settings save should canonicalize a legacy dark-default field")
let savedExport = savedJSON["exportSettings"] as? [String: Any]
expect(savedExport?["colorTheme"] as? String == "colors-memo", "settings save should canonicalize a legacy export theme")

print("PASS")
