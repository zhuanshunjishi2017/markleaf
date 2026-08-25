import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getBlockHandleInfo, setBlockHandleVisible } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor() {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, '段落内容')
  editor.view.coordsAtPos = () => ({ left: 0, right: 0, top: 10, bottom: 30 })
  editors.push(editor)
  editor.commands.setTextSelection(1)
  return editor
}

describe('paragraph block handle visibility', () => {
  it('hides the handle info when visibility is disabled', () => {
    const editor = makeEditor()

    setBlockHandleVisible(editor, false)

    expect(getBlockHandleInfo(editor)).toBeNull()
  })

  it('restores the handle info when visibility is enabled again', () => {
    const editor = makeEditor()

    setBlockHandleVisible(editor, false)
    setBlockHandleVisible(editor, true)

    expect(getBlockHandleInfo(editor)).not.toBeNull()
  })

  it('shows the handle inside a list item with the list-type label', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '- list item')
    editor.view.coordsAtPos = () => ({ left: 0, right: 0, top: 10, bottom: 30 })
    editors.push(editor)
    editor.commands.setTextSelection(3)

    const handle = getBlockHandleInfo(editor)
    expect(handle).not.toBeNull()
    expect(handle?.label).toBe('•')
  })

  it('positions the footnote handle from the paragraph box', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '[^1]: 注释内容')
    editor.view.coordsAtPos = () => ({ left: 0, right: 0, top: 0, bottom: 0 })
    editors.push(editor)
    editor.commands.setTextSelection(2)
    const paragraph = editor.view.dom.querySelector('.markleaf-footnote-def')
    expect(paragraph).not.toBeNull()
    paragraph!.getBoundingClientRect = () => ({
      x: 0,
      y: 42,
      left: 0,
      right: 100,
      top: 42,
      bottom: 66,
      width: 100,
      height: 24,
      toJSON: () => {},
    })

    const handle = getBlockHandleInfo(editor)

    expect(handle?.label).toBe('注')
    expect(handle?.viewportTop).toBe(42)
  })

  it('keeps block handle widgets out of the editable DOM during IME composition', async () => {
    const editor = makeEditor()

    expect(getBlockHandleInfo(editor)).not.toBeNull()
    expect(editor.view.dom.querySelector('.ml-block-handle')).toBeNull()

    editor.view.dom.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))

    // IME 组合输入期间正文 DOM 内不能出现 absolute/contenteditable=false widget，
    // 否则 Chromium/WebView2 容易把组合文本提交位置算偏。
    editor.view.dispatch(editor.state.tr.insertText('a', 1))

    expect(editor.view.dom.querySelector('.ml-block-handle')).toBeNull()

    editor.view.dom.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }))
    await new Promise(resolve => window.setTimeout(resolve, 60))

    expect(getBlockHandleInfo(editor)).not.toBeNull()
  })
})
