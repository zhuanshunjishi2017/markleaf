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
})
