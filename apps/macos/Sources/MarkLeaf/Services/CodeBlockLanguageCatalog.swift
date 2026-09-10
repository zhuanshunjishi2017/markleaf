import Foundation

/// Common fenced-code language identifiers offered by the native picker.
/// The picker remains editable, so this list is a convenience rather than a
/// validation allowlist.
enum CodeBlockLanguageCatalog {
    static let commonLanguages = [
        "swift",
        "c",
        "cpp",
        "objective-c",
        "java",
        "kotlin",
        "python",
        "javascript",
        "typescript",
        "jsx",
        "tsx",
        "html",
        "css",
        "json",
        "yaml",
        "xml",
        "shell",
        "powershell",
        "sql",
        "markdown",
        "latex",
        "go",
        "rust",
        "php",
        "ruby",
        "r",
        "toml",
        "ini",
        "diff",
        "mermaid",
    ]

    static func normalized(_ value: String) -> String {
        value.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
