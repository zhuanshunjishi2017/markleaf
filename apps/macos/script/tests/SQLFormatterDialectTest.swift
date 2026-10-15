import Foundation

// Minimal logging stubs required by AppSettings in this isolated contract build.
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
    static func info(_ message: String) {}
    static func warning(_ message: String) {}
    static func error(_ message: String) {}
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(SQLFormatterDialect.normalized("PostgreSQL") == .postgres, "SQL dialect aliases should normalize")
expect(SQLFormatterDialect.normalized("TS-SQL") == .tsql, "SQL dialect aliases should normalize T-SQL")
expect(SQLFormatterDialect.normalized("unknown") == .ansi, "unknown SQL dialects should fall back to ANSI")
expect(SQLFormatterDialect.allCases.map(\.rawValue) == [
    "ansi", "postgres", "mysql", "sqlite", "bigquery", "tsql",
], "SQL dialects should have a stable settings order")

let decoder = JSONDecoder()
let defaultSettings = try decoder.decode(AppSettings.self, from: Data("{}".utf8))
expect(defaultSettings.sqlFormatterDialect == .ansi, "SQL should default to ANSI")
let defaultObject = try JSONSerialization.jsonObject(with: JSONEncoder().encode(defaultSettings)) as! [String: Any]
expect(defaultObject["inlineCommonActions"] as? Bool == true, "older settings should default to inline common actions")
let textMenuSettings = try decoder.decode(AppSettings.self, from: Data(#"{"inlineCommonActions":false,"sqlFormatterDialect":"postgres"}"#.utf8))
let textMenuRoundTrip = try JSONSerialization.jsonObject(with: JSONEncoder().encode(textMenuSettings)) as! [String: Any]
expect(textMenuRoundTrip["inlineCommonActions"] as? Bool == false, "text menu preference should survive a round trip")
expect(textMenuSettings.sqlFormatterDialect == .postgres, "menu preference must preserve the SQL dialect")

let explicitSettings = try decoder.decode(AppSettings.self, from: Data(#"{"sqlFormatterDialect":"postgres"}"#.utf8))
expect(explicitSettings.sqlFormatterDialect == .postgres, "an explicit SQL dialect should survive decoding")

let tool = ExternalCodeFormatterCatalog.tool(language: "sql")
expect(tool != nil, "SQL should have an external formatter")
let arguments = ExternalCodeFormatterCatalog.processArguments(for: tool!, selectionLineRange: nil)
expect(
    ExternalCodeFormatterCatalog.processArguments(
        for: tool!,
        selectionLineRange: nil,
        sqlDialect: .postgres
    ).contains("postgres"),
    "SQL arguments should contain the configured dialect"
)

var persisted = explicitSettings
persisted.sqlFormatterDialect = .mysql
let encoded = try JSONEncoder().encode(persisted)
let encodedObject = try JSONSerialization.jsonObject(with: encoded) as! [String: Any]
expect(encodedObject["sqlFormatterDialect"] as? String == "mysql", "SQL dialect should persist by raw value")

print("SQL formatter dialect tests passed")
