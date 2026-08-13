import { afterEach, describe, expect, it } from 'vitest'
import { SourceEditor } from '../src/source-editor'
import { createEditor, getMarkdown, replaceEditorDocument } from '../src/editor'

const sources: SourceEditor[] = []
const editorsForSourceTests: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const source of sources.splice(0)) source.destroy()
  for (const editor of editorsForSourceTests.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

describe('source editor', () => {
  it('finds and replaces Markdown without losing source text', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const source = new SourceEditor(parent, '# Title\n\nleaf leaf', () => {})
    sources.push(source)

    expect(source.find('leaf', true, false)).toEqual({ current: 1, total: 2 })
    expect(source.replaceCurrent('leaf', 'branch', true, false)).toEqual({ current: 1, total: 1 })
    expect(source.replaceAll('leaf', 'tree', true, false)).toBe(1)
    expect(source.getText()).toBe('# Title\n\nbranch tree')
  })

  it('preserves Markdown across visual and source editor reconstruction', () => {
    const visualParent = document.createElement('div')
    const sourceParent = document.createElement('div')
    document.body.append(visualParent, sourceParent)
    const initialVisual = createEditor(visualParent, '# Title\n\n**bold**')
    editorsForSourceTests.push(initialVisual)
    const source = new SourceEditor(sourceParent, getMarkdown(initialVisual), () => {})
    sources.push(source)

    editorsForSourceTests.splice(editorsForSourceTests.indexOf(initialVisual), 1)
    const visual = replaceEditorDocument(initialVisual, visualParent, source.getText())
    editorsForSourceTests.push(visual)
    expect(getMarkdown(visual)).toContain('# Title')
    expect(getMarkdown(visual)).toContain('**bold**')
  })

  it('preserves consecutive blank lines in plain text exactly', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const content = 'first\n\n\nsecond\n'
    const source = new SourceEditor(parent, content, () => {})
    sources.push(source)

    expect(source.getText()).toBe(content)
  })

  it('exports and replaces the selected Markdown source', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const source = new SourceEditor(parent, '**bold**', () => {})
    sources.push(source)
    source.view.dispatch({ selection: { anchor: 0, head: 8 } })

    expect(source.getSelectedText()).toBe('**bold**')
    expect(source.replaceSelection('plain')).toBe(true)
    expect(source.getText()).toBe('plain')
  })

  it('deletes only a non-empty source selection', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const source = new SourceEditor(parent, 'leaf', () => {})
    sources.push(source)

    expect(source.deleteSelection()).toBe(false)
    source.view.dispatch({ selection: { anchor: 0, head: 4 } })
    expect(source.deleteSelection()).toBe(true)
    expect(source.getText()).toBe('')
  })
})
