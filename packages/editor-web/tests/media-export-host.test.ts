import { expect, it, vi } from 'vitest'
import { handleMermaidImageExport } from '../src/media-export-host'

const request = { protocolVersion: 1, type: 'command' as const, requestId: 'one', documentId: 'doc', revision: 3, payload: { command: 'exportMermaidImage', text: 'svg' } }
it('echoes the original identity and request, even if the current document changes during generation', async () => {
  const post = vi.fn()
  const result = { mediaType: 'image/svg+xml' as const, fileExtension: 'svg' as const, base64: 'PHN2Zy8+', width: 320, height: 180 }
  await handleMermaidImageExport(request, async () => result, post)
  expect(post).toHaveBeenCalledWith({ ...request, type: 'mermaidImageExport', payload: result })
})
it('returns explicit errors for unknown formats, failed renders and oversize payloads', async () => {
  for (const text of ['invalid', 'png', 'svg']) {
    const post = vi.fn()
    await handleMermaidImageExport({ ...request, payload: { command: 'exportMermaidImage', text } }, async () => {
      if (text === 'png') throw new Error('not rendered')
      return { mediaType: 'image/svg+xml', fileExtension: 'svg', base64: 'A'.repeat(16 * 1024 * 1024), width: 1, height: 1 }
    }, post)
    expect(post.mock.calls[0]![0].payload.error).toEqual(expect.any(String))
    expect(post.mock.calls[0]![0].requestId).toBe('one')
  }
})
