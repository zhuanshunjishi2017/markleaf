import type { MermaidImageExport, MermaidImageFormat } from '@markleaf/editor-core'
import type { EditorMessage, HostMessage } from './protocol'

export async function handleMermaidImageExport(request: HostMessage,
  generate: (format: MermaidImageFormat) => Promise<MermaidImageExport>, post: (message: EditorMessage) => void): Promise<void> {
  const envelope = { protocolVersion: request.protocolVersion, type: 'mermaidImageExport' as const,
    requestId: request.requestId, documentId: request.documentId, revision: request.revision }
  try {
    const format = (request.payload as { text?: unknown })?.text
    if (format !== 'png' && format !== 'svg') throw new Error('Unsupported image format')
    const payload = await generate(format)
    const result = { ...envelope, payload }
    if (new TextEncoder().encode(JSON.stringify(result)).length > 16 * 1024 * 1024) throw new Error('Diagram exceeds the 16 MiB image message limit; use SVG')
    post(result)
  } catch (error) {
    post({ ...envelope, payload: { error: error instanceof Error ? error.message : 'Image export failed' } })
  }
}
