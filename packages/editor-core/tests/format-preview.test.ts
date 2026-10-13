import { afterEach, describe, expect, it } from 'vitest'
import * as core from '../src/index'
import type { Editor } from '@tiptap/core'
import { undoDepth } from '@tiptap/pm/history'

let editor: Editor
function setup(markdown = 'before **bold** after', themedVisualSelection = false) {
  document.body.innerHTML = '<main></main>'
  editor = core.createEditor(document.querySelector('main')!, markdown, false, { themedVisualSelection })
  editor.view.setProps({ handleScrollToSelection: () => true })
  editor.commands.setTextSelection({ from: 8, to: 12 })
  return editor
}
function preview(command: string | null) {
  const api = core as typeof core & { setEditorFormatPreview: (editor: Editor, command: string | null) => void }
  expect(typeof api.setEditorFormatPreview).toBe('function')
  api.setEditorFormatPreview(editor, command)
}
afterEach(() => { editor?.destroy(); document.body.innerHTML = '' })

describe('temporary format preview', () => {
  it('temporarily hides the selection paint without losing the selection, and restores it after hover', () => {
    setup('plain text', true)
    editor.commands.setTextSelection({ from: 1, to: 6 })
    const selection = editor.state.selection.toJSON()
    expect(editor.view.dom.querySelector('.markleaf-themed-selection')).not.toBeNull()
    preview('toggleHighlight')
    expect(editor.view.dom.querySelector('.markleaf-themed-selection')).toBeNull()
    expect(editor.view.dom.classList.contains('markleaf-format-preview-active')).toBe(true)
    expect(editor.state.selection.toJSON()).toEqual(selection)
    preview(null)
    expect(editor.view.dom.querySelector('.markleaf-themed-selection')).not.toBeNull()
    expect(editor.view.dom.classList.contains('markleaf-format-preview-active')).toBe(false)
  })
  it('previews removing an active mark without changing markdown, selection or undo history', () => {
    setup()
    const selection = editor.state.selection.toJSON()
    const depth = undoDepth(editor.state)
    preview('toggleBold')
    expect(editor.view.dom.querySelector('[data-format-preview="toggleBold"]')?.getAttribute('style'))
      .toContain('font-weight: normal')
    expect(core.getMarkdown(editor)).toBe('before **bold** after')
    expect(editor.state.selection.toJSON()).toEqual(selection)
    expect(undoDepth(editor.state)).toBe(depth)
    preview(null)
    expect(editor.view.dom.querySelector('[data-format-preview]')).toBeNull()
  })

  it.each([
    ['toggleBold', 'font-weight: 700'],
    ['toggleItalic', 'font-style: italic'],
    ['toggleUnderline', 'underline'],
    ['toggleStrike', 'line-through'],
    ['toggleHighlight', 'var(--highlight'],
  ])('previews %s on unformatted selected text', (command, style) => {
    setup('plain text')
    editor.commands.setTextSelection({ from: 1, to: 6 })
    preview(command)
    expect(editor.view.dom.querySelector('[data-format-preview]')?.getAttribute('style')).toContain(style)
    expect(core.getMarkdown(editor)).toBe('plain text')
  })

  it('replaces a previous preview and clears it when the user changes the selection', () => {
    setup()
    preview('toggleItalic')
    preview('toggleStrike')
    expect(editor.view.dom.querySelector('[data-format-preview="toggleItalic"]')).toBeNull()
    editor.commands.setTextSelection(1)
    expect(editor.view.dom.querySelector('[data-format-preview]')).toBeNull()
  })

  it('does not leave decorations on a replaced document or allow read-only preview', () => {
    setup()
    preview('toggleItalic')
    editor.commands.setContent('replacement')
    expect(editor.view.dom.querySelector('[data-format-preview]')).toBeNull()
    editor.setEditable(false)
    preview('toggleBold')
    expect(editor.view.dom.querySelector('[data-format-preview]')).toBeNull()
  })
})
