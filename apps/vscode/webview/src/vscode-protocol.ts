// VS Code-specific document transport. Native hosts keep protocol.ts unchanged.
import { isExportOptions, type ExportOptions, type ExportHtmlResult } from './vscode-export-options'
// 协议层被扩展进程（Node，无 DOM）与 webview 共同引用，因此只能依赖内核之下的
// 纯类型契约层，不能经 ./core 引入渲染内核，否则会把 DOM 类型带入扩展进程。
import type { EditorCommandState } from '@markleaf/editor-core/editor-state'
import type { MarkLeafSettings } from './vscode-settings'
import { isFormatCommand, type ShortcutSettings } from './vscode-shortcuts'

export type ActionContext = Partial<EditorCommandState> & {
  editable?: boolean
  imageSource?: string
  linkHref?: string
  footnoteLabels?: string[]
}
export type EditorCommand = { command: string; text?: string }
export type ImageUpload = { name: string; data: string }
export type WebviewFocus = 'document' | 'input' | null
export type DocumentSnapshot = {
  type: 'document'
  version: number
  markdown: string
  writable: boolean
}

export type HostAction = 'save' | 'undo' | 'redo' | 'openSource' | 'openSourceBeside'
  | 'format' | 'block' | 'insertLink' | 'insertImage' | 'insertImageUrl' | 'image' | 'importImages' | 'codeLanguage'
  | 'find' | 'replace' | 'toggleOutline' | 'toggleFocus' | 'toggleTypewriter' | 'toggleRead' | 'zoomIn' | 'zoomOut' | 'zoomReset'
  | 'exportDocument' | 'exportPdf' | 'exportHtml' | 'exportImage' | 'print' | 'exportLast'
  | 'copyMarkdown' | 'copyPlainText' | 'copyHtml' | 'pastePlainText' | 'preferences' | 'help' | 'shortcuts' | 'formatCommand'

export type WebviewMessage =
  | { type: 'export'; options: ExportOptions; preview: boolean }
  | { type: 'cancelExport' }
  | { type: 'exportRendered'; requestId: number; result?: ExportHtmlResult; error?: string }
  | { type: 'ready'; mac?: boolean }
  | { type: 'focus'; target: WebviewFocus }
  | { type: 'edit'; sequence: number; baseVersion: number; markdown: string }
  | { type: 'action'; action: HostAction; command?: string; context?: ActionContext; files?: ImageUpload[] }
  | { type: 'updateShortcut'; requestId: number; command: string; binding: string }
  | { type: 'openShortcutSettings' }
  | { type: 'updateSetting'; key: keyof MarkLeafSettings; value: string | number | boolean }
  | { type: 'resolveImages'; paths: string[] }
  | { type: 'flushed'; requestId: number; success: boolean }
  | { type: 'recoverDraft'; markdown: string }
  | { type: 'copy'; text: string }
  | { type: 'openLink'; href: string }
  | { type: 'error'; message: string }

export type ExtensionMessage = DocumentSnapshot
  | { type: 'exportOptions'; options: ExportOptions }
  | { type: 'renderExport'; requestId: number; markdown: string; title: string; options: ExportOptions; settings: MarkLeafSettings; language: string }
  | { type: 'exportFinished'; message: string; error?: boolean }
  | { type: 'requestAction'; action: HostAction }
  | { type: 'requestFormatCommand'; command: string }
  | { type: 'shortcutSaved'; requestId: number; shortcuts: ShortcutSettings; error?: string }
  | { type: 'recovered'; document: DocumentSnapshot }
  | { type: 'actionFinished' }
  | { type: 'accepted'; sequence: number; version: number }
  | { type: 'rejected'; sequence: number; document: DocumentSnapshot; error: string }
  | { type: 'flush'; requestId: number }
  | { type: 'images'; urls: Record<string, string> }
  | { type: 'command'; command: string; text?: string }
  | { type: 'settings'; settings: MarkLeafSettings; customCss?: string; language?: string; shortcuts?: ShortcutSettings }
  | { type: 'error'; message: string }

export function isWebviewMessage(value: unknown): value is WebviewMessage {
  if (!value || typeof value !== 'object') return false
  const message = value as Record<string, unknown>
  const integer = (n: unknown): n is number => Number.isSafeInteger(n) && Number(n) >= 0
  switch (message.type) {
    case 'export': return isExportOptions(message.options) && typeof message.preview === 'boolean'
    case 'cancelExport': return true
    case 'exportRendered': {
      const result = message.result as Record<string, unknown> | undefined
      return integer(message.requestId) && ((typeof message.error === 'string' && result === undefined)
        || (message.error === undefined && !!result && typeof result.html === 'string' && Array.isArray(result.images) && result.images.every(p => typeof p === 'string')))
    }
    case 'ready': return message.mac === undefined || typeof message.mac === 'boolean'
    case 'focus': return message.target === null || message.target === 'document' || message.target === 'input'
    case 'edit': return integer(message.sequence) && integer(message.baseVersion) && typeof message.markdown === 'string'
    case 'action': return ['exportDocument', 'exportPdf', 'exportHtml', 'exportImage', 'print', 'exportLast', 'save', 'undo', 'redo', 'openSource', 'openSourceBeside', 'format', 'block', 'insertLink', 'insertImage', 'insertImageUrl', 'image', 'importImages', 'codeLanguage', 'find', 'replace', 'toggleOutline', 'toggleFocus', 'toggleTypewriter', 'toggleRead', 'zoomIn', 'zoomOut', 'zoomReset', 'copyMarkdown', 'copyPlainText', 'copyHtml', 'pastePlainText', 'preferences', 'help', 'formatCommand'].includes(String(message.action))
      && (message.action !== 'formatCommand' || isFormatCommand(message.command))
      && (message.context === undefined || isActionContext(message.context))
      && (message.action !== 'importImages' || (Array.isArray(message.files) && message.files.length > 0
        && message.files.every(file => file && typeof file.name === 'string' && typeof file.data === 'string'
          && file.data.length <= 24 * 1024 * 1024 && /^[A-Za-z0-9+/]*={0,2}$/.test(file.data))))
    case 'updateSetting': return typeof message.key === 'string' && ['string', 'boolean', 'number'].includes(typeof message.value)
    case 'updateShortcut': return integer(message.requestId) && isFormatCommand(message.command) && typeof message.binding === 'string'
    case 'openShortcutSettings': return true
    case 'resolveImages': return Array.isArray(message.paths) && message.paths.every(p => typeof p === 'string')
    case 'flushed': return integer(message.requestId) && typeof message.success === 'boolean'
    case 'recoverDraft': return typeof message.markdown === 'string'
    case 'copy': return typeof message.text === 'string'
    case 'openLink': return typeof message.href === 'string'
    case 'error': return typeof message.message === 'string'
    default: return false
  }
}

function isActionContext(value: unknown): value is ActionContext {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false
  return Object.entries(value).every(([key, item]) => key === 'footnoteLabels'
    ? Array.isArray(item) && item.every(label => typeof label === 'string')
    : item === null || ['boolean', 'string', 'number'].includes(typeof item))
}
