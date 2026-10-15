import { afterEach, expect, it, vi } from 'vitest'

afterEach(() => {
  window.dispatchEvent(new Event('unload'))
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
  Reflect.deleteProperty(document, 'elementFromPoint')
  Reflect.deleteProperty(document, 'caretRangeFromPoint')
})

async function createHarness(hostPlatform?: 'macOS' | 'Windows'): Promise<{
  editorDom: HTMLElement
  send: (message: unknown) => void
  messages: unknown[]
}> {
  Object.defineProperty(document, 'fonts', { configurable: true, value: { ready: Promise.resolve() } })
  document.body.innerHTML = '<div id="app"><div id="editor"></div><div id="source-editor" hidden></div><button id="source-toggle" hidden></button></div>'
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))

  let receiveFromHost: ((event: MessageEvent) => void) | undefined
  const messages: unknown[] = []
  window.chrome = {
    webview: {
      hostPlatform,
      postMessage(message) { messages.push(message) },
      addEventListener(_type, listener) {
        receiveFromHost = listener
      },
    },
  }

  await import('../src/main')
  expect(receiveFromHost).toBeDefined()
  const send = (message: unknown): void => {
    receiveFromHost!(new MessageEvent('message', { data: message }))
  }
  send({
    protocolVersion: 1,
    type: 'loadDocument',
    documentId: 'format-preview-host-test',
    revision: 0,
    payload: { markdown: 'source', visualSelection: { from: 1, to: 7 } },
  })
  return {
    editorDom: document.querySelector<HTMLElement>('.ProseMirror')!,
    send,
    messages,
  }
}

it('bridges native hover preview commands without changing the document', async () => {
  const { editorDom, send } = await createHarness()
  send({
    protocolVersion: 1,
    type: 'command',
    documentId: 'format-preview-host-test',
    revision: 0,
    payload: { command: 'previewFormat', text: 'toggleBold' },
  })

  const preview = editorDom.querySelector('.markleaf-format-preview')
  expect(preview).not.toBeNull()
  expect(preview?.getAttribute('data-format-preview')).toBe('toggleBold')
  expect(editorDom.textContent).toBe('source')
})

it('clears the native hover preview when the host ends the menu session', async () => {
  const { editorDom, send } = await createHarness()
  const base = {
    protocolVersion: 1,
    type: 'command',
    documentId: 'format-preview-host-test',
    revision: 0,
  }
  send({ ...base, payload: { command: 'previewFormat', text: 'toggleBold' } })
  expect(editorDom.querySelector('.markleaf-format-preview')).not.toBeNull()

  send({ ...base, payload: { command: 'endContextMenu' } })
  expect(editorDom.querySelector('.markleaf-format-preview')).toBeNull()
})

it('does not install an independent HTML formatting surface on macOS', async () => {
  await createHarness('macOS')
  expect(document.querySelector('#format-menu')).toBeNull()
})

it('keeps the HTML formatting surface for hosts without a native toolbar', async () => {
  await createHarness()
  expect(document.querySelector('#format-menu')).not.toBeNull()
})

it('uses one native formatting surface on Windows and clears preview on menu close', async () => {
  const { editorDom, send } = await createHarness('Windows')
  expect(document.querySelector('#format-menu')).toBeNull()
  const base = { protocolVersion: 1, type: 'command', documentId: 'format-preview-host-test', revision: 0 }
  send({ ...base, payload: { command: 'previewFormat', text: 'toggleItalic' } })
  expect(editorDom.querySelector('.markleaf-format-preview')?.getAttribute('data-format-preview')).toBe('toggleItalic')
  send({ ...base, payload: { command: 'hideFormatMenu' } })
  expect(editorDom.querySelector('.markleaf-format-preview')).toBeNull()
  expect(editorDom.textContent).toBe('source')
})

it('ignores stale Windows menu cleanup and hover after a new context request', async () => {
  const { editorDom, send, messages } = await createHarness('Windows')
  const paragraph = editorDom.querySelector('p')!
  Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => paragraph })
  const context = (): number => {
    paragraph.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }))
    const requests = messages.filter((x: any) => x.type === 'contextMenuRequested') as { payload: { menuSessionId: number } }[]
    return requests.at(-1)!.payload.menuSessionId
  }
  const first = context()
  const second = context()
  expect(second).toBeGreaterThan(first)
  const base = { protocolVersion: 1, type: 'command', documentId: 'format-preview-host-test', revision: 0 }
  send({ ...base, payload: { command: 'previewFormat', text: 'toggleItalic', menuSessionId: second } })
  expect(editorDom.querySelector('.markleaf-format-preview')).not.toBeNull()
  send({ ...base, payload: { command: 'hideFormatMenu', menuSessionId: first } })
  send({ ...base, payload: { command: 'previewFormat', text: 'toggleBold', menuSessionId: first } })
  expect(editorDom.dataset.markleafPreserveSelection).toBe('1')
  expect(editorDom.querySelector('.markleaf-format-preview')?.getAttribute('data-format-preview')).toBe('toggleItalic')
  send({ ...base, payload: { command: 'hideFormatMenu', menuSessionId: second } })
  expect(editorDom.querySelector('.markleaf-format-preview')).toBeNull()
})

it('preserves the selection for macOS synthetic contextmenu without a preceding DOM mousedown', async () => {
  const { editorDom, send } = await createHarness('macOS')
  const paragraph = editorDom.querySelector('p')!
  const range = document.createRange()
  range.setStart(document.createTreeWalker(paragraph, NodeFilter.SHOW_TEXT).nextNode()!, 2)
  range.collapse(true)
  Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => paragraph })
  Object.defineProperty(document, 'caretRangeFromPoint', { configurable: true, value: () => range })
  paragraph.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }))
  send({
    protocolVersion: 1, type: 'command', documentId: 'format-preview-host-test', revision: 0,
    payload: { command: 'previewFormat', text: 'toggleBold' },
  })
  expect(editorDom.querySelector('.markleaf-format-preview')?.textContent).toBe('source')
})
