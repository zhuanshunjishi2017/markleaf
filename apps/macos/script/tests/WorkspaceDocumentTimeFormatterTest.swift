import AppKit

struct WorkspaceEntry {
    let name: String
    let path: String
    let isDirectory: Bool
}

enum WorkspaceSearchPolicy {
    static func snippet(content: String, query: String, nameMatches: Bool) -> String { content }
}

struct TestSettings {
    var displayLanguage = "zh-Hans"
}

final class SettingsService {
    static let shared = SettingsService()
    var settings = TestSettings()
}

enum L10n {
    static func t(_ text: String) -> String { text }

    static func f(_ format: String, _ args: CVarArg...) -> String {
        String(format: format, arguments: args)
    }
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let calendar = Calendar(identifier: .gregorian)
let now = calendar.date(from: DateComponents(year: 2026, month: 8, day: 1, hour: 16, minute: 30))!
let today = calendar.date(from: DateComponents(year: 2026, month: 8, day: 1, hour: 8, minute: 15))!
let yesterday = calendar.date(from: DateComponents(year: 2026, month: 7, day: 31, hour: 12, minute: 29))!
let twoDaysAgo = calendar.date(from: DateComponents(year: 2026, month: 7, day: 30, hour: 9, minute: 8))!

let formattedToday = WorkspaceDocumentTimeFormatter.format(today, now: now)
expect(
    formattedToday == "今天 08:15",
    "today should include the localized Today prefix; got \(formattedToday)"
)
expect(
    WorkspaceDocumentTimeFormatter.format(yesterday, now: now) == "昨天 12:29",
    "yesterday should keep its localized prefix and a separating space"
)
expect(
    WorkspaceDocumentTimeFormatter.format(twoDaysAgo, now: now) == "7月30日",
    "dates older than yesterday should use a calendar date"
)

print("PASS")
