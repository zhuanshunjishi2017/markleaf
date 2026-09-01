import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let legacyJSON = #"{"format":"pdf","paperSize":"B4"}"#
let legacy = try! JSONDecoder().decode(PersistedExportSettings.self, from: Data(legacyJSON.utf8))
expect(legacy.keepTablesTogether, "missing table behavior migrates to enabled")
expect(legacy.keepHeadingsWithNextBlock, "missing heading behavior migrates to enabled")
expect(legacy.paperSize == "B4", "B4 must be accepted")

let disabledJSON = #"{"keepTablesTogether":false,"keepHeadingsWithNextBlock":false,"paperSize":"B5"}"#
let disabled = try! JSONDecoder().decode(PersistedExportSettings.self, from: Data(disabledJSON.utf8))
expect(!disabled.keepTablesTogether, "explicit table behavior must persist")
expect(!disabled.keepHeadingsWithNextBlock, "explicit heading behavior must persist")
expect(disabled.paperSize == "B5", "B5 must be accepted")

print("PASS")
