import Foundation

enum AppLog {
    static func warning(_ message: String) {}
    static func info(_ message: String) {}
    static func error(_ message: String) {}
}

struct TestSettings {
    var displayLanguage = "zh-Hans"
}

final class SettingsService {
    static let shared = SettingsService()
    var settings = TestSettings()
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let repositoryRoot = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let styles = repositoryRoot.appendingPathComponent("packages/styles", isDirectory: true)
let names = try FileManager.default.contentsOfDirectory(atPath: styles.path)
expect(names.filter { $0 == "colors-default-light.css" }.count == 1, "canonical default-light resource should exist exactly once")
expect(names.filter { $0 == "colors-memo.css" }.count == 1, "canonical memo resource should exist exactly once")
expect(!names.contains("colors-white-only.css"), "legacy white-only resource should not be packaged")
expect(!names.contains("colors-apple-note.css"), "legacy apple-note resource should not be packaged")

let temp = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
let custom = temp.appendingPathComponent("custom", isDirectory: true)
try FileManager.default.createDirectory(at: custom, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: temp) }
let customCSS = "/* @type: color-theme\n   @name: Custom Exact\n   @mode: light */\n:root { --bg-primary: #123456; }\n"
try customCSS.write(to: custom.appendingPathComponent("colors-Custom_ID.css"), atomically: true, encoding: .utf8)

guard let manager = StyleManager(directories: [styles, custom]) else {
    fputs("FAIL: style manager should discover built-in and custom resources\n", stderr)
    exit(1)
}
let ids = manager.colorThemes.map(\.id)
expect(ids.filter { $0 == "colors-default-light" }.count == 1, "discovery should expose canonical default light exactly once")
expect(ids.filter { $0 == "colors-memo" }.count == 1, "discovery should expose canonical memo exactly once")
expect(ids.contains("colors-Custom_ID"), "custom theme ID should remain byte-for-byte unchanged and discoverable")
expect(manager.defaultThemeID(forDark: false) == "colors-default-light", "system light fallback should resolve the canonical default-light ID")
expect(manager.defaultThemeID(forDark: false, preferredLight: "colors-white-only") == "colors-default-light", "legacy preferred light ID should resolve to its canonical resource")

let lightCSS = try String(contentsOf: styles.appendingPathComponent("colors-default-light.css"), encoding: .utf8)
expect(lightCSS.contains("@name: 旧版浅色"), "renamed default-light CSS should use the Windows 1.7.4 display name")
expect(lightCSS.contains("--bg-primary:          #FFFFFF;"), "renamed default-light CSS should preserve local colors")
expect(lightCSS.contains("--theme-light:         #0078D4;"), "renamed default-light CSS should preserve local accent declarations")
let memoCSS = try String(contentsOf: styles.appendingPathComponent("colors-memo.css"), encoding: .utf8)
expect(memoCSS.contains("@name: 备忘录"), "renamed memo CSS should retain its display name")
expect(memoCSS.contains("--bg-primary:          #F8F1E2;"), "renamed memo CSS should preserve local colors")

let expectedNames = [
    "colors-apple-blue": "浅色",
    "colors-apple-dark": "深色",
    "colors-dark": "旧版深色",
    "colors-default-light": "旧版浅色",
    "colors-high-contrast-dark": "深色高对比",
    "colors-high-contrast-light": "浅色高对比",
    "colors-memo": "备忘录",
    "colors-morandi-cyan": "莫兰迪青",
    "colors-morandi-dark": "莫兰迪棕",
    "colors-morandi": "莫兰迪褐",
]
for (id, expectedName) in expectedNames {
    expect(manager.colorThemes.first(where: { $0.id == id })?.displayName == expectedName, "\(id) should expose the Windows 1.7.4 display name")
}
expect(L10n.translate("旧版浅色", language: "en") == "Legacy Light", "renamed light theme should remain localized in English")
expect(L10n.translate("备忘录", language: "ja") == "メモ", "memo theme should remain localized in Japanese")
expect(L10n.translate("深色高对比", language: "zh-Hant") == "深色高對比", "high-contrast theme should remain localized in Traditional Chinese")
expect(L10n.translate("莫兰迪棕", language: "en") == "Morandi Brown", "renamed Morandi theme should remain localized in English")

print("PASS")
