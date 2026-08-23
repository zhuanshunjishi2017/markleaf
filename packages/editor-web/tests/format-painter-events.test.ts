import { afterEach, expect, it, vi } from 'vitest'
import type { EditorMessage, HostMessage } from '../src/protocol'

const shell = `
  <div id="app">
    <form id="find-bar" hidden>
      <input id="find-input" />
      <input id="replace-input" hidden />
      <input id="find-case" type="checkbox" />
      <input id="find-whole" type="checkbox" />
      <span id="find-case-text"></span>
      <span id="find-whole-text"></span>
      <span id="find-result"></span>
      <button id="find-previous" type="button"></button>
      <button id="find-next" type="button"></button>
      <button id="replace-one" type="button" hidden></button>
      <button id="replace-all" type="button" hidden></button>
      <button id="find-close" type="button"></button>
    </form>
    <div id="editor"></div>
    <div id="source-editor" hidden></div>
    <button id="source-toggle" type="button" hidden></button>
  </div>
`

function selectDomText(text: string, backwards = false): void {
  const textNode = Array.from(document.querySelectorAll('.ProseMirror *'))
    .flatMap((element) => Array.from(element.childNodes))
    .find((node) => node.nodeType === Node.TEXT_NODE && node.textContent === text)
  expect(textNode).toBeDefined()

  const selection = window.getSelection()!
  selection.removeAllRanges()
  if (backwards) {
    selection.setBaseAndExtent(textNode!, text.length, textNode!, 0)
  } else {
    const range = document.createRange()
    range.setStart(textNode!, 0)
    range.setEnd(textNode!, text.length)
    selection.addRange(range)
  }
}

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
  delete window.chrome
})

it('applies after a backward mouse selection reaches ProseMirror after mouseup', async () => {
  document.body.innerHTML = shell
  vi.stubGlobal('matchMedia', () => ({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }))

  const editorMessages: EditorMessage[] = []
  let receiveFromHost: ((event: MessageEvent<HostMessage>) => void) | undefined
  window.chrome = {
    webview: {
      postMessage(message) {
        editorMessages.push(message)
      },
      addEventListener(_type, listener) {
        receiveFromHost = listener
      },
    },
  }

  await import('../src/main')
  expect(receiveFromHost).toBeDefined()

  const send = (message: HostMessage): void => {
    receiveFromHost!(new MessageEvent('message', { data: message }))
  }
  send({
    protocolVersion: 1,
    type: 'loadDocument',
    documentId: 'format-painter-event-test',
    revision: 0,
    payload: { markdown: '**source**\n\ntarget line' },
  })

  const editorDom = document.querySelector<HTMLElement>('.ProseMirror')!
  editorDom.focus()
  selectDomText('source')
  document.dispatchEvent(new Event('selectionchange'))
  send({
    protocolVersion: 1,
    type: 'command',
    requestId: 'arm',
    documentId: 'format-painter-event-test',
    revision: 0,
    payload: { command: 'formatPainter' },
  })
  expect(editorMessages.find((message) => message.requestId === 'arm')?.payload)
    .toEqual({ success: true })

  // WebKit can paint the DOM selection before ProseMirror receives the
  // selectionchange event. The mouseup handler must wait for that state sync.
  selectDomText('target line', true)
  editorDom.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
  document.dispatchEvent(new Event('selectionchange'))
  await new Promise((resolve) => window.setTimeout(resolve, 0))

  send({
    protocolVersion: 1,
    type: 'requestSnapshot',
    requestId: 'snapshot',
    documentId: 'format-painter-event-test',
    revision: 0,
  })
  expect(editorMessages.find((message) => message.requestId === 'snapshot')?.payload)
    .toEqual({ markdown: '**source**\n\n**target line**' })
})
