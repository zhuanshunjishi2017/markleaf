import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let preferencesField = NSTextField(string: "1.60")

let dialogField = NSTextField(frame: NSRect(x: 0, y: 0, width: 360, height: 30))
DialogTextFieldStyle.apply(to: dialogField)

expect(dialogField.bezelStyle == .squareBezel,
       "dialog fields should use the subtly rounded rectangular bezel requested from Preferences")
expect(dialogField.frame.height == preferencesField.fittingSize.height,
       "dialog fields should use the intrinsic Preferences field height instead of a pill height")
expect(dialogField.contentHuggingPriority(for: .vertical) == .required,
       "grid layouts should not stretch dialog fields into capsules")

print("PASS")
