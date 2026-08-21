import AppKit

enum EditorContextMenuState {
    /// 右键菜单的启用状态由当前编辑器选区显式计算，避免 AppKit 根据 action target
    /// 再次自动启用本应置灰的剪贴板命令。
    static func preserveExplicitAvailability(in menu: NSMenu) {
        menu.autoenablesItems = false
    }

    /// 主菜单的快捷键必须先交给 Web 编辑器处理，不能依赖刚刚更新中的缓存光标能力。
    /// 编辑器收到命令后会再次执行 captureFormat，负责最终判定是否成功。
    static func formatPainterShortcutEnabled(isSourceMode: Bool) -> Bool {
        !isSourceMode
    }

    static func formatPainterEnabled(
        isSourceMode: Bool,
        canStartFormatPainter: Bool,
        isFormatPainterArmed: Bool
    ) -> Bool {
        // 右键菜单打开时，光标刚刚由 WebView 重定位，能力状态可能仍在消息队列中。
        // 可视化模式下先允许命令进入编辑器，由 captureFormat 做最终校验；源码模式仍禁用。
        _ = canStartFormatPainter
        _ = isFormatPainterArmed
        return !isSourceMode
    }
}
