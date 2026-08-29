import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(
    WelcomeResource.candidateFileNames(displayLanguage: "en") == ["welcome.en.md", "welcome.zh-Hans.md"],
    "English should use its localized welcome document and then Simplified Chinese as fallback"
)
expect(
    WelcomeResource.candidateFileNames(displayLanguage: "unsupported") == ["welcome.zh-Hans.md"],
    "unsupported languages should fall back to Simplified Chinese"
)
let cache = URL(fileURLWithPath: "/tmp/markleaf-welcome-cache", isDirectory: true)
expect(
    WelcomeResource.cachedURL(cacheDirectory: cache).lastPathComponent == "welcome.md",
    "the editable cached copy should use the stable welcome.md file name"
)

let fixtureRoot = FileManager.default.temporaryDirectory
    .appendingPathComponent("markleaf-welcome-resource-\(UUID().uuidString).bundle", isDirectory: true)
let resources = fixtureRoot.appendingPathComponent("Contents/Resources/Welcome", isDirectory: true)
try FileManager.default.createDirectory(at: resources, withIntermediateDirectories: true)
try """
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.markleaf.WelcomeResourceTest</string>
<key>CFBundlePackageType</key><string>BNDL</string>
</dict></plist>
""".write(to: fixtureRoot.appendingPathComponent("Contents/Info.plist"), atomically: true, encoding: .utf8)
try "English welcome".write(
    to: resources.appendingPathComponent("welcome.en.md"),
    atomically: true,
    encoding: .utf8
)
defer { try? FileManager.default.removeItem(at: fixtureRoot) }
let fixtureBundle = Bundle(url: fixtureRoot)!
expect(
    WelcomeResource.bundledURL(in: fixtureBundle, displayLanguage: "en")?.lastPathComponent == "welcome.en.md",
    "the resource resolver should load the localized document from the Welcome bundle directory"
)

print("PASS")
