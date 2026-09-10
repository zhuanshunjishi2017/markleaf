import Foundation

enum ExportThemeSelectionPolicy {
    static func preferredThemeID(
        currentThemeID: String?,
        persistedThemeID: String?,
        availableThemeIDs: [String]
    ) -> String? {
        if let currentThemeID {
            let normalized = ThemeIDNormalizer.normalize(currentThemeID)
            if availableThemeIDs.contains(normalized) {
                return normalized
            }
        }
        if let persistedThemeID {
            let normalized = ThemeIDNormalizer.normalize(persistedThemeID)
            if availableThemeIDs.contains(normalized) {
                return normalized
            }
        }
        return nil
    }
}
