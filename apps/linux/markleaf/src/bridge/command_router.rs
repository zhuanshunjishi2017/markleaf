// 命令路由:透传给内核 vs 宿主本地处理。
// 真源:macOS NativeMenuBuilder.performMenuCommand switch(桌面命令唯一蓝本,
// Windows 无等价表)。宿主集合 = macOS 中由 AppKit/文件系统/窗口处理的命令;
// 其余一律透传(前端对未知 command 忽略,行为与桌面一致)。
// Linux 首发映射差异(revealInFinder/share 等)在 ui 层按 id 再分派。

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CommandRoute {
    /// 宿主本地处理(文件/窗口/剪贴板/导出/设置……),携带命令 id 字面量。
    Host(&'static str),
    /// 直接 `command` 消息透传给编辑器内核(调用方自持原始 id)。
    Passthrough,
}

/// macOS 宿主处理的命令全集(NativeMenuBuilder switch 的宿主分支)。
/// 透传集合由内核 command-state 驱动,无需在此枚举(默认透传)。
const HOST_COMMANDS: &[&str] = &[
    // 文档生命周期
    "new", "newPlainText", "open", "openReadOnly", "save", "saveAll", "saveAs",
    // 窗口/标签
    "newWindow", "openInNewWindow", "closeCurrentTab", "closeOtherTabs",
    "renameActiveTab", "restoreClosedTab", "tabNext", "tabPrevious",
    // 剪贴板(宿主持系统剪贴板事实)
    "cut", "copy", "copyMarkdown", "copyPlain", "copyHtml",
    "paste", "pastePlainText", "copyLinkAddress",
    // 视图/窗口状态
    "find", "toggleSidebar", "workspaceTab", "outlineTab", "toggleDetachedOutline",
    "treeView", "listView", "toggleStatusBar", "sourceMode", "toggleReadOnlyMode",
    "zoomIn", "zoomOut", "resetZoom", "toggleFocusMode", "toggleFullScreen",
    "toggleFollowSystemTheme", "toggleCodeHighlight", "toggleEditorFocusMode",
    "toggleTypewriterMode",
    // 插入/对象宿主侧(选文件/URL/对话框)
    "insertLink", "insertImage", "insertImageFromUrl", "changeImage",
    "saveImageAs", "saveMermaidAs", "insertMathInline", "insertMathBlock",
    "insertFootnote", "resetFootnoteLabel", "goToFootnoteReference",
    "clearFootnoteReferences", "deleteFootnote", "insertMermaid", "editMermaid",
    "rerenderMermaid", "rerenderAllMermaid", "deleteMermaid",
    "declareCodeLanguage", "copyCodeBlock", "editMath", "setMathNumber",
    "editTableCaption", "editImageCaption", "toggleCodeBlock",
    // 导出/打印
    "export", "exportWithLastSettings", "print",
    // 工作区/文件系统
    "openFolder", "closeFolder", "copyActiveTabPath", "copyActiveFileContents",
    "revealActiveTabInWorkspace", "revealActiveTabInFinder",
    // 应用/设置/资源
    "showPreferences", "showAbout", "showThemeSettings", "showShortcuts",
    "checkForUpdates", "installOptionalFonts", "importTheme", "revealThemeFolder",
    "openWelcome", "openHelp", "openChangelog", "openSampleAlert",
    "recoverUnsavedFiles", "restartEditor", "shareActiveTab",
    "goToFootnoteDefinition", "deleteFootnoteReference", "openLink", "editLink",
];

pub fn route(command_id: &str) -> CommandRoute {
    match HOST_COMMANDS.iter().find(|candidate| **candidate == command_id) {
        Some(&host_id) => CommandRoute::Host(host_id),
        None => CommandRoute::Passthrough,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn host_set_has_no_duplicate_or_kernel_overlap() {
        // 关键内核命令绝不能进宿主集合。
        for kernel_id in ["undo", "redo", "toggleBold", "insertTable"] {
            assert!(!HOST_COMMANDS.contains(&kernel_id), "{kernel_id} 误入宿主集合");
        }
    }
}
