// VS Code-specific document transport. Native hosts keep protocol.ts unchanged.
export type DocumentSnapshot = {
  type: 'document'
  version: number
  markdown: string
  writable: boolean
}

export type HostAction = 'save' | 'undo' | 'redo' | 'openSource' | 'openSourceBeside'
  | 'format' | 'insertLink' | 'insertImage'

export type WebviewMessage =
  | { type: 'ready' }
  | { type: 'edit'; sequence: number; baseVersion: number; markdown: string }
  | { type: 'action'; action: HostAction }
  | { type: 'resolveImages'; paths: string[] }
  | { type: 'flushed'; requestId: number; success: boolean }
  | { type: 'recoverDraft'; markdown: string }
  | { type: 'copy'; text: string }
  | { type: 'codeLanguage'; position: number; language: string }
  | { type: 'openLink'; href: string }
  | { type: 'error'; message: string }

export type ExtensionMessage = DocumentSnapshot
  | { type: 'requestAction'; action: HostAction }
  | { type: 'recovered'; document: DocumentSnapshot }
  | { type: 'actionFinished' }
  | { type: 'accepted'; sequence: number; version: number }
  | { type: 'rejected'; sequence: number; document: DocumentSnapshot; error: string }
  | { type: 'flush'; requestId: number }
  | { type: 'images'; urls: Record<string, string> }
  | { type: 'command'; command: string; text?: string }
  | { type: 'settings'; fontSize: number; maxWidth: number; defaultMode: 'read' | 'edit' }
  | { type: 'error'; message: string }

export function isWebviewMessage(value: unknown): value is WebviewMessage {
  if (!value || typeof value !== 'object') return false
  const message = value as Record<string, unknown>
  const integer = (n: unknown): n is number => Number.isSafeInteger(n) && Number(n) >= 0
  switch (message.type) {
    case 'ready': return true
    case 'edit': return integer(message.sequence) && integer(message.baseVersion) && typeof message.markdown === 'string'
    case 'action': return ['save', 'undo', 'redo', 'openSource', 'openSourceBeside', 'format', 'insertLink', 'insertImage'].includes(String(message.action))
    case 'resolveImages': return Array.isArray(message.paths) && message.paths.every(p => typeof p === 'string')
    case 'flushed': return integer(message.requestId) && typeof message.success === 'boolean'
    case 'recoverDraft': return typeof message.markdown === 'string'
    case 'copy': return typeof message.text === 'string'
    case 'codeLanguage': return integer(message.position) && typeof message.language === 'string'
    case 'openLink': return typeof message.href === 'string'
    case 'error': return typeof message.message === 'string'
    default: return false
  }
}
