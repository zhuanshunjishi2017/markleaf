import { afterEach, describe, expect, it } from 'vitest'
import { createEditor } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
  window.getSelection()?.removeAllRanges()
})

function makeEditor(
  markdown: string,
  themedVisualSelection: boolean,
): { editor: ReturnType<typeof createEditor>; mount: HTMLElement } {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown, false, { themedVisualSelection })
  editors.push(editor)
  return { editor, mount }
}

function clickEditorBackground(mount: HTMLElement): void {
  mount.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
  mount.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, button: 0 }))
  mount.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0 }))
}

describe('visual editor selection', () => {
  it('installs themed selection only for a macOS editor', () => {
    const macOS = makeEditor('- first item\n- second item', true)
    macOS.editor.commands.setTextSelection({
      from: 3,
      to: macOS.editor.state.doc.content.size - 3,
    })

    const windows = makeEditor('- first item\n- second item', false)
    windows.editor.commands.setTextSelection({
      from: 3,
      to: windows.editor.state.doc.content.size - 3,
    })

    expect(macOS.mount.querySelector('li .markleaf-themed-selection')).not.toBeNull()
    expect(windows.mount.querySelector('.markleaf-themed-selection')).toBeNull()
  })

  it('clears macOS list-item highlighting after clicking the editor background', () => {
    const { editor, mount } = makeEditor('- first item\n- second item\n- third item', true)
    editor.commands.setTextSelection({ from: 3, to: editor.state.doc.content.size - 3 })

    expect(mount.querySelector('li .markleaf-themed-selection')).not.toBeNull()
    clickEditorBackground(mount)

    expect(editor.state.selection.empty).toBe(true)
    expect(mount.querySelector('.markleaf-themed-selection')).toBeNull()
  })

  it('collapses a whole-document macOS selection after losing focus', () => {
    const { editor, mount } = makeEditor('# Heading\n\nFirst paragraph.\n\nSecond paragraph.', true)
    editor.commands.selectAll()

    expect(editor.state.selection.empty).toBe(false)
    editor.view.dom.dispatchEvent(new FocusEvent('blur', { bubbles: true }))

    expect(editor.state.selection.empty).toBe(true)
    expect(mount.querySelector('.markleaf-themed-selection')).toBeNull()
  })
})
