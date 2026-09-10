import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getMarkdown, updateEditorMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []
function open(markdown: string, externalHistory = false) {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown, false, { externalHistory })
  editors.push(editor)
  return editor
}

afterEach(() => {
  editors.splice(0).forEach(editor => editor.destroy())
  document.body.innerHTML = ''
})

describe('underline Markdown content fidelity', () => {
  it.each([
    'I need to develop a C++ memory pool project based on C++17.',
    'C++17 and C++20',
    'C++17 and C++',
    'i++ + j++',
    '++i; ++j',
    '++i;++j',
    '++ leading space++',
    '++trailing space ++',
    '++++',
  ])('preserves literal text after editing and reopening: %s', markdown => {
    const editor = open(markdown, true)
    expect(editor.state.doc.textContent).toBe(markdown)
    expect(editor.getHTML()).not.toContain('<u>')
    editor.view.dispatch(editor.state.tr.insertText('Edited: ', 1))
    const saved = getMarkdown(editor)
    expect(saved).toBe(`Edited: ${markdown}`)
    updateEditorMarkdown(editor, saved)
    expect(editor.state.doc.textContent).toBe(`Edited: ${markdown}`)
    expect(editor.getHTML()).not.toContain('<u>')
  })

  it.each(['++下划线++', '前++下划线++后', '++two  spaces++', '++**bold** and italic *text*++'])('retains explicit underline formatting: %s', markdown => {
    const editor = open(markdown)
    expect(editor.getHTML()).toContain('<u>')
    const content = editor.state.doc.toJSON()
    updateEditorMarkdown(editor, getMarkdown(editor))
    expect(editor.state.doc.toJSON()).toEqual(content)
  })

  it('keeps code content opaque, including literal delimiters', () => {
    const editor = open('Sample\n\n`C++ and ++text++`\n\n```cpp\nC++17; i++; ++j;\n```')
    expect(editor.getHTML()).not.toContain('<u>')
    editor.view.dispatch(editor.state.tr.insertText('Code: ', 1))
    const saved = getMarkdown(editor)
    expect(saved).toContain('`C++ and ++text++`')
    expect(saved).toContain('```cpp\nC++17; i++; ++j;\n```')
    expect(open(saved).state.doc.toJSON()).toEqual(editor.state.doc.toJSON())
  })

  it.each(['word', 'C++', '++literal++', 'two  spaces', 'with **literal** markers'])('retains a visually applied underline inside other text: %s', text => {
    const editor = open('prefix suffix')
    editor.view.dispatch(editor.state.tr.insertText(text, 8))
    editor.commands.setTextSelection({ from: 8, to: 8 + text.length })
    expect(executeEditorCommand(editor, 'toggleUnderline')).toBe(true)
    const content = editor.state.doc.toJSON()
    const saved = getMarkdown(editor)
    expect(open(saved).state.doc.toJSON()).toEqual(content)
    expect(executeEditorCommand(editor, 'undo')).toBe(true)
    expect(editor.getHTML()).not.toContain('<u>')
    expect(executeEditorCommand(editor, 'redo')).toBe(true)
    expect(editor.getHTML()).toContain('<u>')
  })
})
