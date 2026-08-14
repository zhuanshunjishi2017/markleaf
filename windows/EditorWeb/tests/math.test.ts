import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getMarkdown } from '../src/editor'
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
})
