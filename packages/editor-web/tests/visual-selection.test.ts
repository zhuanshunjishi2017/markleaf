import { afterEach, describe, expect, it } from 'vitest'
import { createEditor } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
  window.getSelection()?.removeAllRanges()
})

function makeEditor(markdown: string): { editor: ReturnType<typeof createEditor>; mount: HTMLElement } {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  return { editor, mount }
}

function clickEditorBackground(mount: HTMLElement): void {
  mount.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
  mount.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  mount.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0 }))
}

describe('visual editor selection', () => {
  it('clears list-item selection highlighting after clicking the editor background', () => {
    const { editor, mount } = makeEditor('- first item\n- second item\n- third item')
    editor.commands.setTextSelection({ from: 3, to: editor.state.doc.content.size - 3 })

    expect(editor.view.dom.querySelector('li .markleaf-themed-selection')).not.toBeNull()
    clickEditorBackground(mount)

    expect(editor.state.selection.empty).toBe(true)
    expect(editor.view.dom.querySelector('.markleaf-themed-selection')).toBeNull()
  })

  it('collapses a whole-document selection after clicking the editor background', () => {
    const { editor, mount } = makeEditor('# Heading\n\nFirst paragraph.\n\nSecond paragraph.')
    editor.commands.selectAll()

    expect(editor.state.selection.empty).toBe(false)
    clickEditorBackground(mount)

    expect(editor.state.selection.empty).toBe(true)
  })
})
