import { afterEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { createEditor, executeEditorCommand, expandSourceEditor, getEditorCommandState, getMarkdown, setMarkdownEditingSettings } from '../src/editor'
import { mathInputPatterns, renderMathInHtml } from '../src/math'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(content = '') {
  const el = document.createElement('div')
  document.body.append(el)
  const editor = createEditor(el, content)
  editors.push(editor)
  return editor
}

function selectMathNode(editor: ReturnType<typeof createEditor>, name: string): void {
  let found = false
  editor.state.doc.descendants((node, pos) => {
    if (!found && node.type.name === name) {
      editor.commands.setNodeSelection(pos)
      found = true
    }
    return !found
  })
}

describe('math formulas', () => {
  it('does not treat the second opening dollar as an inline closing dollar', () => {
    expect(mathInputPatterns.inline.test('$$w$')).toBe(false)
  })

  it('still recognizes normal inline math while typing block math', () => {
    expect(mathInputPatterns.inline.test('$w$')).toBe(true)
    expect(mathInputPatterns.block.test('$$w$$')).toBe(true)
  })

  it('keeps display math after a list as a separate block', () => {
    for (const markdown of ['- item\n$$x^2$$', '1. item\n$$x^2$$']) {
      const editor = makeEditor(markdown)

      expect(editor.getHTML()).toContain('<div data-math-block="1">x^2</div>')
      expect(editor.getJSON().content?.map((node: any) => node.type)).toEqual([
        markdown.startsWith('-') ? 'bulletList' : 'orderedList',
        'mathBlock',
      ])
      expect(getMarkdown(editor)).toContain('$$x^2$$')
    }
  })

  it('round-trips inline and block math markdown', () => {
    const editor = makeEditor('a $x^2$ b\n\n$$y^2$$\n')

    expect(getMarkdown(editor)).toContain('$x^2$')
    expect(getMarkdown(editor)).toContain('$$y^2$$')
  })

  it('does not decode literal symbols inside formula source', () => {
    const editor = makeEditor('a $x < y & z$ b\n\n$$p > q & r$$')

    expect(getMarkdown(editor)).toContain('$x < y & z$')
    expect(getMarkdown(editor)).toContain('$$p > q & r$$')

    setMarkdownEditingSettings({ escapeLiteralSymbols: true })
    expect(getMarkdown(editor)).toContain('$x < y & z$')
    expect(getMarkdown(editor)).toContain('$$p > q & r$$')
  })

  it('does not decode literal symbols inside LaTeX delimiter variants', () => {
    const editor = makeEditor('\\(x < y & z\\)\n\n\\[p > q & r\\]')

    expect(getMarkdown(editor)).toContain('$x < y & z$')
    expect(getMarkdown(editor)).toContain('$$p > q & r$$')
  })

  it('parses LaTeX parenthesis and bracket math delimiters', () => {
    const editor = makeEditor('a \\(\\frac{1}{2} + \\alpha\\) b\n\n\\[y^2 + 1\\]')

    expect(editor.getHTML()).toContain('data-math-inline')
    expect(editor.getHTML()).toContain('data-math-block')
    expect(getMarkdown(editor)).toContain('$\\frac{1}{2} + \\alpha$')
    expect(getMarkdown(editor)).toContain('$$y^2 + 1$$')
    expect(getMarkdown(editor)).not.toContain('\\(')
    expect(getMarkdown(editor)).not.toContain('\\[')
  })

  it('converts typed LaTeX parenthesis and bracket formulas', () => {
    const inline = makeEditor('')
    inline.commands.insertContent('\\(\\frac{1}{2}\\')
    const inlineHandled = inline.view.someProp('handleTextInput', handler => handler(
      inline.view,
      inline.state.selection.from,
      inline.state.selection.to,
      ')',
      () => inline.state.tr.insertText(')'),
    ))

    expect(inlineHandled).toBe(true)
    expect(inline.getHTML()).toContain('data-math-inline')
    expect(getMarkdown(inline)).toContain('$\\frac{1}{2}$')

    const block = makeEditor('')
    block.commands.insertContent('\\[y^2\\')
    const blockHandled = block.view.someProp('handleTextInput', handler => handler(
      block.view,
      block.state.selection.from,
      block.state.selection.to,
      ']',
      () => block.state.tr.insertText(']'),
    ))

    expect(blockHandled).toBe(true)
    expect(block.getHTML()).toContain('data-math-block')
    expect(getMarkdown(block)).toContain('$$y^2$$')
  })

  it('converts typed double-dollar formulas to a block formula', () => {
    const editor = makeEditor('')

    editor.commands.insertContent('$$x^2')
    const firstClosingDollarHandled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      '$',
      () => editor.state.tr.insertText('$'),
    ))

    expect(firstClosingDollarHandled).toBeFalsy()
    expect(editor.getHTML()).not.toContain('data-math-inline')
    editor.commands.insertContent('$')

    const secondClosingDollarHandled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      '$',
      () => editor.state.tr.insertText('$'),
    ))

    expect(secondClosingDollarHandled).toBe(true)
    expect(editor.getHTML()).toContain('data-math-block')
    expect(getMarkdown(editor)).toContain('$$x^2$$')
  })

  it('inserts inline and block math from latex text', () => {
    const editor = makeEditor('')

    expect(executeEditorCommand(editor, 'insertMathInline', 'a+b')).toBe(true)
    expect(getMarkdown(editor)).toContain('$a+b$')
    expect((editor.state.selection as any).node.type.name).toBe('mathInline')
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()

    const block = makeEditor('')
    expect(executeEditorCommand(block, 'insertMathBlock', 'c+d')).toBe(true)
    expect(getMarkdown(block)).toContain('$$c+d$$')
    expect((block.state.selection as any).node.type.name).toBe('mathBlock')
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()
  })

  it('renders empty formulas with a visual placeholder and persists the placeholder syntax', () => {
    const inline = makeEditor('')
    expect(executeEditorCommand(inline, 'insertMathInline')).toBe(true)
    expect(inline.view.dom.querySelector('.markleaf-math-placeholder')?.textContent).toBe('...')
    expect(document.querySelector('.markleaf-expanded-source-editor')?.textContent).toBe('')
    expect(getMarkdown(inline)).toContain('$...$')

    const block = makeEditor('')
    expect(executeEditorCommand(block, 'insertMathBlock')).toBe(true)
    expect(block.view.dom.querySelector('.markleaf-math-placeholder')?.textContent).toBe('...')
    expect(document.querySelector('.markleaf-expanded-source-editor')?.textContent).toBe('')
    expect(getMarkdown(block)).toContain('$$...$$')
  })

  it('reads placeholder syntax as empty formulas and toggles the placeholder while editing', () => {
    const editor = makeEditor('$...$\n\n$$...$$')
    const formulas: Array<{ node: any; position: number }> = []
    editor.state.doc.descendants((node, position) => {
      if (node.type.name === 'mathInline' || node.type.name === 'mathBlock') {
        formulas.push({ node, position })
      }
    })

    expect(formulas).toHaveLength(2)
    expect(formulas.every(({ node }) => node.textContent === '')).toBe(true)
    expect(editor.view.dom.querySelectorAll('.markleaf-math-placeholder')).toHaveLength(2)

    const block = formulas[1]!
    expect(expandSourceEditor(editor, block.position, 'mathBlock')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    source.textContent = 'x+1'
    source.dispatchEvent(new Event('input', { bubbles: true }))
    expect(editor.view.dom.querySelectorAll('.markleaf-math-placeholder')).toHaveLength(1)

    source.textContent = ''
    source.dispatchEvent(new Event('input', { bubbles: true }))
    expect(editor.view.dom.querySelectorAll('.markleaf-math-placeholder')).toHaveLength(2)
    expect(getMarkdown(editor)).toContain('$$...$$')
  })

  it('wraps the selection into a math block', () => {
    const editor = makeEditor('x^2')
    editor.commands.selectAll()

    expect(executeEditorCommand(editor, 'insertMathBlock')).toBe(true)
    expect(getMarkdown(editor)).toContain('$$x^2$$')
    expect((editor.state.selection as any).node.type.name).toBe('mathBlock')
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()
  })

  it('renders empty formulas with a visual placeholder and persists the placeholder syntax', () => {
    const inline = makeEditor('')
    expect(executeEditorCommand(inline, 'insertMathInline')).toBe(true)
    expect(inline.view.dom.querySelector('.markleaf-math-placeholder')?.textContent).toBe('...')
    expect(document.querySelector('.markleaf-expanded-source-editor')?.textContent).toBe('')
    expect(getMarkdown(inline)).toContain('$...$')

    const block = makeEditor('')
    expect(executeEditorCommand(block, 'insertMathBlock')).toBe(true)
    expect(block.view.dom.querySelector('.markleaf-math-placeholder')?.textContent).toBe('...')
    expect(document.querySelector('.markleaf-expanded-source-editor')?.textContent).toBe('')
    expect(getMarkdown(block)).toContain('$$...$$')
  })

  it('reads placeholder syntax as empty formulas and toggles the placeholder while editing', () => {
    const editor = makeEditor('$...$\n\n$$...$$')
    const formulas: Array<{ node: any; position: number }> = []
    editor.state.doc.descendants((node, position) => {
      if (node.type.name === 'mathInline' || node.type.name === 'mathBlock') {
        formulas.push({ node, position })
      }
    })

    expect(formulas).toHaveLength(2)
    expect(formulas.every(({ node }) => node.textContent === '')).toBe(true)
    expect(editor.view.dom.querySelectorAll('.markleaf-math-placeholder')).toHaveLength(2)

    const block = formulas[1]!
    expect(expandSourceEditor(editor, block.position, 'mathBlock')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    source.textContent = 'x+1'
    source.dispatchEvent(new Event('input', { bubbles: true }))
    expect(editor.view.dom.querySelectorAll('.markleaf-math-placeholder')).toHaveLength(1)

    source.textContent = ''
    source.dispatchEvent(new Event('input', { bubbles: true }))
    expect(editor.view.dom.querySelectorAll('.markleaf-math-placeholder')).toHaveLength(2)
    expect(getMarkdown(editor)).toContain('$$...$$')
  })

  it('inserts empty formulas and opens the floating source editor', () => {
    const inline = makeEditor('')
    expect(executeEditorCommand(inline, 'insertMathInline')).toBe(true)
    expect((inline.state.selection as any).node.type.name).toBe('mathInline')
    expect(getMarkdown(inline)).toBe('$...$')
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()

    const block = makeEditor('')
    expect(executeEditorCommand(block, 'insertMathBlock')).toBe(true)
    expect((block.state.selection as any).node.type.name).toBe('mathBlock')
    expect(getMarkdown(block)).toBe('$$...$$\n\n')
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()
  })

  it('converts and deletes a selected math node', () => {
    const editor = makeEditor('$a$')
    selectMathNode(editor, 'mathInline')

    expect(executeEditorCommand(editor, 'convertMath')).toBe(true)
    expect(getMarkdown(editor)).toContain('$$a$$')

    selectMathNode(editor, 'mathBlock')
    expect(executeEditorCommand(editor, 'deleteMath')).toBe(true)
    expect(getMarkdown(editor)).not.toContain('$')
  })

  it('opens or converts the selected formula through the inline and block commands', () => {
    const inline = makeEditor('before $x$ after')
    selectMathNode(inline, 'mathInline')
    expect(executeEditorCommand(inline, 'insertMathInline')).toBe(true)
    expect(document.querySelector('.markleaf-expanded-source-editor')?.textContent).toBe('x')

    expect(executeEditorCommand(inline, 'insertMathBlock')).toBe(true)
    expect((inline.state.selection as any).node.type.name).toBe('mathBlock')
    expect(document.querySelector('.markleaf-expanded-source-editor')?.textContent).toBe('x')
    expect(getMarkdown(inline)).toContain('$$x$$')

    const block = makeEditor('$$y$$')
    selectMathNode(block, 'mathBlock')
    expect(executeEditorCommand(block, 'insertMathBlock')).toBe(true)
    expect(document.querySelectorAll('.markleaf-expanded-source-editor')[1]?.textContent).toBe('y')

    expect(executeEditorCommand(block, 'insertMathInline')).toBe(true)
    expect((block.state.selection as any).node.type.name).toBe('mathInline')
    expect(getMarkdown(block)).toContain('$y$')
  })

  it('uses node selection for a formula', () => {
    const editor = makeEditor('before $x^2$ after')
    selectMathNode(editor, 'mathInline')

    expect(document.querySelector('.markleaf-math.ProseMirror-selectednode')).not.toBeNull()
  })

  it('keeps selected formula content transparent while drawing the theme outline', () => {
    const style = document.createElement('style')
    style.textContent = readFileSync(resolve(import.meta.dirname, '../src/styles.css'), 'utf8')
    document.head.append(style)
    const formula = document.createElement('span')
    formula.className = 'markleaf-math ProseMirror-selectednode'
    formula.style.setProperty('--theme-light', 'rgb(18, 52, 86)')
    formula.style.setProperty('--theme-dark', 'rgb(170, 34, 51)')
    document.body.append(formula)

    const computed = getComputedStyle(formula)
    expect(computed.backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(computed.boxShadow).toContain('var(--theme-light)')
    expect(computed.boxShadow).not.toContain('var(--theme-dark)')
  })

  it('opens the floating source editor from a selected formula', () => {
    const inline = makeEditor('before $x^2$ after')
    selectMathNode(inline, 'mathInline')

    expect(executeEditorCommand(inline, 'editMath')).toBe(true)
    expect(document.querySelector('.markleaf-expanded-source-mathInline')).not.toBeNull()

    const block = makeEditor('$$y^2$$')
    selectMathNode(block, 'mathBlock')
    expect(executeEditorCommand(block, 'editMath')).toBe(true)
    expect(document.querySelector('.markleaf-expanded-source-mathBlock')).not.toBeNull()
  })

  it('animates the floating source editor with a native popover motion', () => {
    const style = document.createElement('style')
    style.textContent = readFileSync(resolve(import.meta.dirname, '../src/styles.css'), 'utf8')
    document.head.append(style)

    const overlay = document.createElement('div')
    overlay.className = 'editor-tooltip markleaf-expanded-source markleaf-expanded-source-mathBlock'
    document.body.append(overlay)

    // jsdom does not resolve shorthand animation properties, so assert the
    // cascade that browsers and reduced-motion users will actually receive.
    expect(style.textContent).toContain('animation: markleaf-expanded-source-motion 180ms')
    expect(style.textContent).toContain('animation: markleaf-expanded-source-motion 180ms cubic-bezier(.22, 1, .36, 1) reverse both')
    expect(style.textContent).toContain('translateY(-12px)')
    expect(style.textContent).toContain('@keyframes markleaf-expanded-source-motion')
  })

  it('expands a block formula into an editable source area without removing its render', async () => {
    const editor = makeEditor('$$x^2$$')
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)

    const rendered = editor.view.dom.querySelector('.markleaf-math-block')
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')
    expect(rendered).not.toBeNull()
    expect(source?.textContent).toBe('x^2')
    expect(editor.view.dom.querySelector('.markleaf-expanded-source')).toBeNull()

    source!.textContent = 'y^2'
    source!.dispatchEvent(new Event('input', { bubbles: true }))
    expect(getMarkdown(editor)).toContain('$$y^2$$')

    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    await new Promise(resolve => setTimeout(resolve, 260))
    expect(document.querySelector('.markleaf-expanded-source')).toBeNull()
    expect(editor.view.dom.querySelector('.markleaf-math-block')).not.toBeNull()
  })

  it('highlights LaTeX source and marks mismatched brackets as invalid', async () => {
    const editor = makeEditor('$$\\frac{1}{2 + \\alpha$$')
    expect(executeEditorCommand(editor, 'setCodeHighlightVisible', '1')).toBe(true)
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)
    await new Promise(resolve => requestAnimationFrame(() => resolve(undefined)))

    const source = document.querySelector('.markleaf-expanded-source-editor')
    expect(source?.querySelector('.ml-code-keyword')?.textContent).toBe('\\frac')
    expect(source?.querySelectorAll('.ml-code-invalid')).toHaveLength(1)
    expect(source?.querySelector('.ml-code-invalid')?.textContent).toBe('{')
  })

  it('isolates inline source editing from the outer ProseMirror selection', () => {
    const editor = makeEditor('$$abcdef$$')
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)

    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    expect(source.parentElement?.tagName).toBe('PRE')
    expect(source.tagName).toBe('CODE')

    const text = source.firstChild!
    const range = document.createRange()
    range.setStart(text, 3)
    range.collapse(true)
    window.getSelection()?.removeAllRanges()
    window.getSelection()?.addRange(range)

    const bubbledClick = vi.fn()
    const bubbledDelete = vi.fn()
    editor.view.dom.addEventListener('click', bubbledClick)
    editor.view.dom.addEventListener('keydown', bubbledDelete)
    source.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0 }))
    source.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Delete' }))

    expect(bubbledClick).not.toHaveBeenCalled()
    expect(bubbledDelete).not.toHaveBeenCalled()
    expect(window.getSelection()?.focusOffset).toBe(3)
  })

  it('keeps the floating source editor open on blur and closes it on outside click', () => {
    vi.useFakeTimers()
    const editor = makeEditor('$$x^2$$')
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!

    source.dispatchEvent(new FocusEvent('blur'))
    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()

    document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    const closing = document.querySelector('.markleaf-expanded-source')
    expect(closing?.classList.contains('markleaf-expanded-source-exit')).toBe(true)
    expect(closing).not.toBeNull()

    vi.advanceTimersByTime(260)
    expect(document.querySelector('.markleaf-expanded-source')).toBeNull()
    vi.useRealTimers()
  })

  it('closes the floating editor with Escape and restores focus after the formula', async () => {
    const editor = makeEditor('a $x$ b')
    selectMathNode(editor, 'mathInline')
    expect(expandSourceEditor(editor, editor.state.selection.from, 'mathInline')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    source.focus()
    // jsdom cannot compute client rects for ProseMirror's scroll-to-selection.
    const originalCoordsAtPos = editor.view.coordsAtPos.bind(editor.view)
    editor.view.coordsAtPos = () => ({ left: 0, top: 0, right: 0, bottom: 0 })

    source.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'Escape', bubbles: true, cancelable: true,
    }))

    // 消失动画会延迟移除浮层；等动画完成后再断言 DOM 清理。
    await new Promise(resolve => setTimeout(resolve, 260))
    expect(document.querySelector('.markleaf-expanded-source')).toBeNull()
    await new Promise(resolve => setTimeout(resolve, 260))
    expect((editor.state.selection as any).from).toBe(6)
    expect(editor.isFocused).toBe(true)
    editor.view.coordsAtPos = originalCoordsAtPos
  })

  it('selects only floating source contents for Command/Control+A', () => {
    const editor = makeEditor('$$abcdef$$')
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    source.focus()

    source.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'a', metaKey: true, bubbles: true, cancelable: true,
    }))

    const selection = window.getSelection()!
    expect(selection.rangeCount).toBe(1)
    expect(selection.toString()).toBe('abcdef')
    expect((editor.state.selection as any).node.type.name).toBe('mathBlock')
  })

  it('does not close the floating editor when clicking highlighted content inside it', async () => {
    const editor = makeEditor('$$\\alpha + 1$$')
    expect(executeEditorCommand(editor, 'setCodeHighlightVisible', '1')).toBe(true)
    expect(expandSourceEditor(editor, 0, 'mathBlock')).toBe(true)
    await new Promise(resolve => requestAnimationFrame(() => resolve(undefined)))

    const highlighted = document.querySelector<HTMLElement>('.markleaf-expanded-source .ml-code-keyword')!
    highlighted.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, composed: true }))

    expect(document.querySelector('.markleaf-expanded-source')).not.toBeNull()
  })

  it('keeps a formula NodeView target associated with its math node', () => {
    const editor = makeEditor('before $x^2$ after\n\n$$y^2$$')
    const mathElements = editor.view.dom.querySelectorAll<HTMLElement>('.markleaf-math')

    expect(mathElements).toHaveLength(2)
    expect(mathElements[0]?.closest('.markleaf-math')).toBe(mathElements[0])
    expect(mathElements[0]?.querySelector('.katex')).not.toBeNull()
    expect(mathElements[1]?.querySelector('.katex')).not.toBeNull()
  })

  it('disables native text selection for a selected formula', () => {
    const style = document.createElement('style')
    style.textContent = readFileSync(resolve(import.meta.dirname, '../src/styles.css'), 'utf8')
    document.head.append(style)
    const formula = document.createElement('div')
    formula.className = 'markleaf-math markleaf-math-block ProseMirror-selectednode'
    const renderedContent = document.createElement('span')
    formula.append(renderedContent)
    document.body.append(formula)

    expect(getComputedStyle(formula).userSelect).toBe('none')
    expect(getComputedStyle(renderedContent).userSelect).toBe('none')
  })

  it('renders math in exported html', () => {
    const html = renderMathInHtml(
      '<span data-math-inline="1">x^2</span><div data-math-block="1">y^2</div>',
    )

    expect(html).toContain('katex')
    expect(html).not.toContain('data-math-inline')
    expect(html).not.toContain('data-math-block')
  })

  it('round-trips a numbered block math equation', () => {
    const editor = makeEditor('$$y^2 \\tag{1}$$')

    expect(getMarkdown(editor)).toContain('\\tag{1}')
  })

  it('sets and clears a block math equation number via the command', () => {
    const editor = makeEditor('$$y^2$$')
    selectMathNode(editor, 'mathBlock')
    expect(getEditorCommandState(editor).mathNumber).toBeNull()

    expect(executeEditorCommand(editor, 'setMathNumber', '1.1')).toBe(true)
    expect(getMarkdown(editor)).toContain('\\tag{1.1}')

    selectMathNode(editor, 'mathBlock')
    expect(getEditorCommandState(editor).mathNumber).toBe('1.1')
    expect(executeEditorCommand(editor, 'setMathNumber', '')).toBe(true)
    expect(getMarkdown(editor)).not.toContain('\\tag')
  })

  it('renders a numbered block math equation in exported html', () => {
    const html = renderMathInHtml('<div data-math-block="1" data-math-number="1">y^2</div>')

    expect(html).toContain('katex')
    expect(html).not.toContain('data-math-number')
    expect(html).toContain('(1)')
  })
})
