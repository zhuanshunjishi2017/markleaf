import Foundation

enum ThemeIDNormalizer {
    static func normalize(_ id: String) -> String {
        switch id {
        case "colors-white", "colors-white-only":
            return "colors-default-light"
        case "colors-apple-note":
            return "colors-memo"
        default:
            return id
        }
    }
}
