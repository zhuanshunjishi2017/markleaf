import AppKit
import Foundation

enum L10n {
    static func t(_ text: String) -> String { text }
    static func f(_ format: String, _ args: CVarArg...) -> String {
        String(format: format, arguments: args)
    }
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func descendants(of view: NSView) -> [NSView] {
    view.subviews.flatMap { [$0] + descendants(of: $0) }
}

_ = NSApplication.shared
let controller = OptionalFontsWindowController()
guard let window = controller.window, let content = window.contentView else {
    fatalError("optional font window was not created")
}
let views = descendants(of: content)
guard let table = views.compactMap({ $0 as? NSTableView }).first else {
    fatalError("optional font table is missing")
}
let buttonTitles = Set(views.compactMap { ($0 as? NSButton)?.title })

expect(window.title == "安装可选字体", "window title should describe optional fonts")
expect(window.contentMinSize.width >= 680, "window must not collapse into a narrow strip")
expect(table.numberOfColumns == 3, "table should expose style, pack, and status columns")
expect(controller.numberOfRows(in: table) == OptionalFontCatalog.packs.count,
       "table should show every catalog pack, including unavailable packs")
for title in ["安装所选", "安装全部可用字体", "卸载所选", "查看许可证", "关闭"] {
    expect(buttonTitles.contains(title), "missing action button: \(title)")
}

print("OptionalFontsWindow layout tests passed")
