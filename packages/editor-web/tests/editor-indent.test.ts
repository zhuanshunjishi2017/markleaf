import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(markdown: string, readOnly = false): ReturnType<typeof createEditor> {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown, readOnly)
  editors.push(editor)
  return editor
}

function pressTab(editor: ReturnType<typeof createEditor>, shiftKey = false): KeyboardEvent {
  const event = new KeyboardEvent('keydown', {
    key: 'Tab',
    code: 'Tab',
    bubbles: true,
    cancelable: true,
    shiftKey,
  })
  editor.view.dom.dispatchEvent(event)
  return event
}

describe('visual editor indentation', () => {
  it('inserts an indent when Tab is pressed in a paragraph', () => {
    const editor = makeEditor('文本')
    editor.commands.setTextSelection(1)

    const event = pressTab(editor)

    expect(event.defaultPrevented).toBe(true)
    expect(getMarkdown(editor)).toBe('  文本')
  })

  it('does not change a read-only document when Tab is pressed', () => {
    const editor = makeEditor('文本', true)
    editor.commands.setTextSelection(1)

    pressTab(editor)

    expect(getMarkdown(editor)).toBe('文本')
  })

  it('removes the visual indent with Shift+Tab', () => {
    const editor = makeEditor('  文本')
    editor.commands.setTextSelection(3)

    const event = pressTab(editor, true)

    expect(event.defaultPrevented).toBe(true)
    expect(getMarkdown(editor)).toBe('文本')
  })

  it('leaves list item Tab behavior to the list extension', () => {
    const editor = makeEditor('- 项目')
    editor.commands.setTextSelection(3)

    const event = pressTab(editor)

    expect(event.defaultPrevented).toBe(false)
    expect(getMarkdown(editor)).toMatch(/^- 项目/)
  })

  it('does not insert visual indentation inside a table cell', () => {
    const editor = makeEditor('| A |\n| --- |\n| B |')
    editor.commands.setTextSelection(3)

    pressTab(editor)

    // The table extension may consume Tab; the visual indent extension must not.
    expect(getMarkdown(editor).split('\n').some((line) => line.startsWith('  '))).toBe(false)
  })
})
