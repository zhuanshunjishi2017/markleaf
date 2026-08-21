import AppKit

enum L10n {
    static func t(_ text: String) -> String { text }
}

enum StatusBarCommandDisplayMode {
    case always
    case temporary
    case hidden
}

struct StatusBarSettings {
    var sidebarToggleVisible = true
    var commandStatusVisible = true
    var commandDisplayMode = StatusBarCommandDisplayMode.always
    var wordCountVisible = true
    var blockTypeVisible = true
    var positionVisible = true
    var encodingVisible = true
    var newLineVisible = true
    var modeToggleVisible = true
    var zoomVisible = true
}

struct AppSettings {
    var statusBar = StatusBarSettings()
}

final class SettingsService {
    static let shared = SettingsService()
    var settings = AppSettings()

    func update(_ mutate: (inout AppSettings) -> Void) {
        mutate(&settings)
    }
}

private var failures = 0

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        failures += 1
        fputs("FAIL: \(message)\n", stderr)
        return
    }
}

private func descendants(of view: NSView) -> [NSView] {
    [view] + view.subviews.flatMap(descendants)
}

private func frameInContent(_ view: NSView, contentView: NSView) -> NSRect {
    view.convert(view.bounds, to: contentView)
}

_ = NSApplication.shared
let controller = StatusBarSettingsWindowController()
guard let contentView = controller.window?.contentView else {
    fputs("FAIL: status bar settings window must have content\n", stderr)
    exit(1)
}
contentView.layoutSubtreeIfNeeded()

let views = descendants(of: contentView)
let labels = views.compactMap { $0 as? NSTextField }
guard let optionsTitle = labels.first(where: { $0.stringValue == "显示项目" }),
      let commandTitle = labels.first(where: { $0.stringValue == "命令状态显示方式" }),
      let popup = views.compactMap({ $0 as? NSPopUpButton }).first else {
    fputs("FAIL: expected status bar settings controls were not found\n", stderr)
    exit(1)
}

let optionsFrame = frameInContent(optionsTitle, contentView: contentView)
let commandFrame = frameInContent(commandTitle, contentView: contentView)
let popupFrame = frameInContent(popup, contentView: contentView)
let titleOffset = abs(optionsFrame.minX - commandFrame.minX)
let titleToPopupGap = popupFrame.minX - commandFrame.maxX
let contentWidth = contentView.bounds.width

expect((20...28).contains(optionsFrame.minX),
       "options content should start near the window leading inset (left: \(optionsFrame.minX))")
expect(contentWidth < 500,
       "status bar settings window should size to its content (width: \(contentWidth))")
expect(titleOffset <= 1,
       "section titles must share a leading edge (offset: \(titleOffset))")
expect((8...16).contains(titleToPopupGap),
       "command mode popup must stay close to its title (gap: \(titleToPopupGap))")

if failures > 0 {
    exit(1)
}
print("PASS")
