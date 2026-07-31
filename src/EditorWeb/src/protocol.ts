export const protocolVersion = 1
export const maximumMessageBytes = 1024 * 1024

export type HostMessage = {
  protocolVersion: number
  type: 'loadDocument' | 'requestSnapshot' | 'command' | 'updateImagePaths'
  requestId?: string
  documentId: string
  revision: number
  payload?: unknown
}

export type EditorMessage = {
  protocolVersion: number
  type:
    | 'ready'
    | 'documentLoaded'
    | 'commandResult'
    | 'dirtyChanged'
    | 'snapshot'
    | 'selectionChanged'
    | 'commandStateChanged'
    | 'outlineChanged'
    | 'requestSave'
    | 'openLink'
    | 'dropFiles'
    | 'pasteImage'
    | 'findResult'
    | 'error'
  requestId?: string
  documentId: string
  revision: number
  payload?: unknown
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: EditorMessage): void
        postMessageWithAdditionalObjects?(message: EditorMessage, additionalObjects: object[]): void
        addEventListener(
          type: 'message',
          listener: (event: MessageEvent<HostMessage>) => void,
        ): void
      }
    }
  }
}

export function postToHost(message: EditorMessage): void {
  window.chrome?.webview?.postMessage(message)
}

export function postToHostWithAdditionalObjects(message: EditorMessage, additionalObjects: object[]): void {
  window.chrome?.webview?.postMessageWithAdditionalObjects?.(message, additionalObjects)
}

export function isHostMessage(value: unknown): value is HostMessage {
  if (!value || typeof value !== 'object') {
    return false
  }

  let serialized: string
  try {
    serialized = JSON.stringify(value)
  } catch {
    return false
  }
  if (new TextEncoder().encode(serialized).byteLength > maximumMessageBytes) {
    return false
  }

  const message = value as Partial<HostMessage>
  return message.protocolVersion === protocolVersion
    && (message.type === 'loadDocument'
      || message.type === 'requestSnapshot'
      || message.type === 'command'
      || message.type === 'updateImagePaths')
    && typeof message.documentId === 'string'
    && message.documentId.length > 0
    && typeof message.revision === 'number'
    && Number.isSafeInteger(message.revision)
    && message.revision >= 0
    && (message.requestId === undefined || typeof message.requestId === 'string')
}
