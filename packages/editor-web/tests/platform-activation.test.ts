import { afterEach, describe, expect, it, vi } from 'vitest'
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
  Reflect.deleteProperty(document, 'elementFromPoint')
  delete window.chrome
})

async function loadLink(hostPlatform?: 'macOS'): Promise<{
  anchor: HTMLAnchorElement
  messages: EditorMessage[]
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
      hostPlatform,
      postMessage(message) {
        messages.push(message)
      },
      addEventListener(_type, listener) {
        receiveFromHost = listener
      },
    },
  }

  await import('../src/main')
  receiveFromHost!(new MessageEvent('message', { data: {
    protocolVersion: 1,
    type: 'loadDocument',
    documentId: 'platform-activation-test',
    revision: 0,
    payload: { markdown: '[site](https://example.com)' },
  } satisfies HostMessage }))

  Object.defineProperty(document, 'elementFromPoint', {
    configurable: true,
    value: () => document.querySelector('.ProseMirror'),
  })

  return {
    anchor: document.querySelector<HTMLAnchorElement>('.ProseMirror a')!,
    messages,
  }
}

function openLinkCount(messages: EditorMessage[]): number {
  return messages.filter(message => message.type === 'openLink').length
}

describe('primary activation modifier', () => {
  it('uses Command and leaves Control-click to the context menu on macOS', async () => {
    const { anchor, messages } = await loadLink('macOS')

    anchor.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true,
      button: 0,
      ctrlKey: true,
    }))
    expect(openLinkCount(messages)).toBe(0)

    anchor.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true,
      button: 0,
      metaKey: true,
    }))
    expect(openLinkCount(messages)).toBe(1)
  })

  it('keeps Ctrl-click activation for the Windows/default host', async () => {
    const { anchor, messages } = await loadLink()

    anchor.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true,
      button: 0,
      metaKey: true,
    }))
    expect(openLinkCount(messages)).toBe(0)

    anchor.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true,
      button: 0,
      ctrlKey: true,
    }))
    expect(openLinkCount(messages)).toBe(1)
  })
})
