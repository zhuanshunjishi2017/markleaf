import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, pasteMarkdownText, shouldParsePastedTextAsMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(content = '') {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, content)
  editors.push(editor)
  return editor
}

describe('Markdown paste', () => {
  it('parses block and inline Markdown through the Markdown manager', () => {
    const editor = makeEditor('')

    expect(pasteMarkdownText(editor, '# Heading\n\n**bold** and $x^2$\n\n$$y^2$$')).toBe(true)

    expect(editor.getJSON().content?.map(node => node.type)).toEqual([
      'heading',
      'paragraph',
      'mathBlock',
      'paragraph',
    ])
    expect(editor.getHTML()).toContain('<strong>bold</strong>')
    expect(editor.getHTML()).toContain('data-math-inline="1"')
    expect(editor.getHTML()).toContain('data-math-block="1"')
  })

  it('recognizes Markdown source even when the clipboard also contains highlighted HTML', () => {
    const editor = makeEditor('')
    const markdown = '# Heading\n\n**bold** and $x^2$'
    const vscodeHtml = '<div style="color: #ccc"><pre><span style="color: #abc"># Heading</span>\n\n**bold** and $x^2$</pre></div>'

    expect(shouldParsePastedTextAsMarkdown(editor, markdown, vscodeHtml)).toBe(true)
  })

  it('keeps genuine rich text on the HTML paste path', () => {
    const editor = makeEditor('')

    expect(shouldParsePastedTextAsMarkdown(
      editor,
      'Already formatted text',
      '<p><strong>Already formatted</strong> text</p>',
    )).toBe(false)
  })

  it('keeps genuinely formatted web selections on the HTML paste path', () => {
    const editor = makeEditor('')

    expect(shouldParsePastedTextAsMarkdown(
      editor,
      'Heading\nbold link',
      '<h1>Heading</h1><p><strong>bold</strong> <a href="https://example.com">link</a></p>',
    )).toBe(false)
  })
})
