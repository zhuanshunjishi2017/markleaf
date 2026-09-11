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

// 关于面板图标必须来自 bundle 资源，而不是依赖 LaunchServices 缓存的应用图标；
// 全新生成、位于临时目录的包否则会退回通用文档图标。
let iconURL = URL(fileURLWithPath: NSTemporaryDirectory())
    .appendingPathComponent("markleaf-about-icon-\(UUID().uuidString).icns")
let sourceImage = NSImage(size: NSSize(width: 64, height: 64))
sourceImage.lockFocus()
NSColor.systemBlue.setFill()
NSBezierPath(rect: NSRect(x: 0, y: 0, width: 64, height: 64)).fill()
sourceImage.unlockFocus()
let pngData = NSBitmapImageRep(
    data: sourceImage.tiffRepresentation!
)!.representation(using: .png, properties: [:])!
try! pngData.write(to: iconURL)
defer { try? FileManager.default.removeItem(at: iconURL) }

let withBundleIcon = AboutPanel.standardOptions(
    infoDictionary: ["CFBundleVersion": "310"],
    descriptionText: "credits",
    bundleResourceURL: iconURL,
    applicationIcon: nil
)
expect(withBundleIcon[.applicationIcon] is NSImage,
       "about panel icon must come from the bundled AppIcon resource")

let fallback = NSImage(size: NSSize(width: 32, height: 32))
fallback.lockFocus()
NSColor.systemRed.setFill()
NSBezierPath(rect: NSRect(x: 0, y: 0, width: 32, height: 32)).fill()
fallback.unlockFocus()
let withFallback = AboutPanel.standardOptions(
    infoDictionary: ["CFBundleVersion": "310"],
    descriptionText: "credits",
    bundleResourceURL: nil,
    applicationIcon: fallback
)
expect((withFallback[.applicationIcon] as? NSImage) === fallback,
       "about panel should fall back to the system application icon when the resource is missing")

let withoutIcon = AboutPanel.standardOptions(
    infoDictionary: ["CFBundleVersion": "310"],
    descriptionText: "credits",
    bundleResourceURL: nil,
    applicationIcon: nil
)
expect(withoutIcon[.applicationIcon] == nil,
       "about panel should not register an empty icon")

print("PASS")
