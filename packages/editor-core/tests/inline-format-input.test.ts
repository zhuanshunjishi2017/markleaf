import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getMarkdown, pasteMarkdownTextWithResult, setMarkdownEditingSettings } from '../src/index'
import { resetInlineInput } from '../src/renderer-entry'

const editors: ReturnType<typeof createEditor>[] = []
afterEach(() => {
  editors.splice(0).forEach(editor => editor.destroy())
  document.body.innerHTML = ''
  setMarkdownEditingSettings({})
})

function open(markdown = '') {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  return editor
}

function type(editor: ReturnType<typeof open>, text: string) {
  for (const character of text) {
    const { from, to } = editor.state.selection
    const handled = editor.view.someProp('handleTextInput', handler =>
      handler(editor.view, from, to, character, () => editor.state.tr.insertText(character, from, to)))
    if (!handled) editor.view.dispatch(editor.state.tr.insertText(character, from, to))
  }
}

function event(editor: ReturnType<typeof open>, name: 'blur' | 'compositionstart' | 'compositionend' | 'paste') {
  editor.view.someProp('handleDOMEvents', handlers => {
    handlers[name]?.(editor.view, new Event(name) as never)
    return false
  })
}

function expectLiteral(editor: ReturnType<typeof open>, text: string) {
  expect(editor.state.doc.textContent).toBe(text)
  expect(editor.getHTML()).not.toMatch(/<(em|strong|s|mark|code)[ >]/)
  const reopened = open(getMarkdown(editor))
  expect(reopened.state.doc.toJSON()).toEqual(editor.state.doc.toJSON())
}

describe('continuous inline format input', () => {
  it('does not consume the underscore inside existing prose or move the caret there', () => {
    const paragraph = '编制日期：2026-10-17。依据当前仓库的 policy.py、pipeline.py、session_guard.py、Chainlit 配置及已有回归集编写。'
    const editor = open(paragraph)
    editor.commands.setTextSelection(1)
    type(editor, '_')
    expectLiteral(editor, `_${paragraph}`)
    expect(editor.state.selection.from).toBe(2)
  })

  it.each(['*', '**', '***', '_', '__', '___', '~~', '==', '`'])(
    'does not pair newly prepended %s with an existing closing marker', marker => {
      const editor = open(`正文${marker}`)
      editor.commands.setTextSelection(1)
      type(editor, marker)
      expectLiteral(editor, `${marker}正文${marker}`)
      expect(editor.state.selection.from).toBe(1 + marker.length)
    },
  )

  it.each(['*', '**', '***', '_', '__', '___', '~~', '==', '`'])(
    'does not pair %s with a loaded opening marker', marker => {
      const editor = open(`${marker}正文`)
      editor.commands.setTextSelection(editor.state.doc.content.size - 1)
      type(editor, marker)
      expectLiteral(editor, `${marker}正文${marker}`)
    },
  )

  it.each([
    ['*正文*', '<em>正文</em>'],
    ['**正文**', '<strong>正文</strong>'],
    ['***正文***', '<strong><em>正文</em></strong>'],
    ['_正文_', '<em>正文</em>'],
    ['__正文__', '<strong>正文</strong>'],
    ['___正文___', '<strong><em>正文</em></strong>'],
    ['~~正文~~', '<s>正文</s>'],
    ['==正文==', '<mark>正文</mark>'],
    ['`正文`', '<code>正文</code>'],
  ])('formats continuously typed %s at the current caret', (input, html) => {
    const editor = open('前')
    editor.commands.setTextSelection(2)
    type(editor, input)
    expect(editor.getHTML()).toContain(html)
    expect(editor.state.selection.from).toBe(4)
    expect(open(getMarkdown(editor)).state.doc.toJSON()).toEqual(editor.state.doc.toJSON())
  })

  it('undo restores both literal markers without removing earlier typing', () => {
    const editor = open()
    type(editor, '**正文**')
    expect(editor.getHTML()).toContain('<strong>正文</strong>')
    expect(editor.commands.undo()).toBe(true)
    expectLiteral(editor, '**正文**')
    expect(editor.commands.redo()).toBe(true)
    expect(editor.getHTML()).toContain('<strong>正文</strong>')
  })

  it('forgets the opening marker after moving away and back', () => {
    const editor = open()
    type(editor, '*正文')
    editor.commands.setTextSelection(1)
    editor.commands.setTextSelection(4)
    type(editor, '*')
    expectLiteral(editor, '*正文*')
  })

  it.each(['blur', 'paste', 'compositionstart'] as const)('forgets pending markers on %s', name => {
    const editor = open()
    type(editor, '*正文')
    event(editor, name)
    if (name === 'compositionstart') event(editor, 'compositionend')
    type(editor, '*')
    expectLiteral(editor, '*正文*')
  })

  it('does not convert during IME composition', () => {
    const editor = open()
    type(editor, '*正文')
    event(editor, 'compositionstart')
    type(editor, '*')
    event(editor, 'compositionend')
    expectLiteral(editor, '*正文*')
  })

  it('does not pair across pasted content', () => {
    const editor = open()
    type(editor, '*正文')
    pasteMarkdownTextWithResult(editor, '粘贴')
    type(editor, '*')
    expectLiteral(editor, '*正文粘贴*')
  })

  it('does not use a programmatically inserted opening marker', () => {
    const editor = open()
    editor.commands.insertContent('**正文*')
    type(editor, '*')
    expectLiteral(editor, '**正文**')
  })

  it('resets pending markers when a host switches editing modes', () => {
    const editor = open()
    type(editor, '*正文')
    resetInlineInput(editor)
    type(editor, '*')
    expectLiteral(editor, '*正文*')
  })

  it('does not convert when input replaces a selection', () => {
    const editor = open()
    type(editor, '*正文x')
    editor.commands.setTextSelection({ from: 4, to: 5 })
    type(editor, '*')
    expectLiteral(editor, '*正文*')
  })

  it('keeps later typing separate from the autoformat undo step', () => {
    const editor = open()
    type(editor, '**正文**')
    type(editor, '后')
    editor.commands.undo()
    expect(editor.getHTML()).toContain('<strong>正文</strong>')
    expect(editor.state.doc.textContent).toBe('正文')
    editor.commands.undo()
    expectLiteral(editor, '**正文**')
  })

  it('does not share pending markers between editors', () => {
    const first = open()
    const second = open('正文')
    type(first, '*')
    second.commands.setTextSelection(3)
    type(second, '*')
    expectLiteral(second, '正文*')
    type(first, '正文*')
    expect(first.getHTML()).toContain('<em>正文</em>')
  })

  it('preserves syntax-owned equals in code, formulas and link destinations', () => {
    const markdown = '`a==b` $a==b$ [link](https://example.test/?a==b)\n\n```text\na==b\n```\n\n$$\na==b\n$$'
    const editor = open(markdown)
    expect(getMarkdown(editor)).toBe(markdown)
    expect(open(getMarkdown(editor)).state.doc.toJSON()).toEqual(editor.state.doc.toJSON())
  })

  it('respects the explicit preference to serialize literal symbols without escapes', () => {
    const editor = open()
    editor.commands.insertContent({ type: 'text', text: '==正文==' })
    setMarkdownEditingSettings({ escapeMarkdownLiteralSymbols: false })
    expect(getMarkdown(editor)).toBe('==正文==')
  })

  it.each(['\\*正文*', '\\_正文_', '*正文\\*', '_正文\\_', '* 正文*', '*正文 *'])(
    'leaves escaped or whitespace-delimited %s literal', input => {
      const editor = open('前')
      editor.commands.setTextSelection(2)
      type(editor, input)
      expectLiteral(editor, `前${input}`)
    },
  )

  it('does not convert in code blocks', () => {
    const editor = open('```text\nexisting\n```')
    editor.commands.setTextSelection(1)
    type(editor, '*正文*')
    expect(editor.getHTML()).not.toContain('<em>')
    expect(editor.state.doc.textContent).toBe('*正文*existing')
  })

  it.each([
    ['# ', 'heading'], ['- ', 'bulletList'], ['1. ', 'orderedList'],
    ['> ', 'blockquote'], ['    ', 'codeBlock'],
  ])('retains the block shortcut %s', (input, node) => {
    const editor = open()
    type(editor, input)
    expect(editor.isActive(node)).toBe(true)
  })
})
