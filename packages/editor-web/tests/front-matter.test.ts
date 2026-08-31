import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getEditorCommandState, getEditorStatus, getMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(markdown: string) {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, markdown)
  editors.push(editor)
  return { editor, element }
}

describe('YAML front matter', () => {
  it('preserves leading front matter without normalizing its contents', () => {
    const markdown = [
      '---',
      'title: "MarkLeaf"',
      'tags: [markdown, editor]',
      '# keep this comment',
      'draft: false',
      '---',
      '',
      '# 正文',
      '',
      '内容',
    ].join('\n')
    const { editor, element } = makeEditor(markdown)

    expect(element.querySelector('.markleaf-front-matter')).not.toBeNull()
    expect(element.querySelector('.markleaf-front-matter')?.classList.contains('markleaf-front-matter-collapsed')).toBe(true)
    expect(element.querySelector('.markleaf-front-matter-toggle-icon')?.textContent).toBe('\ue946')
    expect(element.querySelector('.markleaf-front-matter-toggle-error')).not.toBeNull()
    expect(element.querySelector('.ProseMirror > p .markleaf-front-matter-toggle')).toBeNull()
    expect(element.querySelector('.markleaf-front-matter-code')?.tagName).toBe('PRE')
    expect(element.querySelector<HTMLElement>('.markleaf-front-matter-editor')?.textContent)
      .toBe('title: "MarkLeaf"\ntags: [markdown, editor]\n# keep this comment\ndraft: false')
    expect(getMarkdown(editor)).toBe(markdown)
  })

  it('expands from the document information icon and collapses from Hide', () => {
    const { element } = makeEditor('---\ntitle: MarkLeaf\n---\n\n正文')
    const container = element.querySelector('.markleaf-front-matter')
    const toggle = element.querySelector<HTMLButtonElement>('.markleaf-front-matter-toggle')
    const hide = element.querySelector<HTMLButtonElement>('.markleaf-front-matter-hide')

    toggle?.click()
    expect(container?.classList.contains('markleaf-front-matter-collapsed')).toBe(false)

    hide?.click()
    expect(container?.classList.contains('markleaf-front-matter-collapsed')).toBe(true)
  })

  it('keeps the panel expanded after ProseMirror processes DOM mutations', async () => {
    const { element } = makeEditor('---\ntitle: MarkLeaf\n---\n\n正文')
    const container = element.querySelector('.markleaf-front-matter')
    const toggle = element.querySelector<HTMLButtonElement>('.markleaf-front-matter-toggle')
    const mouseDown = new MouseEvent('mousedown', { bubbles: true, cancelable: true })

    toggle?.dispatchEvent(mouseDown)
    toggle?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
    await new Promise(resolve => setTimeout(resolve, 0))

    expect(mouseDown.defaultPrevented).toBe(true)
    expect(container?.classList.contains('markleaf-front-matter-collapsed')).toBe(false)
    expect(element.querySelector('.markleaf-front-matter-editor')?.textContent).toBe('title: MarkLeaf')
  })

  it('keeps an empty front matter line until that line is deleted', () => {
    const { editor, element } = makeEditor('---\ntitle: MarkLeaf\n---\n\n正文')
    const frontMatter = editor.state.doc.firstChild

    editor.commands.deleteRange({ from: 1, to: frontMatter!.nodeSize - 1 })

    expect(element.querySelector('.markleaf-front-matter')).not.toBeNull()
    expect(getMarkdown(editor)).toBe('---\n\n---\n\n正文')

    editor.commands.setTextSelection(1)
    expect(editor.commands.keyboardShortcut('Backspace')).toBe(true)
    expect(element.querySelector('.markleaf-front-matter')).toBeNull()
  })

  it('creates or opens front matter from the editor command', () => {
    const empty = makeEditor('正文')
    expect(executeEditorCommand(empty.editor, 'showFrontMatter')).toBe(true)
    expect(empty.element.querySelector('.markleaf-front-matter')?.classList.contains('markleaf-front-matter-collapsed')).toBe(false)
    expect(empty.editor.state.selection.from).toBe(1)
    expect(getMarkdown(empty.editor)).toBe('---\n\n---\n\n正文')

    const existing = makeEditor('---\ntitle: MarkLeaf\n---\n\n正文')
    expect(executeEditorCommand(existing.editor, 'showFrontMatter')).toBe(true)
    expect(existing.element.querySelector('.markleaf-front-matter')?.classList.contains('markleaf-front-matter-collapsed')).toBe(false)
    expect(existing.editor.state.selection.from).toBe(1)

    existing.editor.commands.setTextSelection(existing.editor.state.doc.content.size - 1)
    expect(executeEditorCommand(existing.editor, 'showFrontMatter')).toBe(true)
    expect(existing.editor.state.selection.from).toBe(1)
  })

  it('reports code-menu state and exits to the document body', () => {
    const { editor } = makeEditor('---\ntitle: MarkLeaf\n---\n\n正文')
    editor.commands.setTextSelection(1)

    const state = getEditorCommandState(editor)
    expect(state.frontMatter).toBe(true)
    expect(state.codeBlock).toBe(false)
    expect(state.codeBlockLanguage).toBeNull()
    expect(state.codeBlockText).toBe('title: MarkLeaf')

    expect(executeEditorCommand(editor, 'exitCode')).toBe(true)
    expect(editor.state.selection.$from.parent.type.name).toBe('paragraph')
  })

  it('does not treat horizontal rules later in the document as front matter', () => {
    const markdown = '正文\n\n---\n\n后文'
    const { editor, element } = makeEditor(markdown)

    expect(element.querySelector('.markleaf-front-matter')).toBeNull()
    expect(element.querySelector('hr')).not.toBeNull()
    expect(getMarkdown(editor)).toBe(markdown)
  })

  it('keeps invalid YAML editable and reports it without discarding content', () => {
    const markdown = '---\ntitle: [invalid\n---\n\n正文'
    const { editor, element } = makeEditor(markdown)
    const details = element.querySelector('.markleaf-front-matter')
    const toggle = element.querySelector<HTMLButtonElement>('.markleaf-front-matter-toggle')

    expect(details?.classList.contains('markleaf-front-matter-invalid')).toBe(true)
    expect(toggle?.title).toBe('YAML格式错误')
    expect(toggle?.querySelector('.markleaf-front-matter-toggle-error')?.textContent).toBe('YAML格式错误')
    expect(getMarkdown(editor)).toBe(markdown)
  })

  it('highlights YAML syntax and marks mismatched brackets as invalid', () => {
    const { editor, element } = makeEditor('---\ntitle: "MarkLeaf"\ndraft: false\ntags: [markdown\n---\n\n正文')
    expect(executeEditorCommand(editor, 'setCodeHighlightVisible', '1')).toBe(true)

    expect(element.querySelector('.markleaf-front-matter-editor .ml-code-property')?.textContent).toBe('title')
    expect(element.querySelector('.markleaf-front-matter-editor .ml-code-string')?.textContent).toBe('"MarkLeaf"')
    expect(element.querySelector('.markleaf-front-matter-editor .ml-code-keyword')?.textContent).toBe('false')
    expect(element.querySelector('.markleaf-front-matter-editor .ml-code-invalid')?.textContent).toBe('[')
  })

  it('excludes front matter from document statistics', () => {
    const { editor } = makeEditor('---\ntitle: metadata words\nauthor: 作者\n---\n\n正文 body')
    const status = getEditorStatus(editor)

    expect(status.characterCount).toBe(6)
    expect(status.westernWordCount).toBe(1)
    expect(status.cjkCharacterCount).toBe(2)
    expect(status.paragraphCount).toBe(1)
  })
})
