import { afterEach, describe, expect, it } from 'vitest'
import { EditorState } from '@codemirror/state'
import { createEditor, replaceEditorDocument } from '../src/editor'
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
})
