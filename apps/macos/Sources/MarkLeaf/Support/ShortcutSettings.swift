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
        ShortcutEntry(command: "open", titleKey: "打开…", defaultKey: "o", defaultMask: [.command]),
        ShortcutEntry(command: "save", titleKey: "保存", defaultKey: "s", defaultMask: [.command]),
        ShortcutEntry(command: "saveAs", titleKey: "另存为…", defaultKey: "S", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "exportPdf", titleKey: "导出 PDF…", defaultKey: "e", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "print", titleKey: "打印…", defaultKey: "p", defaultMask: [.command]),
        ShortcutEntry(command: "undo", titleKey: "撤销", defaultKey: "z", defaultMask: [.command]),
        ShortcutEntry(command: "redo", titleKey: "重做", defaultKey: "Z", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "cut", titleKey: "剪切", defaultKey: "x", defaultMask: [.command]),
        ShortcutEntry(command: "copy", titleKey: "拷贝", defaultKey: "c", defaultMask: [.command]),
        ShortcutEntry(command: "paste", titleKey: "粘贴", defaultKey: "v", defaultMask: [.command]),
        ShortcutEntry(command: "find", titleKey: "查找", defaultKey: "f", defaultMask: [.command]),
        ShortcutEntry(command: "replace", titleKey: "替换", defaultKey: "f", defaultMask: [.command, .option]),
        ShortcutEntry(command: "toggleBold", titleKey: "加粗", defaultKey: "b", defaultMask: [.command]),
        ShortcutEntry(command: "toggleItalic", titleKey: "斜体", defaultKey: "i", defaultMask: [.command]),
        ShortcutEntry(command: "toggleUnderline", titleKey: "下划线", defaultKey: "u", defaultMask: [.command]),
        ShortcutEntry(command: "formatPainter", titleKey: "格式刷", defaultKey: "c", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "formatPainterApply", titleKey: "应用格式刷", defaultKey: "v", defaultMask: [.command, .shift]),
        ShortcutEntry(command: "insertLink", titleKey: "插入超链接…", defaultKey: "k", defaultMask: [.command]),
        ShortcutEntry(command: "promoteHeading", titleKey: "提升标题级别", defaultKey: ".", defaultMask: [.command]),
        ShortcutEntry(command: "demoteHeading", titleKey: "降低标题级别", defaultKey: ",", defaultMask: [.command]),
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

    /// 校验新组合。key 为 `charactersIgnoringModifiers` 的小写单字符，mask 为取交集后的修饰键。
    static func validate(key: String, mask: NSEvent.ModifierFlags, for command: String) -> ShortcutConflict {
        let required: NSEvent.ModifierFlags = [.command, .option, .control]
        guard mask.intersection(required) != [] else { return .invalid }
        guard key.count == 1, let scalar = key.unicodeScalars.first, scalar.value < 0xF700,
              (scalar.value >= 0x30 && scalar.value <= 0x39) ||   // 0-9
                (scalar.value >= 0x41 && scalar.value <= 0x5A) || // A-Z
                (scalar.value >= 0x61 && scalar.value <= 0x7A) || // a-z
                scalar.value == 0x20 || "=,-.".contains(Character(scalar)) else {
            return .invalid
        }
        // 系统高风险组合：⌘Space、⌃⌘F（全屏），以及会被系统菜单抢占的 ⌘Q/⌘W/⌘H/⌥⌘H/⌘M/⌘,。
        let systemCmdKeys: Set<String> = ["q", "w", "h", "m", ","]
        if (key == " " && mask.contains(.command)) ||
            (key.lowercased() == "f" && mask.contains(.control) && mask.contains(.command)) ||
            (mask.contains(.command) && systemCmdKeys.contains(key)) {
            return .systemReserved
        }
        for entry in ShortcutCatalog.entries where entry.command != command {
            guard let (ek, em) = ShortcutSettings.shared.effectiveKey(for: entry) else { continue }
            if ek == key && em == mask {
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
        parts.append(key.uppercased())
        return parts.joined()
    }
}
