import { afterEach, expect, it, vi } from 'vitest'
import type { EditorMessage, HostMessage } from '../src/protocol'

const shell = `
  <div id="app">
    <div id="editor"></div>
    <div id="source-editor" hidden></div>
    <button id="source-toggle" type="button" hidden></button>
  </div>
`

afterEach(() => {
  window.dispatchEvent(new Event('unload'))
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
  Reflect.deleteProperty(document, 'fonts')
})

it('disables globally but clears only the explicitly targeted current document', async () => {
  Object.defineProperty(document, 'fonts', { configurable: true, value: Object.assign(new EventTarget(), { ready: Promise.resolve(), status: 'loaded' }) })
  document.body.innerHTML = shell
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  const messages: EditorMessage[] = []
  let receive: ((event: MessageEvent<HostMessage>) => void) | undefined
  window.chrome = { webview: { postMessage(message) { messages.push(message) }, addEventListener(_type, listener) { receive = listener } } }
  await import('../src/main')
  const metadata = '<!-- markleaf:table-layout {"version":1,"shape":[2,2],"width":300,"columns":[100,200],"rows":{}} -->'
  const markdown = metadata + '\n| A | B |\n| --- | --- |\n| one | two |'
  const send = (id: string, type: string, payload?: unknown, requestId?: string) => receive!(new MessageEvent('message', { data: { protocolVersion: 1, type, documentId: id, revision: 0, payload, requestId } as HostMessage }))
  const load = async (id: string, extra = {}) => { send(id, 'loadDocument', { markdown, ...extra }); await vi.waitFor(() => expect(messages.some(m => m.type === 'documentLoaded' && m.documentId === id)).toBe(true)) }
  const snapshot = async (id: string) => { const requestId = crypto.randomUUID(); send(id, 'requestSnapshot', undefined, requestId); await vi.waitFor(() => expect(messages.some(m => m.type === 'snapshot' && m.requestId === requestId)).toBe(true)); return (messages.find(m => m.type === 'snapshot' && m.requestId === requestId)!.payload as { markdown: string }).markdown }
  const payload = { lineHeight: '1.75', fontSize: '16px', maxWidth: '820px', sourceFontSize: '14px', sourceFontFamily: 'monospace', cjkLanguage: 'zh-CN', visualCjkAutoSpacing: true, ignoreMaxWidth: false }
  await load('A')
  window.__markleafApplyVisualVariables!({ ...payload, allowTableColumnResize: false })
  expect(document.querySelector('table.markleaf-table-manual')).toBeNull()
  expect(await snapshot('A')).toContain(metadata)
  await load('B')
  expect(document.querySelector('table.markleaf-table-manual')).toBeNull()
  send('A', 'command', { command: 'clearAllTableLayouts', text: 'A' })
  send('B', 'command', { command: 'clearAllTableLayouts', text: 'A' })
  expect(await snapshot('B')).toContain(metadata)
  send('B', 'command', { command: 'clearAllTableLayouts', text: 'B' })
  expect(await snapshot('B')).not.toContain('markleaf:table-layout')
  send('B', 'command', { command: 'undo' })
  expect(await snapshot('B')).toContain(metadata)
  expect(document.querySelector('table.markleaf-table-manual')).toBeNull()
  // Legacy/zoom payloads cannot enable or clear the feature.
  window.__markleafApplyVisualVariables!(payload)
  expect(await snapshot('B')).toContain(metadata)
  expect(document.querySelector('table.markleaf-table-manual')).toBeNull()
  await load('source', { sourceMode: true })
  send('source', 'command', { command: 'clearAllTableLayouts', text: 'source' })
  expect(await snapshot('source')).not.toContain('markleaf:table-layout')
  send('source', 'command', { command: 'undo' })
  expect(await snapshot('source')).toContain(metadata)
  await load('read-only', { readOnly: true })
  send('read-only', 'command', { command: 'clearAllTableLayouts', text: 'read-only' })
  expect(await snapshot('read-only')).toContain(metadata)
})
