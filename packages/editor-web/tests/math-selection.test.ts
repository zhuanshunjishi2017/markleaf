import { afterEach, describe, expect, it, vi } from 'vitest'

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
  window.getSelection()?.removeAllRanges()
  Reflect.deleteProperty(document, 'elementFromPoint')
  delete window.chrome
})

async function loadMathDocument(): Promise<HTMLElement> {
  document.body.innerHTML = shell
  vi.stubGlobal('matchMedia', () => ({
    matches: false,
    addEventListener() {},
    removeEventListener() {},
  }))

  let receiveFromHost: ((event: MessageEvent) => void) | undefined
  window.chrome = {
    webview: {
      hostPlatform: 'macOS',
      postMessage() {},
      addEventListener(_type, listener) {
        receiveFromHost = listener
      },
    },
  }

  await import('../src/main')
  receiveFromHost!(new MessageEvent('message', { data: {
    protocolVersion: 1,
    type: 'loadDocument',
    documentId: 'math-selection-test',
    revision: 0,
    payload: { markdown: 'selected text\n\n$$x^2$$' },
  } }))

  const formula = document.querySelector<HTMLElement>('.markleaf-math-block')!
  Object.defineProperty(document, 'elementFromPoint', {
    configurable: true,
    value: () => formula,
  })
  return formula
}

describe('formula selection', () => {
  it('clears a stale native text selection before selecting a formula node', async () => {
    const formula = await loadMathDocument()
    const paragraphText = document.querySelector<HTMLElement>('.ProseMirror p')?.firstChild
    expect(paragraphText).not.toBeNull()

    const nativeSelection = window.getSelection()!
    nativeSelection.setBaseAndExtent(paragraphText!, 0, paragraphText!, paragraphText!.textContent!.length)
    expect(nativeSelection.isCollapsed).toBe(false)

    const mouseDown = new MouseEvent('mousedown', {
      bubbles: true,
      cancelable: true,
      button: 0,
    })
    formula.dispatchEvent(mouseDown)

    expect(mouseDown.defaultPrevented).toBe(true)
    expect(nativeSelection.isCollapsed).toBe(true)

    formula.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
    formula.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }))
    expect(formula.classList.contains('ProseMirror-selectednode')).toBe(true)
  })
})
