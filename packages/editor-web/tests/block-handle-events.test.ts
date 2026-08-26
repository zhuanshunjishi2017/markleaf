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

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
})

it('immediately removes the rendered block handle when visibility is disabled', async () => {
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
    documentId: 'block-handle-event-test',
    revision: 0,
    payload: { markdown: 'paragraph' },
  })

  const handle = document.querySelector<HTMLButtonElement>('.ml-block-handle-overlay')!
  expect(handle.hidden).toBe(false)
  expect(handle.textContent).not.toBe('')

  send({
    protocolVersion: 1,
    type: 'command',
    requestId: 'hide-handle',
    documentId: 'block-handle-event-test',
    revision: 0,
    payload: { command: 'setBlockHandleVisible', text: '0' },
  })

  expect(handle.hidden).toBe(true)
  expect(handle.style.display).toBe('none')
  expect(handle.textContent).toBe('')

  handle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
  expect(editorMessages.some((message) => message.type === 'blockMenuRequested')).toBe(false)
})
