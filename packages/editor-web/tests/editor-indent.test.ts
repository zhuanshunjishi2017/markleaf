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

function pressEnter(editor: ReturnType<typeof createEditor>): KeyboardEvent {
  const event = new KeyboardEvent('keydown', {
    key: 'Enter',
    code: 'Enter',
    bubbles: true,
    cancelable: true,
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

function placeCursorInEmptyListItem(editor: ReturnType<typeof createEditor>): void {
  let position: number | null = null
  editor.state.doc.descendants((node, pos) => {
    if (node.type.name === 'paragraph' && node.content.size === 0) {
      let depth = editor.state.doc.resolve(pos + 1).depth
      while (depth > 0) {
        const ancestor = editor.state.doc.resolve(pos + 1).node(depth)
        if (ancestor.type.name === 'listItem' || ancestor.type.name === 'taskItem') {
          position = pos + 1
          return false
        }
        depth -= 1
      }
    }
    return position === null
  })
  if (position === null) throw new Error('Empty list item not found')
  editor.commands.setTextSelection(position)
}

function placeCursorAtEndOfText(editor: ReturnType<typeof createEditor>, text: string): void {
  let position: number | null = null
  editor.state.doc.descendants((node, pos) => {
    if (node.isText && node.text === text) {
      position = pos + node.nodeSize
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

  it.each([
    ['bullet list', '- item\n- ', '- item\n- \n- '],
    ['task list', '- [x] item\n- [ ] ', '- [x] item\n- [ ] \n- [ ] '],
  ])('keeps an empty %s item and creates another item on Enter', (_name, markdown, expected) => {
    const editor = makeEditor(markdown)
    placeCursorInEmptyListItem(editor)

    const event = pressEnter(editor)

    expect(event.defaultPrevented).toBe(true)
    expect(getMarkdown(editor).trimEnd()).toBe(expected.trimEnd())
  })

  it('keeps the empty item created by repeated Enter in an ordered list', () => {
    const editor = makeEditor('7. item')
    placeCursorAtEndOfText(editor, 'item')

    expect(pressEnter(editor).defaultPrevented).toBe(true)
    expect(pressEnter(editor).defaultPrevented).toBe(true)
    expect(getMarkdown(editor).trimEnd()).toBe('7. item\n8. \n9.')
  })

  it('preserves empty items, separate lists, and ordered list numbering during serialization', () => {
    const markdown = '- first\n- \n\n- second\n\n7. first\n9. second\n\n1. separate'
    const editor = makeEditor(markdown)

    expect(getMarkdown(editor)).toBe(markdown)
  })
})
