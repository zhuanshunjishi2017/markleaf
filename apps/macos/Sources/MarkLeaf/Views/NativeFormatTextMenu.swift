import AppKit

/// The traditional alternative to the inline row, using the same editor commands.
enum NativeFormatTextMenu {
    static func append(
        to menu: NSMenu,
        actions: [String: EditorActionState],
        canStartFormatPainter: Bool?,
        formatPainterArmed: Bool?,
        makeItem: (String, String) -> NSMenuItem
    ) {
        func item(_ title: String, _ command: String) -> NSMenuItem {
            let result = makeItem(L10n.t(title), command)
            result.representedObject = command
            result.isEnabled = actions[command]?.enabled ?? false
            result.state = actions[command]?.checked == true ? .on : .off
            return result
        }
        menu.addItem(.separator())
        let painter = item("格式刷", "formatPainter")
        painter.isEnabled = (formatPainterArmed ?? false)
            || (canStartFormatPainter ?? actions["formatPainter"]?.enabled ?? false)
        if let formatPainterArmed { painter.state = formatPainterArmed ? .on : .off }
        menu.addItem(painter)
        menu.addItem(item("应用格式刷", "formatPainterApply"))
        menu.addItem(.separator())
        let format = NSMenu(title: L10n.t("格式"))
        format.autoenablesItems = false
        for (title, command) in [
            ("粗体", "toggleBold"), ("斜体", "toggleItalic"),
            ("下划线", "toggleUnderline"), ("删除线", "toggleStrike"), ("高亮", "toggleHighlight"),
        ] {
            format.addItem(item(title, command))
        }
        let formatItem = NSMenuItem(title: L10n.t("格式"), action: nil, keyEquivalent: "")
        formatItem.submenu = format
        menu.addItem(formatItem)
        menu.addItem(.separator())
        menu.addItem(item("清除格式", "clearFormat"))
    }
}
