import Foundation

enum L10n { static func translate(_ text: String, language: String) -> String { text } }
enum UnsafeEmphasisAction: String { case literal }
struct PersistedExportSettings: Codable { mutating func normalize() {} }
enum DocumentEncodingPolicy: String { case utf8 = "UTF-8" }
enum AppLog {
    static func warning(_ message: String) {}
    static func info(_ message: String) {}
    static func error(_ message: String) {}
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

expect(StartupActionMigration.migrate(rawValue: "openLastWorkspaceAndFiles") == .restoreSession, "legacy full restore migrates")
expect(StartupActionMigration.migrate(rawValue: "openLastWorkspace") == .openLastWorkspace, "workspace setting survives")
expect(StartupActionMigration.migrate(rawValue: "newDocument") == .newDocument, "blank document setting survives")
expect(StartupActionMigration.migrate(rawValue: "restoreSession") == .restoreSession, "new value decodes")
expect(StartupActionMigration.migrate(rawValue: nil) == .restoreSession, "missing value uses default")
expect(StartupActionMigration.migrate(rawValue: "nonsense") == .restoreSession, "unknown value uses default")

let decoded = try JSONDecoder().decode(AppSettings.self, from: Data(#"{"startupAction":"openLastWorkspaceAndFiles"}"#.utf8))
expect(decoded.startupAction == .restoreSession, "AppSettings decoding applies migration")
expect(AppSettings().startupAction == .restoreSession, "new installations default to restore session")
print("PASS")
