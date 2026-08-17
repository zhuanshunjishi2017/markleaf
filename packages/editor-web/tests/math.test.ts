import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getEditorCommandState, getMarkdown } from '../src/editor'
import { renderMathInHtml } from '../src/math'

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
  it('round-trips inline and block math markdown', () => {
    const editor = makeEditor('a $x^2$ b\n\n$$y^2$$\n')

    expect(getMarkdown(editor)).toContain('$x^2$')
    expect(getMarkdown(editor)).toContain('$$y^2$$')
  })

  it('inserts inline and block math from latex text', () => {
    const editor = makeEditor('')

    expect(executeEditorCommand(editor, 'insertMathInline', 'a+b')).toBe(true)
    expect(getMarkdown(editor)).toContain('$a+b$')

    expect(executeEditorCommand(editor, 'insertMathBlock', 'c+d')).toBe(true)
    expect(getMarkdown(editor)).toContain('$$c+d$$')
  })

  it('wraps the selection into a math block', () => {
    const editor = makeEditor('x^2')
    editor.commands.selectAll()

    expect(executeEditorCommand(editor, 'insertMathBlock')).toBe(true)
    expect(getMarkdown(editor)).toContain('$$x^2$$')
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
