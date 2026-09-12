import AppKit

struct EditorContextMenuState: Equatable {
    let isSourceMode: Bool
    let isReadOnly: Bool
    let isPlainText: Bool
    let footnoteDefinitionLabel: String?
    let inTable: Bool
    let mermaidSelected: Bool
    let mermaidCount: Int
    let imageSelected: Bool
    let mathInline: Bool
    let mathBlock: Bool
    let codeBlock: Bool
    let codeBlockText: String?
    let frontMatterActive: Bool
    let expandedSource: Bool
    let actions: [String: EditorActionState]
    let semanticContext: EditorSemanticContext

    init(
        isSourceMode: Bool,
        isReadOnly: Bool,
        isPlainText: Bool,
        footnoteDefinitionLabel: String?,
        inTable: Bool,
        mermaidSelected: Bool,
        mermaidCount: Int,
        imageSelected: Bool,
        mathInline: Bool,
        mathBlock: Bool,
        codeBlock: Bool,
        codeBlockText: String?,
        frontMatterActive: Bool = false,
        expandedSource: Bool = false,
        actions: [String: EditorActionState] = [:],
        semanticContext: EditorSemanticContext = .ordinaryBlock
    ) {
        self.isSourceMode = isSourceMode
        self.isReadOnly = isReadOnly
        self.isPlainText = isPlainText
        self.footnoteDefinitionLabel = footnoteDefinitionLabel
        self.inTable = inTable
        self.mermaidSelected = mermaidSelected
        self.mermaidCount = max(0, mermaidCount)
        self.imageSelected = imageSelected
        self.mathInline = mathInline
        self.mathBlock = mathBlock
        self.codeBlock = codeBlock
        self.codeBlockText = codeBlockText
        self.frontMatterActive = frontMatterActive
        self.expandedSource = expandedSource
        self.actions = actions
        self.semanticContext = semanticContext
    }

    /// 右键菜单的启用状态由当前编辑器选区显式计算，避免 AppKit 根据 action target
    /// 再次自动启用本应置灰的剪贴板命令。
    static func preserveExplicitAvailability(in menu: NSMenu) {
        menu.autoenablesItems = false
    }

}
