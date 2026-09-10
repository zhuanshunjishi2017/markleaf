import * as vscode from 'vscode'
import { extname } from 'node:path'
import { resolveDocumentLink } from './resources'
import type { ExportHtmlResult } from '../../../packages/editor-web/src/vscode-export-options'
import { exportStrings } from '../../../packages/editor-web/src/vscode-export-strings'

const maxImageBytes = 16 * 1024 * 1024
const mimeTypes: Record<string, string> = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.webp': 'image/webp', '.bmp': 'image/bmp', '.avif': 'image/avif' }

export async function embedExportImages(document: vscode.TextDocument, result: ExportHtmlResult, signal: AbortSignal, language: string): Promise<string> {
  const s = exportStrings(language)
  let html = result.html
  for (const [index, source] of result.images.entries()) {
    signal.throwIfAborted()
    try {
      let dataUri: string
      if (/^data:image\/(png|jpeg|gif|webp|svg\+xml|bmp|avif);base64,[A-Za-z0-9+/]*={0,2}$/.test(source)) {
        if (Buffer.from(source.slice(source.indexOf(',') + 1), 'base64').length > maxImageBytes) throw new Error(s.largeImage)
        dataUri = source
      } else {
        const uri = resolveDocumentLink(document, source)
        if (!uri || uri.scheme === 'mailto') throw new Error(s.unsupportedImage)
        let mime = mimeTypes[extname(uri.path).toLowerCase()]
        let bytes: Uint8Array
        if (['http', 'https'].includes(uri.scheme)) {
          const response = await fetch(uri.toString(), { signal: AbortSignal.any([signal, AbortSignal.timeout(30000)]) })
          if (!response.ok) { await response.body?.cancel(); throw new Error(`HTTP ${response.status} ${response.statusText}`) }
          const contentType = response.headers.get('content-type')?.split(';')[0]?.trim()
          if (contentType && Object.values(mimeTypes).includes(contentType)) mime = contentType
          if (!mime) { await response.body?.cancel(); throw new Error(s.unsupportedImage) }
          if (Number(response.headers.get('content-length')) > maxImageBytes) { await response.body?.cancel(); throw new Error(s.largeImage) }
          const reader = response.body?.getReader()
          if (!reader) throw new Error(s.missingImage)
          const chunks: Uint8Array[] = []
          let length = 0
          try {
            for (;;) {
              const chunk = await reader.read()
              if (chunk.done) break
              length += chunk.value.byteLength
              if (length > maxImageBytes) throw new Error(s.largeImage)
              chunks.push(chunk.value)
            }
          } finally { await reader.cancel(); reader.releaseLock() }
          bytes = Buffer.concat(chunks)
        } else {
          if (!mime) throw new Error(s.unsupportedImage)
          const stat = await vscode.workspace.fs.stat(uri)
          if (stat.size > maxImageBytes) throw new Error(s.largeImage)
          bytes = await vscode.workspace.fs.readFile(uri)
        }
        if (bytes.byteLength > maxImageBytes) throw new Error(s.largeImage)
        if (!bytes.byteLength) throw new Error(s.missingImage)
        dataUri = `data:${mime};base64,${Buffer.from(bytes).toString('base64')}`
      }
      html = html.replaceAll(`src="markleaf-export-image:${index}"`, `src="${dataUri}"`)
    } catch (error) {
      signal.throwIfAborted()
      throw new Error(`${s.missingImage}: ${source.startsWith('data:') ? 'data:image' : source}\n${error instanceof Error ? error.message : String(error)}`)
    }
  }
  return html
}
