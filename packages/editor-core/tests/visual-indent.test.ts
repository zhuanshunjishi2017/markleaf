import { afterEach, describe, expect, it } from 'vitest'
import { TextSelection } from '@tiptap/pm/state'
import { createEditor } from '../src/editor'

describe('visual code block indentation', () => {
  let editor: ReturnType<typeof createEditor> | null = null

  afterEach(() => {
    editor?.destroy()
    editor = null
    document.body.replaceChildren()
  })

  it('keeps the caret after the inserted indent at the start of a code line', () => {
    const mount = document.createElement('main')
    document.body.append(mount)
    editor = createEditor(mount, '```python\nself.build(1, 0, self.n - 1, arr)\n```')
    const codeBlock = editor.state.doc.firstChild!
    const codeBlockPos = 0
    const lineStart = codeBlockPos + 1
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, lineStart)))

    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
    editor.view.dom.dispatchEvent(event)

    expect(editor.state.doc.firstChild?.textContent?.startsWith('  self')).toBe(true)
    expect(editor.state.selection.from).toBe(lineStart + 2)
  })

  it('preserves the caret offset when indenting before existing code text', () => {
    const mount = document.createElement('main')
    document.body.append(mount)
    editor = createEditor(mount, '```python\n    self.build(1, 0, self.n - 1, arr)\n```')
    const lineStart = 1
    const caretBeforeSelf = lineStart + 4
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, caretBeforeSelf)))

    editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))

    expect(editor.state.doc.firstChild?.textContent?.startsWith('      self')).toBe(true)
    expect(editor.state.selection.from).toBe(caretBeforeSelf + 2)
  })

  it('indents every code line covered by a text selection', () => {
    const mount = document.createElement('main')
    document.body.append(mount)
    editor = createEditor(mount, '```python\nfirst\nsecond\nthird\n```')
    const lineStart = 1
    const selectionFrom = lineStart + 1
    const selectionTo = lineStart + 'first\nsecond\nthird'.length - 1
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, selectionFrom, selectionTo)))

    editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))

    expect(editor.state.doc.firstChild?.textContent).toBe('  first\n  second\n  third')
    expect(editor.state.selection.from).toBe(selectionFrom + 2)
    expect(editor.state.selection.to).toBe(selectionTo + 6)
  })

  it('removes indentation from every selected code line with Shift-Tab', () => {
    const mount = document.createElement('main')
    document.body.append(mount)
    editor = createEditor(mount, '```python\n  first\n  second\n  third\n```')
    const lineStart = 1
    const selectionFrom = lineStart + 1
    const selectionTo = lineStart + '  first\n  second\n  third'.length - 1
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, selectionFrom, selectionTo)))

    editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }))

    expect(editor.state.doc.firstChild?.textContent).toBe('first\nsecond\nthird')
    expect(editor.state.selection.from).toBe(selectionFrom - 1)
    expect(editor.state.selection.to).toBe(selectionTo - 6)
  })
})
