import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, getMarkdown, setBlockHandleVisible, setCodeHighlightVisible, setEditorFocusMode } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []
afterEach(() => {
  for (const editor of editors.splice(0)) {
    setCodeHighlightVisible(editor, false)
    setBlockHandleVisible(editor, true)
    editor.destroy()
  }
  document.body.replaceChildren()
})

describe('initial display settings', () => {
  it('creates highlighted code in the initial view and ignores unchanged settings', () => {
    const mount = document.createElement('div')
    document.body.append(mount)
    const editor = createEditor(mount, '```js\nconst value = 42\n```', false, { codeHighlightVisible: true })
    editors.push(editor)
    const doc = editor.state.doc
    const selection = editor.state.selection
    const markdown = getMarkdown(editor)
    const transaction = vi.fn()
    editor.on('transaction', transaction)
    expect(mount.querySelector('.ml-code-keyword')?.textContent).toBe('const')
    setCodeHighlightVisible(editor, true)
    setBlockHandleVisible(editor, true)
    setEditorFocusMode(editor, false)
    expect(transaction).not.toHaveBeenCalled()
    expect(editor.state.doc).toBe(doc)
    expect(editor.state.selection).toBe(selection)
    expect(getMarkdown(editor)).toBe(markdown)

    setCodeHighlightVisible(editor, false)
    expect(mount.querySelector('.ml-code-keyword')).toBeNull()
    setCodeHighlightVisible(editor, true)
    expect(mount.querySelector('.ml-code-keyword')?.textContent).toBe('const')
    expect(transaction).toHaveBeenCalledTimes(2)
    expect(editor.state.doc).toBe(doc)
    expect(editor.can().undo()).toBe(false)
    expect(getMarkdown(editor)).toBe(markdown)
  })
})
