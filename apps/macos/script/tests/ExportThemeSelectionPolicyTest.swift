import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let availableThemes = ["colors-white-only", "colors-dark"]
expect(
    ExportThemeSelectionPolicy.preferredThemeID(
        currentThemeID: "colors-white-only",
        persistedThemeID: "colors-dark",
        availableThemeIDs: availableThemes
    ) == "colors-white-only",
    "the current editor theme should be the default export theme"
)
expect(
    ExportThemeSelectionPolicy.preferredThemeID(
        currentThemeID: "colors-missing",
        persistedThemeID: "colors-dark",
        availableThemeIDs: availableThemes
    ) == "colors-dark",
    "a persisted export theme should be used only when the current theme is unavailable"
)
expect(
    ExportThemeSelectionPolicy.preferredThemeID(
        currentThemeID: "colors-missing",
        persistedThemeID: "colors-also-missing",
        availableThemeIDs: availableThemes
    ) == nil,
    "an unavailable current and persisted theme should leave selection unresolved"
)
print("PASS")
