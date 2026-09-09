import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, executeEditorCommand, expandSourceEditor, exportEditorSelection, findInEditor, getMarkdown, getFootnoteLabels, replaceAllInEditor,
  replaceCurrentInEditor, setBlockHandleVisible, setMarkdownEditingSettings } from '../src/editor'
import { createEditorInteractions } from '../src/vscode-interactions'
import { createFindBar } from '../src/vscode-find'
import { createReadingView } from '../src/vscode-reading'
import { defaultSettings } from '../src/vscode-settings'

const cleanup: Array<() => void> = []
function setup(markdown: string) {
  document.body.innerHTML = '<div id="toolbar"><button data-command="formatPainter"></button></div><main id="editor"></main><footer><span id="count"></span></footer>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
  editor.view.setProps({ handleScrollToSelection: () => true })
  cleanup.push(() => editor.destroy())
  return { editor, mount }
}
afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  setMarkdownEditingSettings({})
  document.body.innerHTML = ''
  vi.restoreAllMocks()
})

describe('completed rendered editing features', () => {
  it('applies format painter once from an actual DOM selection and cancels during composition', async () => {
    const { editor, mount } = setup('**source**\n\ntarget')
    const interactions = createEditorInteractions(editor, mount, vi.fn())
    cleanup.push(interactions.dispose)
    editor.commands.setTextSelection({ from: 1, to: 7 })
    expect(interactions.togglePainter()).toBe(true)
    const target = editor.view.dom.querySelectorAll('p')[1]!.firstChild!
    window.getSelection()!.setBaseAndExtent(target, 6, target, 0)
    mount.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(getMarkdown(editor)).toBe('**source**\n\n**target**')
    expect(interactions.state().formatPainterArmed).toBe(false)
    expect(interactions.togglePainter()).toBe(true)
    mount.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))
    expect(interactions.state().formatPainterArmed).toBe(false)
    mount.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }))
  })

  it('keeps block menus outside editable content and disables them in reading mode', () => {
    const { editor, mount } = setup('paragraph')
    const menu = vi.fn()
    const interactions = createEditorInteractions(editor, mount, menu)
    cleanup.push(interactions.dispose)
    const handle = mount.querySelector<HTMLButtonElement>('.ml-block-handle')!
    expect(handle.hidden).toBe(false)
    expect(editor.view.dom.contains(handle)).toBe(false)
    handle.dispatchEvent(new MouseEvent('mousedown'))
    expect(menu).toHaveBeenCalledTimes(1)
    editor.setEditable(false, false)
    interactions.update()
    expect(handle.hidden).toBe(true)
    handle.dispatchEvent(new MouseEvent('mousedown'))
    expect(menu).toHaveBeenCalledTimes(1)
    editor.setEditable(true, false)
    setBlockHandleVisible(editor, false)
    interactions.update()
    expect(handle.hidden).toBe(true)
    setBlockHandleVisible(editor, true)
  })

  it('changes table dimensions, alignment and caption through shared commands', () => {
    const { editor } = setup('')
    expect(executeEditorCommand(editor, 'insertTable', '4,5')).toBe(true)
    expect(editor.state.doc.firstChild?.childCount).toBe(4)
    expect(editor.state.doc.firstChild?.firstChild?.childCount).toBe(5)
    expect(executeEditorCommand(editor, 'alignTableCenter')).toBe(true)
    expect(executeEditorCommand(editor, 'setTableCaption', '表格标题')).toBe(true)
    const markdown = getMarkdown(editor)
    expect(markdown).toContain('表格标题')
    expect(markdown).toMatch(/:-+:|: -+ :/)
    expect(executeEditorCommand(editor, 'addRowAfter')).toBe(true)
    expect(editor.state.doc.firstChild?.childCount).toBe(5)
    expect(executeEditorCommand(editor, 'deleteColumn')).toBe(true)
    expect(editor.state.doc.firstChild?.firstChild?.childCount).toBe(4)
  })

  it('collects unreferenced footnotes and edits their labels and definitions', () => {
    const { editor } = setup('text\n\n[^1]: unreferenced')
    expect(getFootnoteLabels(editor)).toEqual(['1'])
    editor.commands.setTextSelection(1)
    expect(executeEditorCommand(editor, 'insertFootnote', JSON.stringify({ label: '2', note: 'new note' }))).toBe(true)
    expect(getFootnoteLabels(editor).sort()).toEqual(['1', '2'])
    expect(executeEditorCommand(editor, 'resetFootnoteLabel', JSON.stringify({ oldLabel: '2', newLabel: 'leaf' }))).toBe(true)
    expect(getMarkdown(editor)).toContain('[^leaf]: new note')
    expect(getMarkdown(editor)).not.toContain('[^2]')
    expect(executeEditorCommand(editor, 'showFrontMatter')).toBe(true)
    expect(editor.state.doc.firstChild?.type.name).toBe('frontMatter')
  })

  it('preserves image source, caption, rotation and size in serialized Markdown', () => {
    const { editor } = setup('')
    editor.commands.setImage({ src: './assets/picture.png', alt: 'Picture' })
    editor.commands.setNodeSelection(0)
    editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs, width: 400, height: 200 }))
    expect(executeEditorCommand(editor, 'resizeImage', '50')).toBe(true)
    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(true)
    expect(executeEditorCommand(editor, 'setImageCaption', '图片标题')).toBe(true)
    const markdown = getMarkdown(editor)
    expect(markdown).toContain('./assets/picture.png')
    const reopened = createEditor(document.createElement('div'), markdown)
    cleanup.push(() => reopened.destroy())
    const attrs = reopened.state.doc.firstChild!.attrs
    expect(attrs).toMatchObject({ rotation: 90, widthPercent: 50, aspectRatio: 2, caption: '图片标题' })
  })

  it('finds text after inline atoms and across marks, then replaces literal text', () => {
    const { editor } = setup('before $x$ **tar**get after\n\ntarget')
    expect(findInEditor(editor, 'target', false, false)).toEqual({ current: 1, total: 2 })
    replaceCurrentInEditor(editor, 'target', '<b>literal</b>', false, false)
    expect(editor.state.doc.firstChild?.textContent).toBe('before x <b>literal</b> after')
    expect(editor.view.dom.querySelector('.markleaf-math-inline')).not.toBeNull()
    expect(editor.getHTML()).toContain('&lt;b&gt;literal&lt;/b&gt;')
    expect(replaceAllInEditor(editor, 'target', '$x$', false, false)).toBe(1)
    expect(editor.state.doc.lastChild?.textContent).toBe('$x$')
  })

  it('runs find/replace UI without writing on find and prevents replacement while reading', () => {
    const { editor } = setup('Hello hello shelloworld')
    const bar = createFindBar(editor, document.querySelector('#toolbar')!)
    cleanup.push(bar.dispose)
    const changed = vi.fn()
    editor.on('update', changed)
    bar.open(true)
    const query = document.querySelector<HTMLInputElement>('#find-input')!
    query.value = 'hello'
    query.dispatchEvent(new InputEvent('input', { bubbles: true }))
    expect(document.querySelector('#find-result')?.textContent).toBe('1 / 3')
    const whole = document.querySelector<HTMLInputElement>('#find-whole')!
    whole.checked = true; whole.dispatchEvent(new Event('input'))
    expect(document.querySelector('#find-result')?.textContent).toBe('1 / 2')
    expect(changed).not.toHaveBeenCalled()
    const replacement = document.querySelector<HTMLInputElement>('#replace-input')!
    replacement.value = 'leaf'
    editor.setEditable(false, false); bar.update()
    expect(document.querySelector<HTMLButtonElement>('#replace-all')?.disabled).toBe(true)
    document.querySelector<HTMLButtonElement>('#replace-all')!.click()
    expect(editor.state.doc.textContent).toBe('Hello hello shelloworld')
    editor.setEditable(true, false); bar.update()
    document.querySelector<HTMLButtonElement>('#replace-all')!.click()
    expect(editor.state.doc.textContent).toBe('leaf leaf shelloworld')
    expect(changed).toHaveBeenCalledTimes(1)
  })

  it('applies typography, outline, spacing and focus without changing the document', () => {
    const { editor, mount } = setup('# Same\n\ntext\n\n###### Same')
    const reading = createReadingView(editor, mount, document.querySelector('#count')!)
    cleanup.push(reading.dispose)
    const original = editor.state.doc
    const changed = vi.fn()
    editor.on('update', changed)
    reading.apply({ ...defaultSettings, showOutline: true, typography: 'serif', colorTheme: 'apple-dark', focusMode: true, fontSize: 20, zoom: 125, bulletMarker: 'plus' })
    expect(editor.state.doc).toBe(original)
    expect(changed).not.toHaveBeenCalled()
    expect(mount.classList.contains('markleaf-style-serif')).toBe(true)
    expect(document.documentElement.style.getPropertyValue('--ml-font-size')).toBe('25px')
    const headings = Array.from(document.querySelectorAll<HTMLButtonElement>('#outline button'))
    expect(headings.map(heading => heading.textContent)).toEqual(['Same', 'Same'])
    expect(headings[0]?.dataset.position).not.toBe(headings[1]?.dataset.position)
    expect(mount.classList.contains('markleaf-editor-focus-mode')).toBe(true)
    editor.setEditable(false, false); reading.update()
    expect(mount.classList.contains('markleaf-editor-focus-mode')).toBe(false)
    expect(changed).not.toHaveBeenCalled()
    setBlockHandleVisible(editor, true)
  })

  it('copies a selection as Markdown, plain text and HTML without changing it', () => {
    const { editor } = setup('**hello** world')
    editor.commands.setTextSelection({ from: 1, to: 6 })
    const before = editor.state.doc
    expect(exportEditorSelection(editor)).toEqual({ text: 'hello', markdown: '**hello**', html: '<strong>hello</strong>' })
    expect(editor.state.doc).toBe(before)
  })

  it('retains the original print layout in derived typography styles', () => {
    const { editor, mount } = setup('# Title\n\ntext')
    const reading = createReadingView(editor, mount, document.querySelector('#count')!)
    cleanup.push(reading.dispose)
    const original = editor.state.doc
    for (const typography of ['print-double', 'latex', 'retro-print'] as const) {
      reading.apply({ ...defaultSettings, typography })
      expect(getComputedStyle(mount).paddingTop).toBe('44px')
      expect(getComputedStyle(editor.view.dom.querySelector('h1')!).textAlign).toBe('center')
    }
    reading.apply(defaultSettings)
    expect(mount.classList.contains('markleaf-style-print')).toBe(false)
    expect(editor.state.doc).toBe(original)
  })

  it('keeps formula text inside code fences and blocks stale floating-source input in read mode', () => {
    const { editor } = setup('```text\n- item\n$$x$$\n```\n\n$$y$$')
    expect(editor.state.doc.firstChild?.textContent).toBe('- item\n$$x$$')
    const position = editor.state.doc.firstChild!.nodeSize
    expect(expandSourceEditor(editor, position, 'mathBlock')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
    source.dispatchEvent(menu)
    expect(menu.defaultPrevented).toBe(false)
    editor.setEditable(false, false)
    const before = editor.state.doc
    source.textContent = 'changed'
    source.dispatchEvent(new Event('input'))
    expect(editor.state.doc).toBe(before)
  })
})
