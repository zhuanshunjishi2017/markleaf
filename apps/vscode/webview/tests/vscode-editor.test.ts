import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, getMarkdown, setImageResourceResolver, updateEditorMarkdown } from '@markleaf/editor-core'
import * as editorModule from '@markleaf/editor-core'
import { defaultSettings } from '../src/vscode-settings'
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
  setImageResourceResolver()
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
    setImageResourceResolver({ resolve: path => `https://webview.example/${encodeURIComponent(path)}` })
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
    document.body.innerHTML = '<div id="toolbar"><button id="mode"></button><button data-command="toggleUnderline" data-edit>U</button><button data-command="toggleHighlight" data-edit>H</button><button data-action="format" data-edit>格式</button><button data-action="openSource">源码</button></div><div id="notice"><span id="notice-text"></span><button id="recover"></button></div><main id="editor"></main><span id="sync-status"></span><span id="word-count"></span>'
    // jsdom does not implement the ClipboardEvent constructor used by ProseMirror.
    vi.stubGlobal('ClipboardEvent', class extends Event {})
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
    expect(messages).toEqual([{ type: 'ready', mac: /Mac/i.test(navigator.platform) }, { type: 'focus', target: null }])
    const instance = visual!
    expect(create).toHaveBeenCalledWith(expect.any(HTMLElement), expect.any(String), true, expect.objectContaining({ externalHistory: true }))
    // jsdom has no text layout; this test checks editing and transport, not
    // browser scroll geometry after toolbar commands restore focus.
    instance.view.setProps({ handleScrollToSelection: () => true })

    const toolbar = document.querySelector('#toolbar')!
    toolbar.insertAdjacentHTML('beforeend', '<details><summary>编辑</summary><button>菜单操作</button></details><details><summary>视图</summary></details>')
    const [editMenu, viewMenu] = [...toolbar.querySelectorAll('details')]
    editMenu!.open = true
    editMenu!.querySelector('button')!.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(editMenu!.open).toBe(true)
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(editMenu!.open).toBe(false)
    editMenu!.open = true
    viewMenu!.querySelector('summary')!.click()
    await vi.waitFor(() => expect(editMenu!.open).toBe(false))
    expect(viewMenu!.open).toBe(true)
    viewMenu!.querySelector('summary')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    expect(viewMenu!.open).toBe(false)
    editMenu!.open = true
    window.dispatchEvent(new Event('blur'))
    expect(editMenu!.open).toBe(false)
    document.activeElement instanceof HTMLElement && document.activeElement.blur()

    const hasFocus = vi.spyOn(document, 'hasFocus').mockReturnValue(true)
    instance.view.dom.focus()
    expect(messages.at(-1)).toEqual({ type: 'focus', target: 'document' })
    // VS Code must receive these keys, including a former default after a user
    // rebinds it. Only an explicit host command opens the MarkLeaf find bar.
    for (const [key, altKey] of [['f', false], ['h', false], ['f', true], ['V', false]] as const) {
      const keydown = new KeyboardEvent('keydown', {
        key, altKey, shiftKey: key === 'V', ctrlKey: true, metaKey: true, bubbles: true, cancelable: true,
      })
      const forwarded = vi.fn()
      window.addEventListener('keydown', forwarded)
      instance.view.dom.dispatchEvent(keydown)
      window.removeEventListener('keydown', forwarded)
      expect(forwarded).toHaveBeenCalledOnce()
      expect(keydown.defaultPrevented).toBe(false)
      expect(document.querySelector<HTMLFormElement>('#find-bar')?.hidden).toBe(true)
    }
    receive({ type: 'requestAction', action: 'replace' })
    expect(document.querySelector<HTMLFormElement>('#find-bar')?.hidden).toBe(false)
    expect(messages.at(-1)).toEqual({ type: 'focus', target: 'input' })
    document.querySelector<HTMLButtonElement>('#find-close')!.click()
    await vi.waitFor(() => expect(messages.at(-1)).toEqual({ type: 'focus', target: 'document' }))
    hasFocus.mockReturnValue(false)
    window.dispatchEvent(new Event('blur'))
    expect(messages.at(-1)).toEqual({ type: 'focus', target: null })

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

    // Exercise the actual toolbar and host menu response through the same
    // document transport, including the selection lost while a menu is open.
    receive({ type: 'document', markdown: 'hello world\n\nNext paragraph', version: 5, writable: true })
    const acknowledgeLatest = () => {
      const edit = [...messages].reverse().find(message => message.type === 'edit')
      if (edit?.type !== 'edit') throw new Error('Expected a document edit')
      receive({ type: 'accepted', sequence: edit.sequence, version: edit.baseVersion + 1 })
    }
    const underline = document.querySelector<HTMLButtonElement>('[data-command="toggleUnderline"]')!
    const highlight = document.querySelector<HTMLButtonElement>('[data-command="toggleHighlight"]')!
    instance.commands.setTextSelection({ from: 1, to: 6 })
    underline.click()
    expect(instance.getHTML()).toContain('<u>hello</u>')
    expect(underline.getAttribute('aria-pressed')).toBe('true')
    acknowledgeLatest()
    highlight.click()
    expect(instance.isActive('highlight')).toBe(true)
    expect(highlight.getAttribute('aria-pressed')).toBe('true')
    acknowledgeLatest()

    mode.click()
    const readOnlyDocument = instance.state.doc
    const actionCount = messages.filter(message => message.type === 'action').length
    expect(underline.disabled).toBe(true)
    receive({ type: 'requestAction', action: 'format' })
    receive({ type: 'command', command: 'deleteParagraph' })
    expect(instance.state.doc).toBe(readOnlyDocument)
    expect(messages.filter(message => message.type === 'action')).toHaveLength(actionCount)
    mode.click()

    receive({ type: 'requestAction', action: 'format' })
    expect(messages.at(-1)).toMatchObject({ type: 'action', action: 'format', context: { underline: true, highlight: true } })
    instance.commands.setTextSelection({ from: 7, to: 12 })
    receive({ type: 'command', command: 'toggleBold' })
    expect(instance.state.doc.firstChild?.firstChild?.text).toBe('hello')
    expect(instance.state.doc.firstChild?.firstChild?.marks.some(mark => mark.type.name === 'bold')).toBe(true)
    expect(instance.state.selection.from).toBe(1)
    expect(instance.state.selection.to).toBe(6)
    acknowledgeLatest()
    receive({ type: 'actionFinished' })

    document.querySelector<HTMLButtonElement>('[data-action="format"]')!.click()
    receive({ type: 'document', markdown: 'Changed in source', version: 9, writable: true })
    receive({ type: 'command', command: 'deleteParagraph' })
    expect(instance.state.doc.textContent).toBe('Changed in source')
    expect(document.querySelector('#notice-text')?.textContent).toContain('文档已改变')
    receive({ type: 'actionFinished' })
    // Complete settings are display-only, including Markdown marker choices.
    receive({ type: 'document', markdown: '# Heading\n\n- first\n- second', version: 10, writable: true })
    const beforeSettings = instance.state.doc
    const editsBeforeSettings = messages.filter(message => message.type === 'edit').length
    receive({ type: 'settings', settings: { ...defaultSettings, showOutline: true, typography: 'serif', fontSize: 20, bulletMarker: 'plus' } })
    mode.click(); mode.click()
    expect(instance.state.doc).toBe(beforeSettings)
    expect(messages.filter(message => message.type === 'edit')).toHaveLength(editsBeforeSettings)
    expect(document.querySelector('#outline button')?.textContent).toBe('Heading')

    // Windows treats the clipboard's plain-text format as Markdown in visual mode.
    receive({ type: 'document', markdown: 'target', version: 11, writable: true })
    instance.commands.setTextSelection({ from: 1, to: 7 })
    receive({ type: 'requestAction', action: 'pastePlainText' })
    receive({ type: 'command', command: 'pastePlainText', text: '**bold** and `code`' })
    expect(messages.filter(message => message.type === 'error')).toEqual([])
    expect(instance.state.doc.textContent).toBe('bold and code')
    expect(instance.getHTML()).toContain('<strong>bold</strong>')
    expect(instance.getHTML()).toContain('<code>code</code>')
    expect(document.querySelector('#sync-status')?.textContent).toContain('正在同步… · 已粘贴 Markdown')
    acknowledgeLatest(); receive({ type: 'actionFinished' })
    expect(document.querySelector('#sync-status')?.textContent).toBe('已同步到 VS Code · 已粘贴 Markdown')

    receive({ type: 'requestAction', action: 'find' })
    expect(document.querySelector<HTMLFormElement>('#find-bar')?.hidden).toBe(false)
    document.querySelector<HTMLButtonElement>('#find-close')!.click()

    // A pasted image reaches the host as bytes, then uses the saved target
    // selection when the asynchronous filesystem command returns.
    receive({ type: 'document', markdown: 'first\n\nsecond', version: 13, writable: true })
    instance.commands.setTextSelection(1)
    const paste = new Event('paste', { bubbles: true, cancelable: true })
    Object.defineProperty(paste, 'clipboardData', { value: {
      files: [new File(['image bytes'], 'pasted.png', { type: 'image/png' })], getData: () => '',
    } })
    instance.view.dom.dispatchEvent(paste)
    await vi.waitFor(() => expect(messages.at(-1)).toMatchObject({ type: 'action', action: 'importImages', files: [{ name: 'pasted.png', data: btoa('image bytes') }] }))
    instance.commands.setTextSelection(instance.state.doc.content.size - 1)
    receive({ type: 'command', command: 'insertImages', text: JSON.stringify(['./assets/pasted.png']) })
    expect(instance.state.doc.firstChild?.type.name).toBe('image')
    expect(getMarkdown(instance)).toContain('./assets/pasted.png')
    acknowledgeLatest(); receive({ type: 'actionFinished' })

    receive({ type: 'requestAction', action: 'insertImage' })
    receive({ type: 'document', markdown: 'changed externally', version: 15, writable: true })
    receive({ type: 'command', command: 'insertImages', text: JSON.stringify(['./assets/retained.png']) })
    expect(instance.state.doc.textContent).toBe('changed externally')
    expect(document.querySelector('#notice-text')?.textContent).toContain('./assets/retained.png')
    receive({ type: 'actionFinished' })

    // Existing editor shortcuts still apply their formatting, but must not
    // also reach workbench bindings such as Ctrl/Cmd+B (toggle the sidebar).
    const primary = /Mac/.test(navigator.platform) ? { metaKey: true } : { ctrlKey: true }
    instance.commands.setTextSelection({ from: 1, to: 8 })
    const workbench = vi.fn()
    window.addEventListener('keydown', workbench)
    for (const [key, code, keyCode, altKey] of [['b', 'KeyB', 66, false], ['1', 'Digit1', 49, true]] as const) {
      const keydown = new KeyboardEvent('keydown', { key, code, keyCode, altKey, ...primary, bubbles: true, cancelable: true })
      instance.view.dom.dispatchEvent(keydown)
      expect(keydown.defaultPrevented).toBe(true)
      acknowledgeLatest()
    }
    window.removeEventListener('keydown', workbench)
    expect(workbench).not.toHaveBeenCalled()
    expect(instance.isActive('bold')).toBe(true)
    expect(instance.isActive('heading', { level: 1 })).toBe(true)
    expect(instance.state.doc.textContent).toBe('changed externally')
    instance.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: '1', code: 'Digit1', altKey: true, ...primary, bubbles: true, cancelable: true }))
    expect(instance.isActive('paragraph')).toBe(true)
    acknowledgeLatest()

    // The real entry applies settings without touching the document. A new
    // formula key invokes the shared assistant at the current caret directly.
    receive({ type: 'document', markdown: 'formula here', version: 20, writable: true })
    instance.commands.setTextSelection(8)
    const beforeShortcutConfig = instance.state.doc
    receive({ type: 'settings', settings: defaultSettings, shortcuts: { scope: 'user', overrides: { insertMathInline: 'Mod+Alt+M', toggleBold: '' } } })
    expect(instance.state.doc).toBe(beforeShortcutConfig)
    const oldBold = new KeyboardEvent('keydown', { key: 'b', code: 'KeyB', ...primary, bubbles: true, cancelable: true })
    instance.view.dom.dispatchEvent(oldBold)
    expect(instance.state.doc).toBe(beforeShortcutConfig)
    expect(oldBold.defaultPrevented).toBe(true)
    instance.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'm', code: 'KeyM', altKey: true, ...primary, bubbles: true, cancelable: true }))
    expect(instance.state.doc.firstChild?.child(1).type.name).toBe('mathInline')
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()
    expect(document.querySelector('.markleaf-formula-symbol-toolbar')).not.toBeNull()
    acknowledgeLatest()

    // Parameterized actions take the existing queue and preserve the selected
    // range until the host returns its input. They do not open the format menu.
    receive({ type: 'document', markdown: 'table here', version: 22, writable: true })
    instance.commands.setTextSelection(1)
    receive({ type: 'requestFormatCommand', command: 'insertTable' })
    expect(messages.at(-1)).toMatchObject({ type: 'action', action: 'formatCommand', command: 'insertTable', context: { editable: true } })
    receive({ type: 'command', command: 'insertTable', text: '2,3' })
    expect(instance.state.doc.firstChild?.type.name).toBe('table')
    acknowledgeLatest(); receive({ type: 'actionFinished' })

    const pasteText = (text: string, html = '') => {
      const event = new Event('paste', { bubbles: true, cancelable: true })
      Object.defineProperty(event, 'clipboardData', { value: {
        files: [], getData: (type: string) => type === 'text/html' ? html : text,
      } })
      instance.view.dom.dispatchEvent(event)
      expect(event.defaultPrevented).toBe(true)
    }
    receive({ type: 'document', markdown: 'target', version: 30, writable: true })
    instance.commands.selectAll()
    const beforeMarkdownPaste = messages.filter(message => message.type === 'edit').length
    pasteText('# Heading\n\n**bold**')
    expect(instance.getHTML()).toContain('<h1>Heading</h1>')
    expect(instance.getHTML()).toContain('<strong>bold</strong>')
    expect(messages.filter(message => message.type === 'edit')).toHaveLength(beforeMarkdownPaste + 1)
    acknowledgeLatest()
    expect(document.querySelector('#sync-status')?.textContent).toContain('已粘贴 Markdown')

    receive({ type: 'document', markdown: 'target', version: 35, writable: true })
    instance.commands.selectAll()
    const windowsClipboardEditor = editor('target')
    windowsClipboardEditor.commands.selectAll()
    expect(editorModule.pasteClipboardContentWithResult(windowsClipboardEditor, '**literal**', '<p><strong>**literal**</strong></p>'))
      .toEqual({ success: true, outcome: 'formatted' })
    pasteText('**literal**', '<p><strong>**literal**</strong></p>')
    expect(instance.getJSON()).toEqual(windowsClipboardEditor.getJSON())
    acknowledgeLatest()
    expect(document.querySelector('#sync-status')?.textContent).toContain('已粘贴格式化内容')
    instance.commands.selectAll()
    receive({ type: 'requestAction', action: 'copyHtml' })
    windowsClipboardEditor.commands.selectAll()
    expect(messages.at(-1)).toEqual({ type: 'copy', text: editorModule.exportEditorSelection(windowsClipboardEditor).html })

    receive({ type: 'document', markdown: 'target', version: 40, writable: true })
    instance.commands.selectAll()
    pasteText('**`font-family` is important**')
    expect(instance.getHTML()).toContain('<code>font-family</code>')
    acknowledgeLatest()
    expect(document.querySelector('#sync-status')?.textContent).toContain('已粘贴 Markdown，并转换了不兼容的格式')

    receive({ type: 'document', markdown: 'target', version: 45, writable: true })
    instance.commands.selectAll()
    const brokenParser = vi.spyOn(instance.markdown!, 'parse').mockImplementation(() => { throw new Error('Invalid fixture syntax') })
    pasteText('# fallback')
    brokenParser.mockRestore()
    expect(instance.state.doc.textContent).toBe('# fallback')
    acknowledgeLatest()
    expect(document.querySelector('#sync-status')?.textContent).toContain('已作为纯文本粘贴：Invalid fixture syntax')

    receive({ type: 'document', markdown: 'target', version: 50, writable: true })
    const beforeFailedPaste = messages.filter(message => message.type === 'edit').length
    const rejectHTML = vi.spyOn(instance.view, 'pasteHTML').mockReturnValue(false)
    pasteText('rich', '<p><strong>rich</strong></p>')
    rejectHTML.mockRestore()
    expect(instance.state.doc.textContent).toBe('target')
    expect(messages.filter(message => message.type === 'edit')).toHaveLength(beforeFailedPaste)
    expect(document.querySelector('#sync-status')?.textContent).toContain('无法粘贴剪贴板内容')

    receive({ type: 'document', markdown: 'readonly', version: 55, writable: false })
    receive({ type: 'command', command: 'pastePlainText', text: '# blocked' })
    expect(instance.state.doc.textContent).toBe('readonly')
    expect(messages.filter(message => message.type === 'edit')).toHaveLength(beforeFailedPaste)
    expect(document.querySelector('#sync-status')?.textContent).toBe('文件只读')

    await new Promise(resolve => setTimeout(resolve, 30))
    window.dispatchEvent(new Event('pagehide'))
    // 端到端跑完整 webview 入口（编辑、阅读、撤销队列、源码操作、冲突恢复），
    // 单文件执行约 3 秒，并行跑整个套件时会超过 vitest 默认的 5 秒上限。
  }, 20000)
})

const createEditorOriginal = createEditor
