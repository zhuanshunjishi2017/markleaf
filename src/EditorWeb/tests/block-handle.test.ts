import { afterEach, expect, it } from 'vitest'
import type { Editor } from '@tiptap/core'
import { createEditor, executeEditorCommand } from '../src/editor'

const editors: Editor[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(markdown: string): Editor {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, markdown)
  editors.push(editor)
  return editor
}

it('inserts an empty paragraph before the current block', () => {
  const editor = makeEditor('first paragraph\n\nsecond paragraph')
  editor.commands.setTextSelection(3)

  expect(executeEditorCommand(editor, 'insertLineBefore')).toBe(true)
  expect(editor.getText()).toContain('\n\nfirst paragraph')
})

it('inserts an empty paragraph after the current block', () => {
  const editor = makeEditor('first paragraph\n\nsecond paragraph')
  editor.commands.setTextSelection(3)

  expect(executeEditorCommand(editor, 'insertLineAfter')).toBe(true)
  expect(editor.state.doc.childCount).toBe(3)
  expect(editor.getText()).toContain('first paragraph')
  expect(editor.getText()).toContain('second paragraph')
})

it('does not treat block highlight metadata as a document change', () => {
  const editor = makeEditor('paragraph')
  let updates = 0
  editor.on('update', ({ transaction }) => {
    if (transaction.docChanged) updates += 1
  })

  expect(executeEditorCommand(editor, 'setBlockHighlight', '0')).toBe(true)
  expect(updates).toBe(0)
})
