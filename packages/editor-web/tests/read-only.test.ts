import { afterEach, describe, expect, it } from 'vitest'
import { EditorState } from '@codemirror/state'
import { collapseVisualSelection, createEditor, executeEditorCommand, replaceEditorDocument } from '../src/editor'
import { SourceEditor } from '../src/source-editor'

const editors: ReturnType<typeof createEditor>[] = []
const sources: SourceEditor[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  for (const source of sources.splice(0)) source.destroy()
  document.body.innerHTML = ''
})

describe('read-only documents', () => {
  it('renders the visual editor as non-editable', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, '# Title', true)
    editors.push(editor)

    expect(editor.isEditable).toBe(false)
  })

  it('keeps the source editor read-only while retaining the state facet', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const source = new SourceEditor(el, 'leaf', () => {}, 2, true)
    sources.push(source)

    expect(source.view.state.facet(EditorState.readOnly)).toBe(true)
  })

  it('replaces the document without losing read-only state', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'a')
    const replacement = replaceEditorDocument(editor, el, 'b', true)
    editors.push(replacement)

    expect(replacement.isEditable).toBe(false)
  })

  it('selects the whole document through the host selectAll command in read-only mode', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'hello world', true)
    editors.push(editor)

    expect(executeEditorCommand(editor, 'selectAll')).toBe(true)
    expect(editor.state.selection.empty).toBe(false)
    expect(editor.state.selection.from).toBe(0)
  })

  it('collapses the visual selection with Escape', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'hello world', true)
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 6 })

    expect(editor.state.selection.empty).toBe(false)
    expect(collapseVisualSelection(editor)).toBe(true)
    expect(editor.state.selection.empty).toBe(true)
  })

  it('does not rewrite selected visual text with inline decorations', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'hello world', true)
    editors.push(editor)
    const htmlBeforeSelection = editor.view.dom.innerHTML
    editor.commands.setTextSelection({ from: 1, to: 6 })

    expect(editor.view.dom.innerHTML).toBe(htmlBeforeSelection)
    expect(editor.state.selection.empty).toBe(false)
  })

  it('cancels native text dragging in the read-only visual editor', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'hello world', true)
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 6 })
    const dragStart = new Event('dragstart', { bubbles: true, cancelable: true })

    expect(editor.view.dom.dispatchEvent(dragStart)).toBe(false)
    expect(dragStart.defaultPrevented).toBe(true)
    expect(editor.state.selection.from).toBe(1)
    expect(editor.state.selection.to).toBe(6)
  })

  it('keeps native text dragging available in the editable visual editor', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'hello world')
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 6 })
    const dragStart = new Event('dragstart', { bubbles: true, cancelable: true })

    expect(editor.view.dom.dispatchEvent(dragStart)).toBe(true)
    expect(dragStart.defaultPrevented).toBe(false)
  })

  it('cancels native text dragging in the read-only source editor', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const source = new SourceEditor(el, 'hello world', () => {}, 2, true)
    sources.push(source)
    source.setSelection(1, 6)
    const dragStart = new Event('dragstart', { bubbles: true, cancelable: true })

    expect(source.view.contentDOM.dispatchEvent(dragStart)).toBe(false)
    expect(dragStart.defaultPrevented).toBe(true)
    expect(source.view.state.selection.main.from).toBe(1)
    expect(source.view.state.selection.main.to).toBe(6)
  })

  it('clears source selection decorations when the source editor loses focus', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const source = new SourceEditor(el, 'leaf', () => {}, 2, true)
    sources.push(source)
    source.setSelection(1, 4)

    expect(source.view.dom.querySelector('.ml-source-selection')).not.toBeNull()
    source.view.contentDOM.dispatchEvent(new FocusEvent('blur'))
    expect(source.view.dom.querySelector('.ml-source-selection')).toBeNull()
  })
})
