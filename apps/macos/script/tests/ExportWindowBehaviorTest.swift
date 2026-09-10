import AppKit
@testable import MarkLeaf

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func descendants(_ view: NSView) -> [NSView] { [view] + view.subviews.flatMap(descendants) }
func settle() { RunLoop.main.run(until: Date().addingTimeInterval(0.35)) }

_ = NSApplication.shared
let session = EditorSession()
let saved = PersistedExportSettings(format: "image", imageMaxHeight: 2000, imageContentWidth: 640,
                                   imageScale: 3, imageFormat: "jpg", imageJpegQuality: 67)
let restored = session.exportOptions(from: saved)
expect(restored.imageContentWidth == 640 && restored.imageMaxHeight == 2000
    && restored.imageScale == 3 && restored.imageFormat == "jpg" && restored.imageJpegQuality == 67,
    "last settings must restore all image options")
expect(restored.fileExtension == "jpg", "last JPG export must use .jpg")

for language in ["zh-Hans", "en", "ja", "zh-Hant"] {
    SettingsService.shared.update { settings in
        settings.exportSettings = saved
        settings.displayLanguage = language
    }
    let tab = DocumentTabID()
    let controller = ExportWindowController(
        lease: ExportSessionLease(tabID: tab, session: session, container: nil),
        binding: ExportBinding(tabID: tab, contentRevision: 0))
    let window = controller.window!
    window.orderFront(nil)
    let root = window.contentView!
    let views = descendants(root)
    let selector = views.compactMap { $0 as? NSSegmentedControl }.first!
    let jpg = views.compactMap { $0 as? NSButton }.first { $0.title == "JPG" }!
    let png = views.compactMap { $0 as? NSButton }.first { $0.title == "PNG" }!
    expect(jpg.state == .on && png.state == .off, "restored JPG must select only JPG")
    for size in [NSSize(width: 960, height: 680), NSSize(width: 1160, height: 800), NSSize(width: 1400, height: 900)] {
        window.setContentSize(size)
        root.layoutSubtreeIfNeeded()
        settle()
        expect(abs(root.frame.width - size.width) < 1, "window must resize to requested width")
        let host = selector.superview!
        expect(selector.frame.minX >= -1 && selector.frame.maxX <= host.bounds.width + 1,
               "all format segments must fit host: \(selector.frame) in \(host.bounds)")
        for control in views.compactMap({ $0 as? NSControl }) where !control.isHiddenOrHasHiddenAncestor {
            let frame = control.convert(control.bounds, to: root)
            // Preview scrollers may be outside their clipping rect by design.
            if frame.minX < 380 && !(control is NSScroller) {
                expect(frame.minX >= -1 && frame.maxX <= 381, "sidebar control must fit: \(control) \(frame)")
            }
        }
    }
    png.performClick(nil)
    jpg.performClick(nil)
    settle()
    let slider = views.compactMap { $0 as? NSSlider }.first!
    expect(!slider.isHiddenOrHasHiddenAncestor, "rapid PNG/JPG switching must keep JPEG quality visible")
    png.performClick(nil)
    settle()
    expect(slider.isHiddenOrHasHiddenAncestor, "PNG must hide JPEG quality")

    let width = views.compactMap { $0 as? NSTextField }.first { $0.identifier?.rawValue == "imageContentWidth" }!
    let button = views.compactMap { $0 as? NSButton }.first { $0.identifier?.rawValue == "exportButton" }!
    controller.controlTextDidBeginEditing(Notification(name: NSControl.textDidBeginEditingNotification, object: width))
    func edit(_ text: String) {
        width.stringValue = text
        NotificationCenter.default.post(name: NSControl.textDidChangeNotification, object: width)
    }
    edit("4001")
    expect(width.stringValue == "640", "over-limit input must immediately restore prior text")
    edit("64.0")
    expect(width.stringValue == "640", "integer dimensions must reject decimal input")
    edit("")
    expect(!button.isEnabled, "empty dimensions must disable export")
    edit("1")
    expect(width.stringValue == "1" && !button.isEnabled, "below-minimum prefix must remain editable")
    edit("1200")
    expect(button.isEnabled, "valid dimensions must enable export")
    edit("0")
    controller.controlTextDidEndEditing(Notification(name: NSControl.textDidEndEditingNotification, object: width))
    expect(width.stringValue == "640" && button.isEnabled, "invalid focus-leave must restore original value")
    if let sheet = window.attachedSheet { window.endSheet(sheet) }
    edit("99999")
    expect(width.stringValue == "640", "monitor must track value restored on focus-leave")

    if let directory = ProcessInfo.processInfo.environment["MARKLEAF_TEST_SCREENSHOTS"] {
        jpg.performClick(nil)
        settle()
        if let bitmap = root.bitmapImageRepForCachingDisplay(in: root.bounds) {
            root.cacheDisplay(in: root.bounds, to: bitmap)
            try bitmap.representation(using: .png, properties: [:])?.write(
                to: URL(fileURLWithPath: directory).appendingPathComponent("export-\(language).png"))
        }
    }
    controller.close()
    print("PASS: \(language) layout, radio state, input rejection and focus-leave")
}
