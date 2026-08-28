import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getMarkdown } from '../src/editor'

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

function placeCursorInText(editor: ReturnType<typeof createEditor>, text: string): void {
  let position: number | null = null
  editor.state.doc.descendants((node, pos) => {
    if (node.isText && node.text === text) {
      position = pos + 1
      return false
    }
    return position === null
  })
  if (position === null) throw new Error(`Text not found: ${text}`)
  editor.commands.setTextSelection(position)
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

  it.each([
    ['bullet list', '- 第一\n- 第二', /- 第一\n\s+- 第二/],
    ['ordered list', '1. 第一\n2. 第二', /1\. 第一\n\s+1\. 第二/],
    ['task list', '- [ ] 第一\n- [ ] 第二', /- \[ \] 第一\n\s+- \[ \] 第二/],
  ])('indents the current %s item when Tab is pressed', (_name, markdown, nestedPattern) => {
    const editor = makeEditor(markdown)
    placeCursorInText(editor, '第二')

    const event = pressTab(editor)

    expect(event.defaultPrevented).toBe(true)
    expect(getMarkdown(editor)).toMatch(nestedPattern)
  })

  it('outdents a nested list item when Shift+Tab is pressed', () => {
    const markdown = '- 第一\n- 第二'
    const editor = makeEditor(markdown)
    placeCursorInText(editor, '第二')

    expect(pressTab(editor).defaultPrevented).toBe(true)
    expect(pressTab(editor, true).defaultPrevented).toBe(true)
    expect(getMarkdown(editor).trimEnd()).toBe(markdown)
  })

  it.each([
    ['bullet list', '- 第一\n- 第二'],
    ['ordered list', '1. 第一\n2. 第二'],
    ['task list', '- [ ] 第一\n- [ ] 第二'],
  ])('indents and outdents the current %s item through editor commands', (_name, markdown) => {
    const editor = makeEditor(markdown)
    placeCursorInText(editor, '第二')

    expect(executeEditorCommand(editor, 'indentListItem')).toBe(true)
    expect(getMarkdown(editor)).not.toBe(markdown)
    expect(executeEditorCommand(editor, 'outdentListItem')).toBe(true)
    expect(getMarkdown(editor).trimEnd()).toBe(markdown)
  })

  it('does not insert visual indentation inside a table cell', () => {
    const editor = makeEditor('| A |\n| --- |\n| B |')
    editor.commands.setTextSelection(3)

    pressTab(editor)

    // The table extension may consume Tab; the visual indent extension must not.
    expect(getMarkdown(editor).split('\n').some((line) => line.startsWith('  '))).toBe(false)
  })
})
