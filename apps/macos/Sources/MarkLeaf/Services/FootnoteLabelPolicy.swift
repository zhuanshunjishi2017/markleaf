import Foundation

enum FootnoteLabelPolicy {
    static func normalized(_ value: String) -> String? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty,
              !trimmed.contains("]"),
              !trimmed.contains("\n"),
              !trimmed.contains("\r") else { return nil }
        return trimmed
    }
}
