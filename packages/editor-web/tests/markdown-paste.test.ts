import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createEditor,
  expandSourceEditor,
  getMarkdown,
  pasteClipboardContent,
  pasteMarkdownText,
  replaceEditorDocument,
  shouldParsePastedTextAsMarkdown,
} from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

if (!globalThis.ClipboardEvent) {
  globalThis.ClipboardEvent = class ClipboardEvent extends Event {} as unknown as typeof ClipboardEvent
}

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(content = '', options: Parameters<typeof createEditor>[3] = {}) {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, content, false, options)
  editors.push(editor)
  return { editor, element }
}

function clipboardEvent(plainText: string, html = ''): ClipboardEvent {
  const event = new Event('paste', { bubbles: true, cancelable: true }) as ClipboardEvent
  Object.defineProperty(event, 'clipboardData', {
    value: {
      items: [],
      files: [],
      types: html ? ['text/plain', 'text/html'] : ['text/plain'],
      getData(type: string) {
        if (type === 'text/plain' || type === 'text') return plainText
        if (type === 'text/html') return html
        return ''
      },
    },
  })
  return event
}

describe('Markdown paste', () => {
  it('replaces the visual selection with heading, list, table, and formula nodes', () => {
    const { editor } = makeEditor('before OLD after')
    editor.commands.setTextSelection({ from: 8, to: 11 })

    expect(pasteMarkdownText(editor, [
      '# Heading',
      '',
      '- one',
      '- two',
      '',
      '| A | B |',
      '| - | - |',
      '| 1 | 2 |',
      '',
      '$$x^2$$',
    ].join('\n'))).toBe(true)

    const nodeTypes: string[] = []
    editor.state.doc.descendants((node) => {
      nodeTypes.push(node.type.name)
    })
    expect(nodeTypes).toEqual(expect.arrayContaining([
      'heading', 'bulletList', 'table', 'mathBlock',
    ]))
    expect(getMarkdown(editor)).not.toContain('OLD')
  })

  it('parses source-editor HTML wrappers when their normalized text is Markdown', () => {
    const { editor } = makeEditor()
    const markdown = '# Heading\r\n\r\n- one\u00a0item'
    const sourceHtml = '<div style="color:#ddd"><pre><code><span style="color:red"># Heading</span>\n\n- one item</code></pre></div>'

    expect(shouldParsePastedTextAsMarkdown(editor, markdown, sourceHtml)).toBe(true)
  })

  it.each([
    {
      name: 'link',
      text: 'OpenAI',
      html: '<p><a href="https://openai.com">OpenAI</a></p>',
    },
    {
      name: 'list',
      text: 'one\ntwo',
      html: '<ul><li>one</li><li>two</li></ul>',
    },
    {
      name: 'table',
      text: 'A\tB\n1\t2',
      html: '<table><tbody><tr><td>A</td><td>B</td></tr><tr><td>1</td><td>2</td></tr></tbody></table>',
    },
  ])('keeps genuine rich HTML $name content on the HTML path', ({ text, html }) => {
    const { editor } = makeEditor()

    expect(shouldParsePastedTextAsMarkdown(editor, text, html)).toBe(false)
  })

  it('uses Markdown for plain text and text-equivalent wrapper-only HTML', () => {
    const { editor } = makeEditor()

    expect(shouldParsePastedTextAsMarkdown(editor, '- plain', '')).toBe(true)
    expect(shouldParsePastedTextAsMarkdown(
      editor,
      '# Heading\r\nsecond\u00a0line',
      '<div><span># Heading</span><br><span>second&nbsp;line</span></div>',
    )).toBe(true)
  })

  it('falls back to rich HTML when only HTML is available', () => {
    const { editor } = makeEditor()

    expect(pasteClipboardContent(editor, '', '<p><strong>rich</strong> text</p>')).toBe(true)
    expect(editor.getHTML()).toContain('<strong>rich</strong>')
  })

  it('preserves a custom paste handler after editor recreation and inserts once per event', () => {
    let activeEditor: ReturnType<typeof createEditor>
    const handlePaste = vi.fn((event: ClipboardEvent) => pasteClipboardContent(
      activeEditor,
      event.clipboardData?.getData('text/plain') ?? '',
      event.clipboardData?.getData('text/html') ?? '',
    ))
    const first = makeEditor('', { handlePaste })
    activeEditor = first.editor

    first.editor.view.dom.dispatchEvent(clipboardEvent('# First'))
    expect(first.element.querySelectorAll('h1')).toHaveLength(1)
    expect(handlePaste).toHaveBeenCalledTimes(1)

    editors.splice(editors.indexOf(first.editor), 1)
    activeEditor = replaceEditorDocument(first.editor, first.element, '', false, { handlePaste })
    editors.push(activeEditor)
    activeEditor.view.dom.dispatchEvent(clipboardEvent('# Second'))

    expect(first.element.querySelectorAll('h1')).toHaveLength(1)
    expect(first.element.textContent).toBe('Second')
    expect(handlePaste).toHaveBeenCalledTimes(2)
  })

  it('leaves expanded formula source paste on its text-editing path', () => {
    const handlePaste = vi.fn(() => true)
    const { editor } = makeEditor('$x$', { handlePaste })
    let mathPosition = -1
    editor.state.doc.descendants((node, position) => {
      if (node.type.name === 'mathInline') mathPosition = position
    })
    expect(expandSourceEditor(editor, mathPosition, 'mathInline')).toBe(true)
    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')
    expect(source).not.toBeNull()

    source!.dispatchEvent(clipboardEvent('# literal'))

    expect(handlePaste).not.toHaveBeenCalled()
  })
})
