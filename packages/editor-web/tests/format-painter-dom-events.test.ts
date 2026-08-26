import { afterEach, expect, it } from 'vitest'
import { createEditor } from '../src/editor'
import { FormatPainterController } from '../src/format-painter'
import { applyFormatPainterFromDomSelection } from '../src/format-painter-dom-events'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
  window.getSelection()?.removeAllRanges()
})

function selectEditorText(editor: ReturnType<typeof createEditor>, text: string): void {
  let from = -1
  editor.state.doc.descendants((node, position) => {
    if (from >= 0 || !node.isText) return from < 0
    const index = node.text!.indexOf(text)
    if (index < 0) return true
    from = position + index
    return false
  })
  expect(from).toBeGreaterThanOrEqual(0)
  editor.commands.setTextSelection({ from, to: from + text.length })
}

it('applies the painter from a newer backward DOM selection while editor state is stale', () => {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, '**source**\n\ntarget line')
  editors.push(editor)

  selectEditorText(editor, 'source')
  const sourceSelection = { from: editor.state.selection.from, to: editor.state.selection.to }
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)

  const target = editor.view.dom.querySelectorAll('p')[1]!
  const domSelection = window.getSelection()!
  domSelection.setBaseAndExtent(target, target.childNodes.length, target, 0)

  expect({ from: editor.state.selection.from, to: editor.state.selection.to })
    .toEqual(sourceSelection)
  expect(applyFormatPainterFromDomSelection(editor, painter, domSelection)).toBe(true)
  expect(editor.getMarkdown()).toBe('**source**\n\n**target line**')
  expect(painter.isArmed).toBe(false)
})
