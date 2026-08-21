import Foundation

enum ExportThemeSelectionPolicy {
    static func preferredThemeID(
        currentThemeID: String?,
        persistedThemeID: String?,
        availableThemeIDs: [String]
    ) -> String? {
        if let currentThemeID, availableThemeIDs.contains(currentThemeID) {
            return currentThemeID
        }
        if let persistedThemeID, availableThemeIDs.contains(persistedThemeID) {
            return persistedThemeID
        }
        return nil
    }
}
