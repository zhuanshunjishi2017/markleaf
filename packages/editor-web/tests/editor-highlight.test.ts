import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(markdown: string): ReturnType<typeof createEditor> {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  return editor
}

describe('Markdown highlight', () => {
  it('renders ==text== and preserves the syntax when serialized', () => {
    const editor = makeEditor('before ==highlight== after')

    expect(editor.view.dom.querySelector('mark')?.textContent).toBe('highlight')
    expect(getMarkdown(editor)).toContain('before ==highlight== after')
  })

  it('supports inline formatting inside highlighted text', () => {
    const editor = makeEditor('==**bold** and *italic*==')
    const mark = editor.view.dom.querySelector('mark')

    expect(mark?.querySelector('strong')?.textContent).toBe('bold')
    expect(mark?.querySelector('em')?.textContent).toBe('italic')
    expect(getMarkdown(editor)).toContain('==**bold** and *italic*==')
  })

  it('converts a newly typed closed pair of double equals into a highlight mark', () => {
    const editor = makeEditor('')
    editor.commands.insertContent('==highlight')
    const handled = editor.view.someProp(
      'handleTextInput',
      handler => handler(
        editor.view,
        editor.state.selection.from,
        editor.state.selection.to,
        '==',
        () => editor.state.tr.insertText('=='),
      ),
    )

    expect(handled).toBe(true)
    expect(editor.getJSON().content?.[0]?.content?.[0]?.marks).toEqual([{ type: 'highlight' }])
    expect(getMarkdown(editor)).toContain('==highlight==')
  })
})
