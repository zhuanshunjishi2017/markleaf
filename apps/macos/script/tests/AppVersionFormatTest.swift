import Foundation
import Darwin

private var failures = 0

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        failures += 1
        fputs("FAIL: \(message)\n", stderr)
        return
    }
}

expect(AppVersion.displayString(version: "1.3.1", build: "42") == "Version 1.3.1 (Build 42)",
       "all locales should use the English version/build display format")
expect(AppVersion.displayString(infoDictionary: [
    "CFBundleShortVersionString": "1.3.1",
    "CFBundleVersion": "310",
]) == "Version 1.3.1 (Build 310)",
       "About should display the marketing version and build from the running bundle")
expect(AppVersion.displayString(infoDictionary: [:]) == "Version unavailable",
       "missing bundle metadata must not fall back to a release-looking mock build")

if failures > 0 {
    exit(1)
}
print("PASS")
