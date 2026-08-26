import { afterEach, describe, expect, it } from 'vitest'
import { createEditor } from '../src/editor'
import { syncDomSelectionToEditor } from '../src/dom-selection-sync'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
  window.getSelection()?.removeAllRanges()
})

function makeEditor(markdown: string): ReturnType<typeof createEditor> {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  return editor
}

describe('DOM selection synchronization', () => {
  it('preserves a backward whole-line selection made from element boundaries', () => {
    const editor = makeEditor('target line')
    const paragraph = editor.view.dom.querySelector('p')!
    const selection = window.getSelection()!
    selection.setBaseAndExtent(
      paragraph,
      paragraph.childNodes.length,
      paragraph,
      0,
    )

    expect(syncDomSelectionToEditor(editor, selection)).toBe(true)
    expect(editor.state.selection.from).toBe(1)
    expect(editor.state.selection.to).toBe(12)
    expect(editor.state.selection.anchor).toBeGreaterThan(editor.state.selection.head)
  })

  it('maps a backward selection across multiple paragraph boundaries', () => {
    const editor = makeEditor('first target\n\nsecond target\n\nthird target')
    const paragraphs = editor.view.dom.querySelectorAll('p')
    const first = paragraphs[0]!
    const third = paragraphs[2]!
    const selection = window.getSelection()!
    selection.setBaseAndExtent(third, third.childNodes.length, first, 0)

    expect(syncDomSelectionToEditor(editor, selection)).toBe(true)
    expect(editor.state.selection.from).toBe(1)
    expect(editor.state.selection.to).toBe(editor.state.doc.content.size - 1)
    expect(editor.state.selection.anchor).toBeGreaterThan(editor.state.selection.head)
  })

  it('rejects a selection whose focus is outside the editor', () => {
    const editor = makeEditor('inside')
    const paragraph = editor.view.dom.querySelector('p')!
    const outside = document.createElement('p')
    outside.textContent = 'outside'
    document.body.append(outside)
    const selection = window.getSelection()!
    selection.setBaseAndExtent(paragraph, 0, outside.firstChild!, 1)

    expect(syncDomSelectionToEditor(editor, selection)).toBe(false)
    expect(editor.state.selection.empty).toBe(true)
  })
})
