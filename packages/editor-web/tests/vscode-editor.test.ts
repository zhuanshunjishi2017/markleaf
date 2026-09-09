import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, getMarkdown, setHostImageResolver, updateEditorMarkdown } from '../src/editor'
import * as editorModule from '../src/editor'
import type { ExtensionMessage, WebviewMessage } from '../src/vscode-protocol'

const editors: ReturnType<typeof createEditor>[] = []
function editor(markdown: string) {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
  editors.push(editor)
  return editor
}
afterEach(() => {
  editors.splice(0).forEach(editor => editor.destroy())
  setHostImageResolver()
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('shared editor in a VS Code text host', () => {
  it('lets the text host own history while retaining native editor history by default', () => {
    const visual = editor('hello')
    expect(visual.extensionManager.extensions.some(extension => extension.name === 'undoRedo')).toBe(false)
    const native = createEditor(document.createElement('div'), 'hello')
    editors.push(native)
    expect(native.extensionManager.extensions.some(extension => extension.name === 'undoRedo')).toBe(true)
  })

  it('applies external updates to the same view without emitting a local document update', () => {
    const visual = editor('# Title\n\nhello world')
    const view = visual.view
    const element = view.dom
    const changed = vi.fn()
    visual.on('update', changed)
    visual.commands.setTextSelection(12)
    updateEditorMarkdown(visual, '# Title\n\nupdated')
    expect(visual.view).toBe(view)
    expect(visual.view.dom).toBe(element)
    expect(changed).not.toHaveBeenCalled()
    expect(getMarkdown(visual)).toContain('updated')
    expect(visual.state.selection.from).toBe(12)
  })

  it('keeps image source paths in Markdown while displaying host resource URLs', () => {
    setHostImageResolver(path => `https://webview.example/${encodeURIComponent(path)}`)
    const visual = editor('![图片](./assets/example%20image.png)')
    expect(visual.view.dom.querySelector('img')?.src).toContain('https://webview.example/')
    expect(getMarkdown(visual)).toContain('./assets/example%20image.png')
    expect(getMarkdown(visual)).not.toContain('webview.example')
  })

  it('retains code, formulas, front matter and footnotes after an external update and a text edit', () => {
    const visual = editor('old')
    updateEditorMarkdown(visual, '---\ntitle: Example\n---\n\n# Title\n\n$x_y + z$\n\n```ts\nconst a = "<b>"\n```\n\nText[^1].\n\n[^1]: A footnote')
    let headingPosition = -1
    visual.state.doc.descendants((node, position) => {
      if (node.type.name === 'heading') headingPosition = position + 1
    })
    expect(headingPosition).toBeGreaterThan(0)
    visual.view.dispatch(visual.state.tr.insertText('New ', headingPosition))
    const markdown = getMarkdown(visual)
    expect(markdown).toContain('title: Example')
    expect(markdown).toContain('# New Title')
    expect(markdown).toContain('$x_y + z$')
    expect(markdown).toContain('const a = "<b>"')
    expect(markdown).toContain('[^1]: A footnote')
  })

  it('can switch to reading mode without modifying the document', () => {
    const visual = editor('# Read me')
    const before = visual.state.doc
    const changed = vi.fn()
    visual.on('update', changed)
    visual.setEditable(false, false)
    expect(visual.isEditable).toBe(false)
    visual.setEditable(true, false)
    expect(visual.state.doc).toBe(before)
    expect(changed).not.toHaveBeenCalled()
  })

  it('runs the webview entry through editing, reading, queued undo, source actions and conflict recovery', async () => {
    document.body.innerHTML = '<div id="toolbar"><button id="mode"></button><button data-action="openSource">源码</button></div><div id="notice"><span id="notice-text"></span><button id="recover"></button></div><main id="editor"></main><span id="sync-status"></span><span id="word-count"></span>'
    const messages: WebviewMessage[] = []
    vi.stubGlobal('acquireVsCodeApi', () => ({ postMessage: (message: WebviewMessage) => messages.push(message), getState: () => undefined, setState: () => {} }))
    vi.spyOn(window, 'scrollTo').mockImplementation(() => {})
    let visual: ReturnType<typeof createEditor> | undefined
    const create = vi.spyOn(editorModule, 'createEditor').mockImplementation((...args) => {
      visual = createEditorOriginal(...args)
      return visual
    })
    await import('../src/vscode')
    const receive = (message: ExtensionMessage) => window.dispatchEvent(new MessageEvent('message', { data: message }))
    receive({ type: 'document', markdown: '# Title\n\nHello\n', version: 1, writable: true })
    expect(messages).toEqual([{ type: 'ready' }])
    const instance = visual!
    instance.commands.insertContent('Edited ')
    expect(messages.at(-1)).toMatchObject({ type: 'edit', baseVersion: 1, sequence: 1 })
    receive({ type: 'accepted', sequence: 1, version: 2 })
    expect(create).toHaveBeenCalledTimes(1)
    expect(document.querySelector('#sync-status')?.textContent).toBe('已同步到 VS Code')

    const mode = document.querySelector<HTMLButtonElement>('#mode')!
    mode.click()
    expect(instance.isEditable).toBe(false)
    mode.click()
    expect(instance.isEditable).toBe(true)
    expect(messages.filter(message => message.type === 'edit')).toHaveLength(1)

    const undo = () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, metaKey: true, cancelable: true }))
    undo(); undo()
    expect(messages.filter(message => message.type === 'action')).toHaveLength(1)
    receive({ type: 'document', markdown: '# Title\n\nHello\n', version: 3, writable: true })
    receive({ type: 'actionFinished' })
    expect(messages.filter(message => message.type === 'action')).toEqual([{ type: 'action', action: 'undo' }, { type: 'action', action: 'undo' }])
    receive({ type: 'actionFinished' })
    document.querySelector<HTMLButtonElement>('[data-action="openSource"]')!.click()
    expect(messages.at(-1)).toEqual({ type: 'action', action: 'openSource' })
    receive({ type: 'actionFinished' })

    instance.commands.insertContent('Local draft ')
    receive({ type: 'document', markdown: 'Source changed', version: 4, writable: true })
    expect(instance.isEditable).toBe(false)
    expect(instance.getText()).toContain('Local draft')
    document.querySelector<HTMLButtonElement>('#recover')!.click()
    expect(messages.at(-1)).toMatchObject({ type: 'recoverDraft', markdown: expect.stringContaining('Local draft') })
    receive({ type: 'recovered', document: { type: 'document', markdown: 'Source changed', version: 4, writable: true } })
    expect(instance.getText()).toBe('Source changed')
    expect(instance.isEditable).toBe(true)
    expect(create).toHaveBeenCalledTimes(1)
    await new Promise(resolve => setTimeout(resolve, 30))
    window.dispatchEvent(new Event('pagehide'))
  })
})

const createEditorOriginal = createEditor
