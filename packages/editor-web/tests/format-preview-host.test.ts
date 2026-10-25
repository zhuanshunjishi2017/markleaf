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

async function createHarness(hostPlatform?: 'macOS' | 'Windows', markdown = 'source'): Promise<{
  editorDom: HTMLElement
  send: (message: unknown) => void
  messages: unknown[]
}> {
  Object.defineProperty(document, 'fonts', { configurable: true, value: Object.assign(new EventTarget(), { ready: Promise.resolve(), status: 'loaded' }) })
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
    payload: { markdown, visualSelection: { from: 1, to: 7 } },
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

it('keeps an object selection usable by native context-menu commands', async () => {
  const { editorDom, send, messages } = await createHarness('Windows', 'value $$x$$')
  const math = editorDom.querySelector('.markleaf-math')!
  Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => math })
  math.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 0, clientY: 0 }))
  math.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, button: 0, clientX: 0, clientY: 0 }))
  math.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, clientX: 0, clientY: 0 }))
  math.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2, clientX: 0, clientY: 0 }))
  send({
    protocolVersion: 1,
    type: 'command',
    documentId: 'format-preview-host-test',
    revision: 0,
    requestId: 'rotate-image',
    payload: { command: 'convertMath' },
  })

  const result = messages.find((message: any) => message.type === 'commandResult' && message.requestId === 'rotate-image') as
    | { payload?: { success?: boolean } }
    | undefined
  expect(result?.payload?.success).toBe(true)
  expect(editorDom.querySelector('.markleaf-math-block')).toBeNull()
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

it.each([false, true])('does not restore the dismissed menu selection after an outside click (preview=%s)', async (preview) => {
  const { editorDom, send, messages } = await createHarness('Windows')
  const paragraph = editorDom.querySelector('p')!
  const text = document.createTreeWalker(paragraph, NodeFilter.SHOW_TEXT).nextNode()!
  const range = document.createRange()
  range.setStart(text, 2)
  range.collapse(true)
  Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => paragraph })
  Object.defineProperty(document, 'caretRangeFromPoint', { configurable: true, value: () => range })
  paragraph.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }))
  const request = messages.find((x: any) => x.type === 'contextMenuRequested') as { payload: { menuSessionId: number } }
  const base = { protocolVersion: 1, type: 'command', documentId: 'format-preview-host-test', revision: 0 }
  if (preview) {
    send({ ...base, payload: { command: 'previewFormat', text: 'toggleBold', menuSessionId: request.payload.menuSessionId } })
    expect(editorDom.querySelector('.markleaf-format-preview')).not.toBeNull()
  }

  // The editor receives the click while the native menu is still fading out.
  paragraph.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, buttons: 1 }))
  paragraph.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  await new Promise(resolve => window.setTimeout(resolve, 0))
  const selections = (): { from: number; to: number }[] => messages
    .filter((x: any) => x.type === 'selectionChanged').map((x: any) => x.payload)
  expect(selections().at(-1)).toMatchObject({ from: 3, to: 3 })
  expect(editorDom.dataset.markleafPreserveSelection).toBeUndefined()

  // Delayed hover cleanup/close must not resurrect the original range.
  send({ ...base, payload: { command: 'previewFormat', text: 'toggleItalic', menuSessionId: request.payload.menuSessionId } })
  send({ ...base, payload: { command: 'hideFormatMenu', menuSessionId: request.payload.menuSessionId } })
  expect(selections().at(-1)).toMatchObject({ from: 3, to: 3 })
  expect(editorDom.querySelector('.markleaf-themed-selection, .markleaf-format-preview')).toBeNull()
})

it('keeps the original selection when closing the menu without a new editor gesture', async () => {
  const { editorDom, send, messages } = await createHarness('Windows')
  const paragraph = editorDom.querySelector('p')!
  Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: () => paragraph })
  paragraph.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }))
  const request = messages.find((x: any) => x.type === 'contextMenuRequested') as { payload: { menuSessionId: number } }
  const base = { protocolVersion: 1, type: 'command', documentId: 'format-preview-host-test', revision: 0 }
  send({ ...base, payload: { command: 'previewFormat', text: 'toggleItalic', menuSessionId: request.payload.menuSessionId } })
  send({ ...base, payload: { command: 'hideFormatMenu', menuSessionId: request.payload.menuSessionId } })
  expect(editorDom.querySelector('.markleaf-format-preview')).toBeNull()
  expect(editorDom.querySelector('.markleaf-themed-selection')?.textContent).toBe('source')
  expect(editorDom.dataset.markleafPreserveSelection).toBeUndefined()
})
