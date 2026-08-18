import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, setBlockHandleVisible } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor() {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, '段落内容')
  editors.push(editor)
  editor.commands.setTextSelection(1)
  return editor
}

describe('paragraph block handle visibility', () => {
  it('hides the handle widget when visibility is disabled', () => {
    const editor = makeEditor()

    setBlockHandleVisible(editor, false)

    expect(editor.view.dom.querySelector('.ml-block-handle')).toBeNull()
  })

  it('restores the handle widget when visibility is enabled again', () => {
    const editor = makeEditor()

    setBlockHandleVisible(editor, false)
    setBlockHandleVisible(editor, true)

    expect(editor.view.dom.querySelector('.ml-block-handle')).not.toBeNull()
  })

  it('shows the handle inside a list item with the list-type label', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '- list item')
    editors.push(editor)
    editor.commands.setTextSelection(3)

    const handle = editor.view.dom.querySelector<HTMLButtonElement>('.ml-block-handle')
    expect(handle).not.toBeNull()
    expect(handle?.textContent).toBe('•')
  })

  it('hides the handle during IME composition and restores it afterwards', async () => {
    const editor = makeEditor()

    expect(editor.view.dom.querySelector('.ml-block-handle')).not.toBeNull()

    editor.view.dom.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))

    expect(editor.view.dom.querySelector('.ml-block-handle')).toBeNull()

    editor.view.dom.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }))
    await new Promise(resolve => window.setTimeout(resolve, 0))

    expect(editor.view.dom.querySelector('.ml-block-handle')).not.toBeNull()
  })
})
