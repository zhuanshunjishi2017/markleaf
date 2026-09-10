import { afterEach, expect, it, vi } from 'vitest'
import type { EditorMessage, HostMessage } from '../src/protocol'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
})

it.each(['macOS', 'Windows'])('shares export generation and Windows pagination with %s', async platform => {
  document.body.innerHTML = '<div id="editor"></div><div id="source-editor" hidden></div><button id="source-toggle"></button>'
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  const messages: EditorMessage[] = []
  let receive: ((event: MessageEvent<HostMessage>) => void) | undefined
  window.chrome = { webview: { hostPlatform: platform === 'macOS' ? 'macOS' : undefined, postMessage: message => messages.push(message), addEventListener: (_type, listener) => { receive = listener } } }
  const shared = await import('../src/export-html')
  const generation = vi.spyOn(shared, 'generateExportHtml')
  await import('../src/main')
  const send = (type: HostMessage['type'], payload: unknown, requestId: string) => receive!(new MessageEvent('message', { data: {
    protocolVersion: 1, documentId: 'export-parity', revision: 0, type, payload, requestId,
  } }))
  const markdown = '# Heading\n\n中文 Text $x^2$\n\n| A | B |\n|---|---|\n| 1 | 2 |'
  send('loadDocument', { markdown }, 'load')
  send('command', { command: 'exportDocument', text: JSON.stringify({ format: 'pdf', keepTablesTogether: true, keepHeadingsWithNextBlock: true }) }, 'native-export')
  await vi.waitFor(() => expect(messages.some(message => message.requestId === 'native-export' && message.type === 'exportContent')).toBe(true))
  // The native bridge must delegate serialization, so a later merge cannot silently restore a second generator.
  expect(generation).toHaveBeenCalledTimes(1)
  const native = (messages.find(message => message.requestId === 'native-export' && message.type === 'exportContent')!.payload as { html: string }).html
  const { renderExportSnapshot } = await import('../src/vscode-export')
  const { defaultSettings } = await import('../src/vscode-settings')
  const { exportDefaults } = await import('../src/vscode-export-options')
  const vscode = await renderExportSnapshot(markdown, '', { ...exportDefaults, keepTablesTogether: true, keepHeadingsWithNext: true }, defaultSettings, 'en')
  expect(generation).toHaveBeenCalledTimes(2)
  for (const html of [native, vscode.html]) {
    const doc = new DOMParser().parseFromString(html, 'text/html')
    expect(doc.body.classList.contains('markleaf-export-pdf')).toBe(true)
    expect(doc.querySelector('.markleaf-heading-with-next h1')?.textContent).toBe('Heading')
    expect(doc.querySelector('.markleaf-keep-together table, table.markleaf-keep-together')).not.toBeNull()
    expect(doc.querySelector('.katex')).not.toBeNull()
    expect(html).toContain('break-inside: avoid-page !important')
    expect(html).toContain('overflow-y: auto !important')
    expect(html).toContain('scrollbar-width: none !important')
  }
})
