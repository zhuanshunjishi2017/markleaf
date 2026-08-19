import Foundation

enum PDFHeaderFooterPolicy {
    static let presets = ["none", "title-left", "page-center", "page-right", "page-total-center", "custom"]

    static func normalizePreset(_ value: String) -> String {
        presets.contains(value) ? value : "none"
    }

    static func normalizeAlignment(_ value: String) -> String {
        ["left", "center", "right"].contains(value) ? value : ""
    }

    static func text(for preset: String, custom: String) -> String {
        switch normalizePreset(preset) {
        case "title-left": return "{title}"
        case "page-center", "page-right": return "{page}"
        case "page-total-center": return "{page}/{pages}"
        case "custom": return custom
        default: return ""
        }
    }

    static func alignment(for preset: String) -> String {
        switch normalizePreset(preset) {
        case "title-left": return "left"
        case "page-center", "page-total-center", "custom": return "center"
        case "page-right": return "right"
        default: return ""
        }
    }

    static func resolve(_ value: String, title: String, page: Int, pages: Int) -> String {
        value
            .replacingOccurrences(of: "{title}", with: title)
            .replacingOccurrences(of: "{document-title}", with: title)
            .replacingOccurrences(of: "{page}", with: String(page))
            .replacingOccurrences(of: "{pages}", with: String(pages))
            .replacingOccurrences(of: "{total}", with: String(pages))
    }
}
