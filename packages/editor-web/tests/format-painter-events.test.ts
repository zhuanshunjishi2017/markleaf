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
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
})

async function createHarness(markdown: string): Promise<{
  editorMessages: EditorMessage[]
  editorMount: HTMLElement
  editorDom: HTMLElement
  send: (message: HostMessage) => void
}> {
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
    payload: { markdown },
  })

  return {
    editorMessages,
    editorMount: document.querySelector<HTMLElement>('#editor')!,
    editorDom: document.querySelector<HTMLElement>('.ProseMirror')!,
    send,
  }
}

function armFormatPainter(
  send: (message: HostMessage) => void,
  editorMessages: EditorMessage[],
): void {
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
}

function requestMarkdown(
  send: (message: HostMessage) => void,
  editorMessages: EditorMessage[],
): unknown {
  send({
    protocolVersion: 1,
    type: 'requestSnapshot',
    requestId: 'snapshot',
    documentId: 'format-painter-event-test',
    revision: 0,
  })
  return editorMessages.find((message) => message.requestId === 'snapshot')?.payload
}

it('applies after a backward mouse selection reaches ProseMirror after mouseup', async () => {
  const { editorMessages, editorDom, send } = await createHarness('**source**\n\ntarget line')
  editorDom.focus()
  armFormatPainter(send, editorMessages)

  // WebKit can paint the DOM selection before ProseMirror receives the
  // selectionchange event. The mouseup handler must wait for that state sync.
  selectDomText('target line', true)
  editorDom.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
  document.dispatchEvent(new Event('selectionchange'))
  await new Promise((resolve) => window.setTimeout(resolve, 0))

  expect(requestMarkdown(send, editorMessages))
    .toEqual({ markdown: '**source**\n\n**target line**' })
})

it('applies to a whole paragraph dragged backward without waiting for selectionchange', async () => {
  const { editorMessages, editorDom, send } = await createHarness('**source**\n\ntarget line')
  editorDom.focus()
  armFormatPainter(send, editorMessages)

  const target = editorDom.querySelectorAll('p')[1]!
  window.getSelection()!.setBaseAndExtent(target, target.childNodes.length, target, 0)
  editorDom.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
  await new Promise((resolve) => window.setTimeout(resolve, 0))

  expect(requestMarkdown(send, editorMessages))
    .toEqual({ markdown: '**source**\n\n**target line**' })
})

it('applies to multiple paragraphs dragged backward without waiting for selectionchange', async () => {
  const { editorMessages, editorDom, send } = await createHarness(
    '**source**\n\nfirst target\n\nsecond target',
  )
  editorDom.focus()
  armFormatPainter(send, editorMessages)

  const paragraphs = editorDom.querySelectorAll('p')
  const firstTarget = paragraphs[1]!
  const secondTarget = paragraphs[2]!
  window.getSelection()!.setBaseAndExtent(
    secondTarget,
    secondTarget.childNodes.length,
    firstTarget,
    0,
  )
  editorDom.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
  await new Promise((resolve) => window.setTimeout(resolve, 0))

  expect(requestMarkdown(send, editorMessages)).toEqual({
    markdown: '**source**\n\n**first target**\n\n**second target**',
  })
})

it('applies when a backward whole-line drag ends on the editor padding at line start', async () => {
  const { editorMessages, editorMount, editorDom, send } = await createHarness(
    '**source**\n\ntarget line',
  )
  editorDom.focus()
  armFormatPainter(send, editorMessages)

  const target = editorDom.querySelectorAll('p')[1]!
  window.getSelection()!.setBaseAndExtent(target, target.childNodes.length, target, 0)
  editorMount.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
  await new Promise((resolve) => window.setTimeout(resolve, 0))

  expect(requestMarkdown(send, editorMessages))
    .toEqual({ markdown: '**source**\n\n**target line**' })
})

it('applies when a backward multi-line drag ends on the editor padding at a line start', async () => {
  const { editorMessages, editorMount, editorDom, send } = await createHarness(
    '**source**\n\nfirst target\n\nsecond target',
  )
  editorDom.focus()
  armFormatPainter(send, editorMessages)

  const paragraphs = editorDom.querySelectorAll('p')
  const firstTarget = paragraphs[1]!
  const secondTarget = paragraphs[2]!
  window.getSelection()!.setBaseAndExtent(
    secondTarget,
    secondTarget.childNodes.length,
    firstTarget,
    0,
  )
  editorMount.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
  await new Promise((resolve) => window.setTimeout(resolve, 0))

  expect(requestMarkdown(send, editorMessages)).toEqual({
    markdown: '**source**\n\n**first target**\n\n**second target**',
  })
})
