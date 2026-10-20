import { afterEach, expect, it, vi } from 'vitest'

afterEach(() => {
  window.dispatchEvent(new Event('unload'))
  vi.resetModules()
  document.body.innerHTML = ''
  delete window.chrome
})

it('removes the host startup background after theme styles are applied', async () => {
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  document.body.innerHTML = '<div id="app"><div id="editor"></div><div id="source-editor" hidden></div><button id="source-toggle" hidden></button></div>'
  let receiveFromHost: ((event: MessageEvent) => void) | undefined
  window.chrome = {
    webview: {
      hostPlatform: 'Windows',
      postMessage() {},
      addEventListener(_type: string, listener: (event: MessageEvent) => void) {
        receiveFromHost = listener
      },
    },
  }
  const startupStyle = document.createElement('style')
  startupStyle.id = 'markleaf-startup-background'
  startupStyle.textContent = 'html,body,#app{background:#1c1c1e !important}'
  document.head.append(startupStyle)
  ;(window as any).__markleafClearStartupBackground = () => startupStyle.remove()

  await import('../src/main')
  receiveFromHost!(new MessageEvent('message', {
    data: {
      protocolVersion: 1,
      type: 'applyStyles',
      documentId: 'startup-background-test',
      revision: 0,
      payload: { baseCss: '', colorThemeCss: ':root{--bg-primary:#1c1c1e}', styles: [], activeStyle: 'serif' },
    },
  }))
  await Promise.resolve()

  expect(document.getElementById('markleaf-startup-background')).toBeNull()
})
