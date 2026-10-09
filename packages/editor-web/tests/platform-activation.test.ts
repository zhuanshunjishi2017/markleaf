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
  window.dispatchEvent(new Event('unload'))
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  vi.resetModules()
  document.body.innerHTML = ''
  Reflect.deleteProperty(document, 'elementFromPoint')
  Reflect.deleteProperty(document, 'fonts')
  delete window.chrome
})

async function loadLink(hostPlatform?: 'macOS'): Promise<{
  anchor: HTMLAnchorElement
  messages: EditorMessage[]
}> {
  document.body.innerHTML = shell
  Object.defineProperty(document, 'fonts', { configurable: true, value: { ready: Promise.resolve() } })
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

function clickLink(anchor: HTMLAnchorElement, messages: EditorMessage[], modifier: 'meta' | 'ctrl'): void {
  const before = openLinkCount(messages)
  const options = { bubbles: true, cancelable: true, button: 0, metaKey: modifier === 'meta', ctrlKey: modifier === 'ctrl' }
  anchor.dispatchEvent(new MouseEvent('mousedown', options))
  expect(openLinkCount(messages)).toBe(before)
  anchor.dispatchEvent(new MouseEvent('mouseup', options))
  const click = new MouseEvent('click', options)
  anchor.dispatchEvent(click)
  expect(click.defaultPrevented).toBe(true)
}

describe('primary activation modifier', () => {
  // 链接激活自内核统一重构（3203175）起绑定在 mount 的 click 事件上，
  // 派发必须用 click；此前测试用 mousedown 导致两条平台契约静默失效。
  it('uses Command and leaves Control-click to the context menu on macOS', async () => {
    const { anchor, messages } = await loadLink('macOS')

    clickLink(anchor, messages, 'ctrl')
    expect(openLinkCount(messages)).toBe(0)

    clickLink(anchor, messages, 'meta')
    expect(openLinkCount(messages)).toBe(1)
    expect(messages.find(message => message.type === 'openLink')?.payload).toEqual({ url: 'https://example.com' })
  })

  it('keeps Ctrl-click activation for the Windows/default host', async () => {
    const { anchor, messages } = await loadLink()

    clickLink(anchor, messages, 'meta')
    expect(openLinkCount(messages)).toBe(0)

    clickLink(anchor, messages, 'ctrl')
    expect(openLinkCount(messages)).toBe(1)
    expect(messages.find(message => message.type === 'openLink')?.payload).toEqual({ url: 'https://example.com' })
  })
})
