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

it('keeps the persisted adaptive justification choice across legacy visual updates', async () => {
  Object.defineProperty(document, 'fonts', {
    configurable: true,
    value: Object.assign(new EventTarget(), { ready: Promise.resolve(), status: 'loaded' }),
  })
  document.body.innerHTML = shell
  vi.stubGlobal('matchMedia', () => ({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }))

  const hostMessages: EditorMessage[] = []
  let receiveFromHost: ((event: MessageEvent<HostMessage>) => void) | undefined
  window.chrome = {
    webview: {
      postMessage(message) {
        hostMessages.push(message)
      },
      addEventListener(_type, listener) {
        receiveFromHost = listener
      },
    },
  }

  await import('../src/main')
  receiveFromHost!(new MessageEvent('message', {
    data: {
      protocolVersion: 1,
      type: 'loadDocument',
      documentId: 'typography-preference-test',
      revision: 0,
      payload: { markdown: '中文English中文English中文English中文English中文English' },
    },
  }))
  await vi.waitFor(() => expect(hostMessages.some(message => message.type === 'documentLoaded')).toBe(true))

  const payload = {
    lineHeight: '1.75',
    fontSize: '16px',
    maxWidth: '820px',
    sourceFontSize: '14px',
    sourceFontFamily: 'monospace',
    cjkLanguage: 'zh-CN',
    visualCjkAutoSpacing: true,
    ignoreMaxWidth: false,
    usePointerAnchor: false,
    anchorX: null,
    anchorY: null,
  }
  const apply = window.__markleafApplyVisualVariables!
  apply({ ...payload, visualScale: 1.25 })
  expect(document.documentElement.style.getPropertyValue('--ml-table-scale')).toBe('1.25')
  apply(payload)
  expect(document.documentElement.style.getPropertyValue('--ml-table-scale')).toBe('1')
  // jsdom cannot complete verified measurements, but the coordinator still
  // marks enabled fallback paragraphs. That is enough to test the preference
  // switch without pretending this is a visual-layout assertion.
  const projected = () => document.querySelectorAll(
    '.markleaf-typography-v2, .markleaf-typography-quality-fallback',
  ).length

  apply({ ...payload, visualAdaptiveJustification: true })
  await new Promise(resolve => requestAnimationFrame(resolve))
  await vi.waitFor(() => expect(projected()).toBeGreaterThan(0), { timeout: 5_000 })

  apply({ ...payload, visualAdaptiveJustification: false })
  await vi.waitFor(() => expect(projected()).toBe(0))

  // Older hosts can still send visual updates without the new preference.
  // They must preserve the current choice instead of silently re-enabling it.
  apply(payload)
  await new Promise(resolve => requestAnimationFrame(resolve))
  await new Promise(resolve => requestAnimationFrame(resolve))
  expect(projected()).toBe(0)

  apply({ ...payload, visualAdaptiveJustification: true })
  await vi.waitFor(() => expect(projected()).toBeGreaterThan(0), { timeout: 5_000 })
})
