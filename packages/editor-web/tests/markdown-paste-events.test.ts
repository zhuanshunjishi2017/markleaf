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

if (!globalThis.ClipboardEvent) {
  globalThis.ClipboardEvent = class ClipboardEvent extends Event {} as unknown as typeof ClipboardEvent
}

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
})

async function createHarness(markdown = ''): Promise<{
  messages: EditorMessage[]
  send: (type: HostMessage['type'], payload?: unknown, requestId?: string) => void
  snapshot: () => string
  editorDom: () => HTMLElement
}> {
  document.body.innerHTML = shell
  vi.stubGlobal('matchMedia', () => ({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }))
  const messages: EditorMessage[] = []
  let receiveFromHost: ((event: MessageEvent<HostMessage>) => void) | undefined
  window.chrome = {
    webview: {
      hostPlatform: 'macOS',
      postMessage(message) {
        messages.push(message)
      },
      addEventListener(_type, listener) {
        receiveFromHost = listener
      },
    },
  }
  await import('../src/main')
  expect(receiveFromHost).toBeDefined()
  const send = (type: HostMessage['type'], payload?: unknown, requestId?: string) => {
    receiveFromHost!(new MessageEvent('message', { data: {
      protocolVersion: 1,
      type,
      requestId,
      documentId: 'markdown-paste-event-test',
      revision: 0,
      payload,
    } as HostMessage }))
  }
  send('loadDocument', { markdown })
  const snapshot = () => {
    const requestId = crypto.randomUUID()
    send('requestSnapshot', undefined, requestId)
    return (messages.find((message) => message.requestId === requestId)?.payload as { markdown: string }).markdown
  }
  return {
    messages,
    send,
    snapshot,
    editorDom: () => document.querySelector<HTMLElement>('.ProseMirror')!,
  }
}

function dispatchPaste(target: HTMLElement, plainText: string, html = ''): Event {
  const event = new Event('paste', { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'clipboardData', {
    value: {
      items: [],
      files: [],
      types: html ? ['text/plain', 'text/html'] : ['text/plain'],
      getData(type: string) {
        if (type === 'text/plain' || type === 'text') return plainText
        if (type === 'text/html') return html
        return ''
      },
    },
  })
  target.dispatchEvent(event)
  return event
}

it('handles one browser Markdown paste exactly once', async () => {
  const harness = await createHarness()

  const event = dispatchPaste(harness.editorDom(), '# Browser heading')

  expect(event.defaultPrevented).toBe(true)
  expect(harness.snapshot().trimEnd()).toBe('# Browser heading')
  expect(harness.editorDom().querySelectorAll('h1')).toHaveLength(1)
})

it('keeps a rich browser link on the HTML paste path', async () => {
  const harness = await createHarness()

  const event = dispatchPaste(
    harness.editorDom(),
    'OpenAI',
    '<p><a href="https://openai.com">OpenAI</a></p>',
  )

  expect(event.defaultPrevented).toBe(true)
  expect(harness.editorDom().querySelector('a')?.getAttribute('href')).toBe('https://openai.com')
})

it('keeps the browser paste handler after loading a replacement document', async () => {
  const harness = await createHarness('first')
  harness.send('loadDocument', { markdown: 'replacement' })

  dispatchPaste(harness.editorDom(), '- one\n- two')

  expect(harness.snapshot()).toContain('- one\n- two')
  expect(harness.editorDom().querySelectorAll('ul > li')).toHaveLength(2)
})

it('executes native pasteMarkdown and pasteClipboard commands', async () => {
  const harness = await createHarness()
  harness.send('command', { command: 'pasteMarkdown', text: '# Native heading' }, 'markdown')
  harness.send('command', {
    command: 'pasteClipboard',
    text: 'OpenAI',
    html: '<p><a href="https://openai.com">OpenAI</a></p>',
  }, 'clipboard')

  expect(harness.messages.find((message) => message.requestId === 'markdown')?.payload)
    .toEqual({ success: true, outcome: 'markdown' })
  expect(harness.messages.find((message) => message.requestId === 'clipboard')?.payload)
    .toEqual({ success: true, outcome: 'formatted' })
  expect(harness.editorDom().querySelectorAll('h1')).toHaveLength(1)
  expect(harness.editorDom().querySelector('a')?.getAttribute('href')).toBe('https://openai.com')
})

it('keeps source-mode native clipboard text literal', async () => {
  const harness = await createHarness('before')
  harness.send('command', { command: 'toggleSourceMode' })
  harness.send('command', {
    command: 'pasteClipboard',
    text: '# literal source',
    html: '<h1>literal source</h1>',
  }, 'source-paste')
  harness.send('command', {
    command: 'pasteMarkdown',
    text: '**still literal source**',
  }, 'source-markdown')

  expect(harness.messages.find((message) => message.requestId === 'source-paste')?.payload)
    .toEqual({ success: true, outcome: 'plainText' })
  expect(harness.messages.find((message) => message.requestId === 'source-markdown')?.payload)
    .toEqual({ success: true, outcome: 'plainText' })
  expect(harness.snapshot()).toContain('# literal source')
  expect(harness.snapshot()).toContain('**still literal source**')
  harness.send('loadDocument', { markdown: 'cleanup' })
})

it('keeps native pasteText Markdown punctuation literal in visual mode', async () => {
  const harness = await createHarness()

  harness.send('command', { command: 'pasteText', text: '# plain text' }, 'plain-text')

  expect(harness.messages.find((message) => message.requestId === 'plain-text')?.payload).toEqual({ success: true })
  expect(harness.editorDom().querySelector('h1')).toBeNull()
  expect(harness.editorDom().querySelector('p')?.textContent).toBe('# plain text')
})

it('retains an HTML-only native clipboard fallback', async () => {
  const harness = await createHarness()
  harness.send('command', {
    command: 'pasteClipboard',
    html: '<p><strong>HTML only</strong></p>',
  }, 'html-only')

  expect(harness.messages.find((message) => message.requestId === 'html-only')?.payload)
    .toEqual({ success: true, outcome: 'formatted' })
  expect(harness.editorDom().querySelector('strong')?.textContent).toBe('HTML only')
})
