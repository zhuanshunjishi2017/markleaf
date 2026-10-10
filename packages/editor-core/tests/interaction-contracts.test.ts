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

  it('keeps an explicit cell selection while the pointer moves', () => {
    const { editor } = setup('| a | b | c |\n| - | - | - |\n| 1 | 2 | 3 |')
    const cellPositions: number[] = []
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'tableCell' && node.type.name !== 'tableHeader') return true
      cellPositions.push(pos)
      return false
    })

    const cellDom = editor.view.dom.querySelector('td,th')!
    const dispatchSpy = vi.spyOn(editor.view, 'dispatch')
    const posAtCoordsSpy = vi.spyOn(editor.view, 'posAtCoords')
      .mockReturnValue({ pos: cellPositions[2]!, inside: -1 })

    cellDom.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1,
      clientX: 10, clientY: 10,
    }))
    editor.view.dispatch(editor.state.tr.setSelection(
      CellSelection.create(editor.state.doc, cellPositions[0]!, cellPositions[2]!),
    ))

    const move = () => cellDom.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1,
      clientX: 10, clientY: 10,
    }))
    move()
    move()
    expect(posAtCoordsSpy.mock.calls.length).toBeGreaterThan(0)
    expect(dispatchSpy).toHaveBeenCalledTimes(3)

    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
    posAtCoordsSpy.mockRestore()
    dispatchSpy.mockRestore()
  })

  it('synthesizes partial character selection while dragging inside one table cell', () => {
    const { editor } = setup('| first cell text | second cell |\n| --- | --- |', true)
    const firstCell = editor.view.dom.querySelector('th,td')!
    vi.spyOn(Object.getPrototypeOf(editor.view), 'posAtCoords')
      .mockReturnValueOnce({ pos: 4, inside: -1 })
      .mockReturnValueOnce({ pos: 9, inside: -1 })

    firstCell.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, detail: 1,
    }))
    firstCell.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1, detail: 1,
    }))

    expect(editor.view.dom.classList.contains('markleaf-cell-selecting')).toBe(false)
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
    expect(editor.state.selection.from).toBe(4)
    expect(editor.state.selection.to).toBe(9)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  })

  it('upgrades a complete same-cell selection to a stable cell selection', () => {
    const { editor } = setup('| first cell text | second cell |\n| --- | --- |', true)
    const firstCell = editor.view.dom.querySelector('th,td')!
    let textRange: { from: number; to: number } | null = null
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'tableHeader' || editor.view.nodeDOM(pos) !== firstCell) return true
      node.descendants((child, childPos) => {
        if (!child.isText) return true
        const from = pos + 1 + childPos
        textRange = { from, to: from + child.nodeSize }
        return false
      })
      return false
    })
    expect(textRange).toEqual({ from: 4, to: 19 })
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: () => firstCell,
    })
    vi.spyOn(Object.getPrototypeOf(editor.view), 'posAtCoords')
      .mockReturnValueOnce({ pos: 4, inside: -1 })
      .mockReturnValueOnce({ pos: 9, inside: -1 })
      .mockReturnValueOnce({ pos: 19, inside: -1 })
      .mockReturnValue({ pos: 4, inside: -1 })

    // Partial character selection stays native while the pointer is inside
    // the same cell.
    firstCell.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1,
    }))
    editor.commands.setTextSelection({ from: 4, to: 9 })
    firstCell.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1,
    }))
    expect(editor.state.selection).toBeInstanceOf(TextSelection)

    // Selecting the complete cell content promotes once and then remains
    // stable even if WebKit sends a late native-selection transaction.
    editor.commands.setTextSelection(textRange!)
    firstCell.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1, detail: 1,
    }))
    expect(editor.state.selection).toBeInstanceOf(CellSelection)
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(true)
    editor.view.dispatch(editor.state.tr.setSelection(
      TextSelection.between(editor.state.doc.resolve(4), editor.state.doc.resolve(9)),
    ))
    expect(editor.state.selection).toBeInstanceOf(CellSelection)

    // A new plain press starts another character-selection session.
    firstCell.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, detail: 1,
    }))
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(false)
    editor.commands.setTextSelection({ from: 4, to: 9 })
    expect(editor.state.selection).toBeInstanceOf(TextSelection)
  })

  it('creates a stable cell selection when a drag enters another cell', () => {
    const { editor } = setup('| first cell text | second cell |\n| --- | --- |', true)
    const [firstCell, secondCell] = Array.from(editor.view.dom.querySelectorAll('td,th'))
    expect(firstCell).toBeDefined()
    expect(secondCell).toBeDefined()
    const firstCellElement = firstCell!
    const secondCellElement = secondCell!
    vi.spyOn(Object.getPrototypeOf(editor.view), 'posAtCoords')
      .mockReturnValueOnce({ pos: 4, inside: -1 })
      .mockReturnValue({ pos: 23, inside: -1 })

    firstCellElement.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1,
    }))
    secondCellElement.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1,
    }))

    expect(editor.state.selection).toBeInstanceOf(CellSelection)
    expect(editor.state.selection.ranges.length).toBe(2)
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(true)
    editor.view.dispatch(editor.state.tr.setSelection(
      TextSelection.between(editor.state.doc.resolve(4), editor.state.doc.resolve(9)),
    ))
    expect(editor.state.selection).toBeInstanceOf(CellSelection)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  })

  it('collapses a locked cell selection when clicking outside the table', () => {
    // 右键菜单关闭后，点击表格之外应与正文一致地取消选择；filterTransaction
    // 不能把这次用户手势的折叠交易一并拦掉。
    const { editor } = setup('| a | b |\n| - | - |\n| 1 | 2 |', true)
    const cellDom = editor.view.dom.querySelector('td,th')!
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: () => cellDom,
    })
    vi.spyOn(Object.getPrototypeOf(editor.view), 'posAtCoords')
      .mockReturnValue({ pos: 4, inside: -1 })

    cellDom.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    }))
    const cellPositions: number[] = []
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'tableCell' && node.type.name !== 'tableHeader') return true
      cellPositions.push(pos)
      return false
    })
    editor.view.dispatch(editor.state.tr.setSelection(
      CellSelection.create(editor.state.doc, cellPositions[0]!, cellPositions[2]!),
    ))
    cellDom.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    }))
    expect(editor.state.selection).toBeInstanceOf(CellSelection)
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(true)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))

    // 模拟 PM 对"点击表格外正文"的处理：先到达锁定插件的 mousedown（无单元格
    // → 解锁），随后 PM 派发的折叠交易应被放行。
    editor.view.dom.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    }))
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(false)
    editor.view.dispatch(editor.state.tr.setSelection(
      TextSelection.between(editor.state.doc.resolve(1), editor.state.doc.resolve(2)),
    ))
    expect(editor.state.selection.empty).toBe(true)
  })

  it('promotes a drag from outside the table to a whole-table selection', () => {
    // 从表格外（如上方标题）拖进表格：整表提升为 CellSelection，替代
    // WebKit 跨表格边界的原生文本选区（闪烁、输入失效、松开回缩的根源）。
    const { editor } = setup('before\n\n| a | b |\n| - | - |\n| 1 | 2 |', true)
    const cellDom = editor.view.dom.querySelector('td,th')!
    const outside = editor.view.dom.querySelector('p')!

    // 表格外按下（插件不接管），拖动进入首个单元格
    outside.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    }))
    cellDom.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1, clientX: 10, clientY: 10,
    }))

    expect(editor.state.selection).toBeInstanceOf(CellSelection)
    const cellCount = editor.state.selection.ranges.length
    expect(cellCount).toBeGreaterThan(1)
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(true)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))

    // 松开后保持整表选中；随后点击表格之外可正常取消
    expect(editor.state.selection).toBeInstanceOf(CellSelection)
    editor.view.dom.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, clientX: 5, clientY: 5,
    }))
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(false)
  })

  it('keeps native character selection inside one cell when themed selection is off', () => {
    // Chromium 等平台没有 ThemedSelection 接管绘制，单格拖选的高亮来源就是
    // 原生 DOM 选区；冻结逻辑必须保持关闭，否则字符选择不可见。
    const { editor } = setup('| first cell text | second cell |\n| --- | --- |', false)
    const [firstCell] = Array.from(editor.view.dom.querySelectorAll('td,th'))
    const firstCellElement = firstCell!
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: () => firstCellElement,
    })
    vi.spyOn(Object.getPrototypeOf(editor.view), 'posAtCoords')
      .mockReturnValueOnce({ pos: 4, inside: -1 })
      .mockReturnValue({ pos: 9, inside: -1 })

    firstCellElement.dispatchEvent(new MouseEvent('mousedown', {
      bubbles: true, button: 0, buttons: 1, detail: 1,
    }))
    firstCellElement.dispatchEvent(new MouseEvent('mousemove', {
      bubbles: true, button: 0, buttons: 1, detail: 1,
    }))

    expect(editor.state.selection).toBeInstanceOf(TextSelection)
    expect(editor.state.selection.from).toBe(4)
    expect(editor.state.selection.to).toBe(9)
    expect(editor.view.dom.classList.contains('markleaf-cell-selection-locked')).toBe(false)
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
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
