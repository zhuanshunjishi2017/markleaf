/* MarkLeaf 共享渲染内核的 public API 契约。
 *
 * 边界定义：
 * - 内核只负责文档渲染、编辑、导出与排版，不含任何宿主通信与宿主 UI。
 * - 内核不感知具体宿主。宿主差异一律通过 host-capabilities 的能力注入表达，
 *   禁止在内核内部出现 if (vscode) / if (macOS) 之类的宿主类型判断。
 * - 三个产品（macOS、Windows、VS Code 扩展）均只经由包的公开入口消费内核，
 *   不得深入 import 内核内部模块。
 *
 * 不属于内核、由各宿主适配层自行持有：
 * - main.ts / protocol.ts   原生宿主（macOS、Windows）入口与消息协议
 * - vscode*.ts / vscode.css VS Code 扩展 webview 适配层
 *
 * 分层约束：此渲染入口依赖 DOM；/document 为不依赖 DOM 的文件语义入口。
 * editor-state.ts 是位于内核之下的纯类型契约层，不依赖 DOM，被扩展进程等
 * 无 DOM 环境直接引用；此处一并 re-export 只为 webview 侧取用方便，
 * 无 DOM 的宿主进程使用包的 /editor-state 或 /command-state 入口；
 * 构建工具使用 /build 入口，不从此 DOM 入口加载。
 *
 * 本文件同时是内核抽为独立包后的包入口，因此此处的导出即为长期兼容契约：
 * 新增导出视为 minor，移除或改签名视为 breaking。
 */

// ---- 编辑器核心：文档渲染、编辑命令、查找替换、选区与剪贴板 ----
export * from './editor'
export * from './editor-state'
export * from './command-state'
export * from './document-mode'
export * from './markdown-underline'

// ---- 源码视图 ----
export * from './source-editor'

// ---- 富文本渲染扩展：公式与图表 ----
export * from './math'
export * from './mermaid'

// ---- 导出：HTML 生成与分页 ----
export * from './export-html'
export * from './export-pagination'

// ---- 编辑辅助交互 ----
export * from './format-painter'
export * from './format-painter-dom-events'
export * from './block-handle'
export * from './editor-interactions'
export { selectEditorContextAt } from './document-pointer'
export * from './outline'
export * from './reading-behavior'
export * from './typography'
export * from './image-resources'

// ---- 视图行为：选区同步、滚动与缩放 ----
export * from './dom-selection-sync'
export * from './native-selection'
export * from './scrollbar-motion'
export * from './zoom-anchor'

// ---- 宿主抽象：能力注入与命令准入，内核感知宿主差异的唯一途径 ----
export * from './host-capabilities'
export * from './host-command-policy'

// ---- 共享文案 ----
export * from './shared-editor-strings'

// ---- ProseMirror 透出 ----
// 宿主适配层需要构造选区，但不得自行依赖 prosemirror-*：各宿主一旦安装出
// 第二份副本，ProseMirror 的私有属性（DecorationSet.findInner、Mapping._maps
// 等）会被 TypeScript 判定为不同声明而类型不兼容，运行时也会出现重复实例。
// 渲染栈的版本由内核唯一持有，宿主一律经此取用。
export { NodeSelection, Selection, TextSelection, type EditorState } from '@tiptap/pm/state'
export type { Editor } from '@tiptap/core'
