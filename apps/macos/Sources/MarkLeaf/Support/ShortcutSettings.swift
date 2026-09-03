import AppKit

/// 可自定义快捷键的命令目录项。
struct ShortcutEntry {
    let command: String
    let titleKey: String
    let defaultKey: String
    let defaultMask: NSEvent.ModifierFlags
}

/// 快捷键目录：仅包含在原生菜单里带默认快捷键的命令（与「快捷键」窗口一致）。
enum ShortcutCatalog {
    static let entries: [ShortcutEntry] = [
        ShortcutEntry(command: "new", titleKey: "新建文档", defaultKey: "n", defaultMask: [.command]),
        ShortcutEntry(command: "newPlainText", titleKey: "新建文本文件", defaultKey: "n", defaultMask: [.command, .option]),
        ShortcutEntry(command: "newWindow", titleKey: "新建窗口", defaultKey: "N", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "open", titleKey: "打开…", defaultKey: "o", defaultMask: [.command]),
        ShortcutEntry(command: "save", titleKey: "保存", defaultKey: "s", defaultMask: [.command]),
        ShortcutEntry(command: "saveAll", titleKey: "保存全部", defaultKey: "s", defaultMask: [.command, .option]),
        ShortcutEntry(command: "saveAs", titleKey: "另存为…", defaultKey: "S", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "export", titleKey: "导出…", defaultKey: "e", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "print", titleKey: "打印…", defaultKey: "p", defaultMask: [.command]),
        ShortcutEntry(command: "undo", titleKey: "撤销", defaultKey: "z", defaultMask: [.command]),
        ShortcutEntry(command: "redo", titleKey: "重做", defaultKey: "Z", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "cut", titleKey: "剪切", defaultKey: "x", defaultMask: [.command]),
        ShortcutEntry(command: "copy", titleKey: "拷贝", defaultKey: "c", defaultMask: [.command]),
        ShortcutEntry(command: "paste", titleKey: "粘贴", defaultKey: "v", defaultMask: [.command]),
        ShortcutEntry(command: "find", titleKey: "查找与替换", defaultKey: "f", defaultMask: [.command]),
        ShortcutEntry(command: "pastePlainText", titleKey: "粘贴为纯文本", defaultKey: "V", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "selectAll", titleKey: "全选", defaultKey: "a", defaultMask: [.command]),
        ShortcutEntry(command: "toggleBold", titleKey: "加粗", defaultKey: "b", defaultMask: [.command]),
        ShortcutEntry(command: "toggleItalic", titleKey: "斜体", defaultKey: "i", defaultMask: [.command]),
        ShortcutEntry(command: "toggleUnderline", titleKey: "下划线", defaultKey: "u", defaultMask: [.command]),
        ShortcutEntry(command: "toggleStrike", titleKey: "删除线", defaultKey: "d", defaultMask: [.command]),
        ShortcutEntry(command: "toggleCode", titleKey: "行内代码", defaultKey: "`", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleHighlight", titleKey: "高亮", defaultKey: "h", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "clearFormat", titleKey: "清除格式", defaultKey: "\\", defaultMask: [.command]),
        ShortcutEntry(command: "formatPainter", titleKey: "格式刷", defaultKey: "c", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "formatPainterApply", titleKey: "应用格式刷", defaultKey: "v", defaultMask: [.command, .control]),
        ShortcutEntry(command: "insertLink", titleKey: "插入超链接…", defaultKey: "k", defaultMask: [.command]),
        ShortcutEntry(command: "insertMathInline", titleKey: "行内公式", defaultKey: "m", defaultMask: [.control, .option]),
        ShortcutEntry(command: "insertMathBlock", titleKey: "段间公式", defaultKey: "m", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "insertHorizontalRule", titleKey: "水平线", defaultKey: "l", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "insertFootnote", titleKey: "插入注释…", defaultKey: "f", defaultMask: [.command, .option]),
        ShortcutEntry(command: "insertTable", titleKey: "插入表格", defaultKey: "t", defaultMask: [.command]),
        ShortcutEntry(command: "promoteHeading", titleKey: "提升标题级别", defaultKey: ".", defaultMask: [.command, .option]),
        ShortcutEntry(command: "demoteHeading", titleKey: "降低标题级别", defaultKey: ",", defaultMask: [.command, .option]),
        ShortcutEntry(command: "setParagraph", titleKey: "正文", defaultKey: "0", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "setHeading1", titleKey: "1级标题", defaultKey: "1", defaultMask: [.command]),
        ShortcutEntry(command: "setHeading2", titleKey: "2级标题", defaultKey: "2", defaultMask: [.command]),
        ShortcutEntry(command: "setHeading3", titleKey: "3级标题", defaultKey: "3", defaultMask: [.command]),
        ShortcutEntry(command: "setHeading4", titleKey: "4级标题", defaultKey: "4", defaultMask: [.command]),
        ShortcutEntry(command: "setHeading5", titleKey: "5级标题", defaultKey: "5", defaultMask: [.command]),
        ShortcutEntry(command: "setHeading6", titleKey: "6级标题", defaultKey: "6", defaultMask: [.command]),
        ShortcutEntry(command: "toggleBlockquote", titleKey: "引用", defaultKey: "q", defaultMask: [.command, .control]),
        ShortcutEntry(command: "toggleCodeBlock", titleKey: "代码块", defaultKey: "k", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "toggleBulletList", titleKey: "无序列表", defaultKey: "]", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "toggleOrderedList", titleKey: "有序列表", defaultKey: "[", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "toggleTaskList", titleKey: "任务列表", defaultKey: "t", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "indentListItem", titleKey: "增加列表缩进", defaultKey: "]", defaultMask: [.command]),
        ShortcutEntry(command: "outdentListItem", titleKey: "减少列表缩进", defaultKey: "[", defaultMask: [.command]),
        ShortcutEntry(command: "openFolder", titleKey: "打开文件夹…", defaultKey: "o", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "closeFolder", titleKey: "关闭文件夹", defaultKey: "q", defaultMask: [.control, .option]),
        ShortcutEntry(command: "toggleSidebar", titleKey: "显示侧栏", defaultKey: "z", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleStatusBar", titleKey: "显示状态栏", defaultKey: "x", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleEditorFocusMode", titleKey: "编辑器专注模式", defaultKey: "\u{F708}", defaultMask: []),
        ShortcutEntry(command: "toggleTypewriterMode", titleKey: "打字机模式", defaultKey: "\u{F709}", defaultMask: []),
        ShortcutEntry(command: "sourceMode", titleKey: "源码模式", defaultKey: "u", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleFocusMode", titleKey: "专注模式", defaultKey: "f", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "zoomIn", titleKey: "放大", defaultKey: "=", defaultMask: [.command]),
        ShortcutEntry(command: "zoomOut", titleKey: "缩小", defaultKey: "-", defaultMask: [.command]),
        ShortcutEntry(command: "resetZoom", titleKey: "重置为100%", defaultKey: "0", defaultMask: [.command]),
    ]

    static func entry(for command: String) -> ShortcutEntry? {
        entries.first { $0.command == command }
    }
}

/// 快捷键持久化与校验（macOS）。UserDefaults 键：customShortcuts。
final class ShortcutSettings {
    static let shared = ShortcutSettings()

    struct Binding: Codable, Equatable {
        var key: String
        var modifiers: UInt

        var mask: NSEvent.ModifierFlags {
            NSEvent.ModifierFlags(rawValue: modifiers)
        }
    }

    /// 持久化状态：overrides 为自定义绑定；cleared 为“已清除（无快捷键）”的命令。
    /// “清除”与“恢复默认”语义不同：清除 = 无快捷键；恢复默认 = 使用内置默认快捷键。
    private struct Persisted: Codable {
        var overrides: [String: Binding] = [:]
        var cleared: [String] = []
    }

    private static let storageKey = "customShortcuts"
    private var state: Persisted

    private init() {
        if let data = UserDefaults.standard.data(forKey: Self.storageKey),
           let decoded = try? JSONDecoder().decode(Persisted.self, from: data) {
            state = decoded
        } else if let data = UserDefaults.standard.data(forKey: Self.storageKey),
                  let legacy = try? JSONDecoder().decode([String: Binding].self, from: data) {
            // 旧格式迁移：仅含 overrides。
            state = Persisted(overrides: legacy, cleared: [])
        } else {
            state = Persisted()
        }
    }

    func binding(for command: String) -> Binding? {
        state.overrides[command]
    }

    /// 菜单项实际使用的快捷键：有自定义用自定义；被“清除”则无快捷键（nil）；
    /// 否则用内置默认。
    func effectiveKey(for entry: ShortcutEntry) -> (key: String, mask: NSEvent.ModifierFlags)? {
        if let binding = state.overrides[entry.command] {
            return (binding.key, binding.mask)
        }
        if state.cleared.contains(entry.command) {
            return nil
        }
        return (entry.defaultKey, entry.defaultMask)
    }

    /// 录制新快捷键：写入自定义绑定，并取消“已清除”标记。
    func set(_ binding: Binding?, for command: String) {
        state.cleared.removeAll { $0 == command }
        if let binding {
            state.overrides[command] = binding
        } else {
            state.overrides.removeValue(forKey: command)
        }
        persist()
    }

    /// 清除快捷键：移除自定义绑定并标记为“无快捷键”。
    func clear(_ command: String) {
        state.overrides.removeValue(forKey: command)
        if !state.cleared.contains(command) {
            state.cleared.append(command)
        }
        persist()
    }

    /// 恢复默认：移除自定义绑定并取消“已清除”标记。
    func restoreDefault(_ command: String) {
        state.overrides.removeValue(forKey: command)
        state.cleared.removeAll { $0 == command }
        persist()
    }

    func resetAll() {
        state = Persisted()
        persist()
    }

    private func persist() {
        if let data = try? JSONEncoder().encode(state) {
            UserDefaults.standard.set(data, forKey: Self.storageKey)
        }
    }

    /// 规范按键：普通字符转为小写；功能键的 Unicode 事件字符转为 `f1`–`f24`。
    static func canonicalKey(_ key: String) -> String? {
        guard let scalar = key.unicodeScalars.first, key.unicodeScalars.count == 1 else { return nil }
        if (0xF704...0xF71B).contains(scalar.value) {
            return "f\(scalar.value - 0xF703)"
        }
        let lowered = key.lowercased()
        if lowered.hasPrefix("f"),
           let number = Int(lowered.dropFirst()), (1...24).contains(number) {
            return lowered
        }
        return lowered
    }

    /// 校验新组合。key 为 `charactersIgnoringModifiers` 产生的字符或功能键 Unicode 字符。
    static func validate(key: String, mask: NSEvent.ModifierFlags, for command: String) -> ShortcutConflict {
        let required: NSEvent.ModifierFlags = [.command, .option, .control]
        guard let canonical = canonicalKey(key),
              let scalar = canonical.unicodeScalars.first else { return .invalid }
        var isFunctionKey = false
        if canonical.hasPrefix("f"),
           let functionNumber = Int(canonical.dropFirst()),
           (1...24).contains(functionNumber) {
            isFunctionKey = true
        }
        if !isFunctionKey {
            guard mask.intersection(required) != [] else { return .invalid }
            guard (scalar.value >= 0x30 && scalar.value <= 0x39) ||   // 0-9
                  (scalar.value >= 0x41 && scalar.value <= 0x5A) || // A-Z
                  (scalar.value >= 0x61 && scalar.value <= 0x7A) || // a-z
                scalar.value == 0x20 || "=,-.`\\/[]".contains(String(scalar)) else {
                return .invalid
            }
        }
        // 系统高风险组合：⌘Space、⌃⌘F（全屏），以及 macOS 精确占用的 ⌘Q/⌘W/⌘H/⌥⌘H/⌘M/⌘,。
        let systemCmdKeys: Set<String> = ["q", "w", "h", "m", ","]
        let isHideOthers = canonical == "h" && mask == [.command, .option]
        if (canonical == " " && mask.contains(.command)) ||
            (canonical == "f" && mask.contains(.control) && mask.contains(.command)) ||
            (mask.contains(.command) && systemCmdKeys.contains(canonical) && (mask == [.command] || isHideOthers)) {
            return .systemReserved
        }
        for entry in ShortcutCatalog.entries where entry.command != command {
            guard let (ek, em) = ShortcutSettings.shared.effectiveKey(for: entry) else { continue }
            if canonicalKey(ek) == canonical && em == mask {
                return .duplicate(command: entry.command)
            }
        }
        return .none
    }
}

enum ShortcutConflict: Equatable {
    case none
    case invalid
    case systemReserved
    case duplicate(command: String)
}

/// 快捷键显示（⌘⇧⌥⌃ + 大写字符）。
enum ShortcutDisplay {
    static func string(key: String, mask: NSEvent.ModifierFlags) -> String {
        var parts: [String] = []
        if mask.contains(.control) { parts.append("⌃") }
        if mask.contains(.option) { parts.append("⌥") }
        if mask.contains(.shift) { parts.append("⇧") }
        if mask.contains(.command) { parts.append("⌘") }
        if let scalar = key.unicodeScalars.first, key.unicodeScalars.count == 1,
           (0xF704...0xF71B).contains(scalar.value) {
            parts.append("F\(scalar.value - 0xF703)")
            return parts.joined()
        }
        let specialKeyNames: [String: String] = [
            "`": "`", "\\": "\\", "/": "/", "[": "[", "]": "]",
        ]
        parts.append(specialKeyNames[key] ?? key.uppercased())
        return parts.joined()
    }
}
