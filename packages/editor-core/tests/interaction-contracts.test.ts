import { afterEach, describe, expect, it, vi } from 'vitest'
import { CellSelection, tableEditingKey } from '@tiptap/pm/tables'
import { NodeSelection, TextSelection } from '@tiptap/pm/state'
import { createEditor, executeEditorCommand, expandSourceEditor, exportEditorSelection, findInEditor, getMarkdown, getFootnoteLabels, replaceAllInEditor,
  replaceCurrentInEditor, setBlockHandleVisible, setMarkdownEditingSettings } from '../src/index'
import { createEditorInteractions } from '../src/index'

const cleanup: Array<() => void> = []
function setup(markdown: string, themedVisualSelection = false) {
  document.body.innerHTML = '<div id="toolbar"><button data-command="formatPainter"></button></div><main id="editor"></main><footer><span id="count"></span></footer>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true, themedVisualSelection })
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

describe('shared editing interactions', () => {
  it('ignores a mouseup DOM range outside the editor instead of resolving position -1', () => {
    const { editor } = setup('selected text', true)
    editor.commands.setTextSelection({ from: 1, to: 9 })
    const selection = editor.state.selection.toJSON()
    const outside = document.querySelector('footer span')!
    outside.textContent = 'outside editor'
    window.getSelection()!.setBaseAndExtent(outside.firstChild!, 0, outside.firstChild!, 7)
    vi.useFakeTimers()
    try {
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
      expect(() => vi.runOnlyPendingTimers()).not.toThrow()
      expect(editor.state.selection.toJSON()).toEqual(selection)
    } finally {
      vi.useRealTimers()
    }
  })
  it('inserts and removes the visual indent inside code blocks with Tab', () => {
    const { editor } = setup('```js\nconst value = 1\n```')
    const codeBlock = editor.state.doc.firstChild!
    expect(codeBlock.type.name).toBe('codeBlock')
    editor.commands.setTextSelection({ from: 2, to: 2 })

    const tab = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
    editor.view.dom.dispatchEvent(tab)
    expect(tab.defaultPrevented).toBe(true)
    expect(getMarkdown(editor)).toContain('```js\n  const value = 1')

    const shiftTab = new KeyboardEvent('keydown', {
      key: 'Tab', shiftKey: true, bubbles: true, cancelable: true,
    })
    editor.view.dom.dispatchEvent(shiftTab)
    expect(shiftTab.defaultPrevented).toBe(true)
    expect(getMarkdown(editor)).toContain('```js\nconst value = 1')
  })

  it('controls code block spell checking through editing preferences', () => {
    setMarkdownEditingSettings({ codeBlockSpellcheck: false })
    const { editor } = setup('```js\nconst value = 1\n```')
    const codeBlock = editor.view.dom.querySelector('pre')!
    expect(codeBlock.getAttribute('spellcheck')).toBe('false')

    setMarkdownEditingSettings({ codeBlockSpellcheck: true })
    editor.view.dispatch(editor.state.tr.setMeta('addToHistory', false))
    expect(codeBlock.getAttribute('spellcheck')).toBeNull()

    setMarkdownEditingSettings({ codeBlockSpellcheck: false })
    editor.view.dispatch(editor.state.tr.setMeta('addToHistory', false))
    expect(codeBlock.getAttribute('spellcheck')).toBe('false')
  })

  it('applies format painter once from an actual DOM selection and cancels during composition', async () => {
    const { editor, mount } = setup('**source**\n\ntarget')
    const interactions = createEditorInteractions({ mount, getEditor: () => editor, enabled: () => editor.isEditable, onMenu: vi.fn(), label: 'Block menu' })
    cleanup.push(interactions.dispose)
    interactions.update()
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
    const interactions = createEditorInteractions({ mount, getEditor: () => editor, enabled: () => editor.isEditable, onMenu: menu, label: 'Block menu' })
    cleanup.push(interactions.dispose)
    interactions.update()
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

  it('keeps multi-cell selections intact after mouseup', async () => {
    const { editor } = setup('| a | b | c |\n| - | - | - |\n| 1 | 2 | 3 |')
    const cellPositions: number[] = []
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'tableCell' && node.type.name !== 'tableHeader') return true
      cellPositions.push(pos)
      return false
    })
    expect(cellPositions.length).toBeGreaterThanOrEqual(6)

    const selection = CellSelection.create(
      editor.state.doc,
      cellPositions[0]!,
      cellPositions[2]!,
    )
    editor.view.dispatch(editor.state.tr.setSelection(selection))
    expect(editor.state.selection instanceof CellSelection).toBe(true)
    expect(editor.state.selection.ranges.length).toBe(3)

    // A real drag leaves a WebKit DOM selection behind. Reproduce that state;
    // otherwise the mouseup normalization has nothing to collapse.
    const firstRange = selection.ranges[0]!
    const lastRange = selection.ranges[selection.ranges.length - 1]!
    const tableDom = editor.view.dom.querySelector('table')!
    const domRange = document.createRange()
    domRange.selectNodeContents(tableDom)
    const domSelection = window.getSelection()!
    domSelection.removeAllRanges()
    domSelection.addRange(domRange)

    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }))
    await new Promise(resolve => setTimeout(resolve, 20))

    expect(editor.state.selection instanceof CellSelection).toBe(true)
    expect(editor.state.selection.ranges.length).toBe(3)
    expect(document.querySelectorAll('.selectedCell').length).toBe(3)
  })

  it('collapses a locked cell selection when clicking outside the table', () => {
    // CellSelection 出现 → 插件自动锁定；折叠选区（如点击表格外）→ 自动解锁。
    const { editor } = setup('| a | b |\n| - | - |\n| 1 | 2 |', true)
    const cellPositions: number[] = []
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'tableCell' && node.type.name !== 'tableHeader') return true
      cellPositions.push(pos)
      return false
    })
    editor.view.dispatch(editor.state.tr.setSelection(
      CellSelection.create(editor.state.doc, cellPositions[0]!, cellPositions[2]!),
    ))
    expect(editor.state.selection).toBeInstanceOf(CellSelection)

    // 折叠选区（如点击表格外）：CellSelection 消失 → 自动解锁。
    editor.view.dispatch(editor.state.tr.setSelection(
      TextSelection.between(editor.state.doc.resolve(1), editor.state.doc.resolve(2)),
    ))
    expect(editor.state.selection).not.toBeInstanceOf(CellSelection)
  })

  it('uses one text-selection engine when a drag crosses into a table', () => {
    // 普通拖选无论从正文、留白还是表格单元格开始，都由同一条托管文字
    // 选区管线驱动；进入表格不能隐式切换成 CellSelection。
    const { editor } = setup('before\n\n| a | b |\n| - | - |\n| 1 | 2 |', true)
    const cellDom = editor.view.dom.querySelector('td,th')!
    const outside = editor.view.dom.querySelector('p')!
    const positions = [1, 4]
    vi.spyOn(editor.view, 'posAtCoords')
      .mockReturnValueOnce({ pos: positions[0]!, inside: -1 })
      .mockReturnValue({ pos: positions[1]!, inside: -1 })

    const down = new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    })
    outside.dispatchEvent(down)
    expect(down.defaultPrevented).toBe(true)
    const move = new MouseEvent('mousemove', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    })
    cellDom.dispatchEvent(move)

    expect(move.defaultPrevented).toBe(true)
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
    expect(editor.state.selection.empty).toBe(false)
    expect(editor.state.selection).not.toBeInstanceOf(CellSelection)
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(false)

    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
  })

  it('keeps a drag that starts in a table as a text selection when it leaves the table', () => {
    const { editor, mount } = setup('| left | right |\n| --- | --- |\n| one | two |\n\nafter', true)
    const cellDom = editor.view.dom.querySelector('td,th')!
    const outside = editor.view.dom.querySelectorAll('p')[1]!
    vi.spyOn(editor.view, 'posAtCoords')
      .mockReturnValueOnce({ pos: 3, inside: -1 })
      .mockReturnValue({ pos: editor.state.doc.content.size - 2, inside: -1 })

    const down = new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    })
    cellDom.dispatchEvent(down)
    expect(down.defaultPrevented).toBe(true)
    expect(mount.classList.contains('markleaf-managed-selection-active')).toBe(true)

    const move = new MouseEvent('mousemove', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 20, clientY: 20,
    })
    outside.dispatchEvent(move)
    expect(move.defaultPrevented).toBe(true)
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
    expect(editor.state.selection).not.toBeInstanceOf(CellSelection)

    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
  })

  it('starts the same managed text drag from editor whitespace', () => {
    const { editor, mount } = setup('before\n\n| a | b |\n| - | - |\n| 1 | 2 |', true)
    vi.spyOn(editor.view, 'posAtCoords')
      .mockReturnValueOnce(null)
      .mockReturnValue({ pos: 4, inside: -1 })

    const down = new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 2, clientY: 2,
    })
    mount.dispatchEvent(down)
    expect(down.defaultPrevented).toBe(true)
    expect(mount.classList.contains('markleaf-managed-selection-active')).toBe(true)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  })

  it('does not convert a managed drag ending on a horizontal rule into a node selection', async () => {
    const { editor, mount } = setup('before\n\n---\n\nafter', true)
    const interactions = createEditorInteractions({
      mount,
      getEditor: () => editor,
      enabled: () => editor.isEditable,
      onMenu: vi.fn(),
      label: 'Block menu',
    })
    cleanup.push(interactions.dispose)
    const paragraph = mount.querySelector('p')!
    const horizontalRule = mount.querySelector('hr')!
    let horizontalRulePosition: number | null = null
    editor.state.doc.descendants((node, position) => {
      if (node.type.name === 'horizontalRule') {
        horizontalRulePosition = position
        return false
      }
      return true
    })
    expect(horizontalRulePosition).not.toBeNull()
    vi.spyOn(editor.view, 'posAtCoords')
      .mockReturnValueOnce({ pos: 1, inside: -1 })
      .mockReturnValue({ pos: horizontalRulePosition!, inside: horizontalRulePosition! })

    paragraph.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 1, clientY: 1,
    }))
    horizontalRule.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 1, clientY: 1,
    }))
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
    horizontalRule.dispatchEvent(new MouseEvent('click', {
      bubbles: true, cancelable: true, button: 0, clientX: 1, clientY: 1,
    }))

    expect(editor.state.selection).toBeInstanceOf(TextSelection)
    await new Promise(resolve => setTimeout(resolve, 20))
  })

  it('ignores an invalid -1 coordinate instead of dispatching an invalid document position', () => {
    const { editor, mount } = setup('before\n\n| a | b |\n| - | - |\n| 1 | 2 |', true)
    vi.spyOn(editor.view, 'posAtCoords').mockReturnValue({ pos: -1, inside: -1 })

    const down = new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1, clientX: 2, clientY: 2,
    })
    expect(() => mount.dispatchEvent(down)).not.toThrow()
    expect(down.defaultPrevented).toBe(true)
    expect(editor.state.selection.from).toBeGreaterThanOrEqual(0)
    expect(editor.state.selection.to).toBeLessThanOrEqual(editor.state.doc.content.size)
  })

  it('does not convert a late native selection after an outside drag enters a table', () => {
    const { editor } = setup('before\n\n| a | b |\n| - | - |\n| 1 | 2 |', true)
    const cellDom = editor.view.dom.querySelector('td,th')!
    const outside = editor.view.dom.querySelector('p')!
    vi.spyOn(editor.view, 'posAtCoords')
      .mockReturnValueOnce({ pos: 1, inside: -1 })
      .mockReturnValue({ pos: 4, inside: -1 })
    editor.view.focus()
    expect(editor.view.hasFocus()).toBe(true)

    outside.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1,
    }))
    cellDom.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, cancelable: true, button: 0, buttons: 1,
    }))
    expect(editor.state.selection).not.toBeInstanceOf(CellSelection)

    // A late native range is valid for a text drag; it must not be converted
    // to a cell selection by the table lock.
    const domSelection = window.getSelection()!
    const nativeRange = document.createRange()
    nativeRange.selectNodeContents(cellDom)
    domSelection.removeAllRanges()
    domSelection.addRange(nativeRange)
    document.dispatchEvent(new Event('selectionchange'))

    expect(domSelection.rangeCount).toBe(1)
    expect(editor.state.selection).not.toBeInstanceOf(CellSelection)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  })

  it('uses the managed selection layer during an outside visual drag', async () => {
    const { editor, mount } = setup('before\n\n| left | right |\n| --- | --- |\n| one | two |', true)
    const outside = editor.view.dom.querySelector('p')!
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: () => outside,
    })
    outside.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, cancelable: true, button: 0, buttons: 1,
    }))

    // The editor owns the endpoint before WebKit starts its asynchronous drag
    // pipeline, so the themed CSS Highlight remains the only visual layer.
    expect(mount.classList.contains('markleaf-managed-selection-active')).toBe(true)

    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(mount.classList.contains('markleaf-managed-selection-active')).toBe(false)
    expect(editor.view.hasFocus()).toBe(true)
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

  it('copies a selection as Markdown, plain text and HTML without changing it', () => {
    const { editor } = setup('**hello** world')
    editor.commands.setTextSelection({ from: 1, to: 6 })
    const before = editor.state.doc
    expect(exportEditorSelection(editor)).toEqual({ text: 'hello', markdown: '**hello**', html: '<strong>hello</strong>' })
    expect(editor.state.doc).toBe(before)
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

  it('takes over formula clicks before WebKit can extend the previous text selection', () => {
    const { editor, mount } = setup('previous text\n\n$$x^2$$', true)
    const interactions = createEditorInteractions({
      mount,
      getEditor: () => editor,
      enabled: () => editor.isEditable,
      onMenu: vi.fn(),
      label: 'Block menu',
    })
    cleanup.push(interactions.dispose)
    const formula = mount.querySelector<HTMLElement>('.markleaf-math-block')!
    const originalElementFromPoint = document.elementFromPoint
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: () => formula,
    })
    cleanup.push(() => Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: originalElementFromPoint,
    }))
    editor.commands.setTextSelection({ from: 1, to: 8 })

    const event = new MouseEvent('mousedown', {
      bubbles: true,
      cancelable: true,
      button: 0,
      buttons: 1,
      clientX: 10,
      clientY: 10,
    })
    formula.dispatchEvent(event)

    expect(event.defaultPrevented).toBe(true)
    expect(editor.state.selection).toBeInstanceOf(NodeSelection)
    expect(editor.state.selection.from).toBeGreaterThan(8)
    expect(window.getSelection()?.isCollapsed ?? true).toBe(true)
  })

  it('clears the native formula range after mouseup without collapsing the node selection', async () => {
    const { editor, mount } = setup('previous text\n\n$$x^2$$', true)
    const interactions = createEditorInteractions({
      mount,
      getEditor: () => editor,
      enabled: () => editor.isEditable,
      onMenu: vi.fn(),
      label: 'Block menu',
    })
    cleanup.push(interactions.dispose)
    const formula = mount.querySelector<HTMLElement>('.markleaf-math-block')!
    const originalElementFromPoint = document.elementFromPoint
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: () => formula,
    })
    cleanup.push(() => Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: originalElementFromPoint,
    }))
    editor.commands.setTextSelection({ from: 1, to: 8 })

    const down = new MouseEvent('mousedown', {
      bubbles: true,
      cancelable: true,
      button: 0,
      buttons: 1,
      clientX: 10,
      clientY: 10,
    })
    formula.dispatchEvent(down)
    const nativeSelection = window.getSelection()!
    const nativeRange = document.createRange()
    nativeRange.selectNodeContents(formula)
    nativeSelection.removeAllRanges()
    nativeSelection.addRange(nativeRange)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))

    await new Promise(resolve => setTimeout(resolve, 20))
    expect(editor.state.selection).toBeInstanceOf(NodeSelection)
    expect(window.getSelection()?.isCollapsed ?? true).toBe(true)
  })

  it('routes literal-source paste, selection and deletion through the same commands', async () => {
    const { editor } = setup('$$abcdef$$')
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)
    await new Promise(resolve => requestAnimationFrame(resolve))
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    const text = source.firstChild!
    const walker = document.createTreeWalker(source, NodeFilter.SHOW_TEXT)
    const firstText = walker.nextNode() ?? text
    window.getSelection()!.setBaseAndExtent(firstText, 1, firstText, 3)
    expect(exportEditorSelection(editor).text).toBe('bc')
    expect(executeEditorCommand(editor, 'pasteText', '**x**')).toBe(true)
    expect(editor.state.doc.firstChild?.textContent).toBe('a**x**def')
    expect(executeEditorCommand(editor, 'selectAll')).toBe(true)
    expect(exportEditorSelection(editor).text).toBe('a**x**def')
    editor.setEditable(false, false)
    expect(executeEditorCommand(editor, 'deleteSelection')).toBe(false)
    expect(editor.state.doc.firstChild?.textContent).toBe('a**x**def')
  })

  it('positions formula source inside the viewport and removes it when disposed', () => {
    const { editor } = setup('$$x$$')
    const anchor = editor.view.nodeDOM(0)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      if (this === anchor) return new DOMRect(30, window.innerHeight - 100, 400, 80)
      if (this === editor.view.dom) return new DOMRect(30, 0, 400, 1800)
      if (this.classList.contains('markleaf-expanded-source')) return new DOMRect(0, 0, 400, 220)
      return new DOMRect()
    })
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source')!
    const top = Number.parseFloat(source.style.top)
    expect(top).toBeGreaterThanOrEqual(8)
    expect(top + 220).toBeLessThanOrEqual(window.innerHeight - 8)
    editor.destroy()
    expect(source.isConnected).toBe(false)
  })

})
