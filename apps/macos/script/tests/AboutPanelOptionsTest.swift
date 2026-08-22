import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let options = AboutPanel.standardOptions(
    infoDictionary: [
        "CFBundleShortVersionString": "1.3.1",
        "CFBundleVersion": "310",
    ],
    descriptionText: "A lightweight native Markdown editor for macOS"
)

expect(options[.applicationName] as? String == "MarkLeaf",
       "native about should display the app name")
expect(options[.applicationVersion] as? String == "1.3.1",
       "market version should be passed as the version that the system prefixes with 'Version'")
expect(options[.version] as? String == "310",
       "build number should be passed as the build version shown in parentheses")
expect(options[.credits] is NSAttributedString,
       "description should be passed as credits so it renders in the panel info area")

print("PASS")
