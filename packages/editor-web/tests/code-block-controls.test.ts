import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createEditor,
  executeEditorCommand,
  getMarkdown,
  setCodeBlockControlHandlers,
} from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  setCodeBlockControlHandlers({})
  document.body.innerHTML = ''
})

function mount(markdown: string) {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, markdown)
  editors.push(editor)
  return editor
}

function controls(editor: ReturnType<typeof createEditor>): HTMLElement {
  const element = editor.view.dom.querySelector<HTMLElement>('.markleaf-code-block-controls')
  expect(element).not.toBeNull()
  return element!
}

describe('code block controls', () => {
  it('shows the language and a copy control for a fenced code block', () => {
    const editor = mount('```swift\nlet value = 1\n```')
    const element = controls(editor)

    expect(element.querySelector('.markleaf-code-block-copy')).not.toBeNull()
    expect(element.querySelector('.markleaf-code-block-language')?.textContent).toBe('swift')
  })

  it('shows an empty language placeholder when no language is specified', () => {
    const editor = mount('```\nplain\n```')
    const language = controls(editor).querySelector('.markleaf-code-block-language')

    expect(language?.textContent).toBe('')
    expect(language?.classList.contains('markleaf-code-block-language-empty')).toBe(true)
  })

  it('requests language editing for the exact code block position', () => {
    const editLanguage = vi.fn()
    setCodeBlockControlHandlers({ editLanguage })
    const editor = mount('before\n\n```swift\nlet value = 1\n```')
    const language = controls(editor).querySelector<HTMLElement>('.markleaf-code-block-language')!

    language.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    expect(editLanguage).toHaveBeenCalledTimes(1)
    expect(editLanguage).toHaveBeenCalledWith(expect.any(Number), 'swift')
  })

  it('copies the exact code block text', () => {
    const copyCode = vi.fn()
    setCodeBlockControlHandlers({ copyCode })
    const editor = mount('```js\nconst value = 1\n```')
    const copy = controls(editor).querySelector<HTMLElement>('.markleaf-code-block-copy')!

    copy.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    expect(copyCode).toHaveBeenCalledWith('const value = 1')
  })

  it('updates a code block language by position', () => {
    const editor = mount('```js\nconst value = 1\n```')

    expect(executeEditorCommand(
      editor,
      'setCodeBlockLanguageAt',
      JSON.stringify({ position: 0, language: 'typescript' }),
    )).toBe(true)
    expect(getMarkdown(editor)).toContain('```typescript\nconst value = 1\n```')
  })

  it('inserts a code block with the selected language', () => {
    const editor = mount('paragraph')

    expect(executeEditorCommand(editor, 'insertCodeBlockWithLanguage', 'swift')).toBe(true)
    expect(getMarkdown(editor)).toContain('```swift\nparagraph\n```')
  })

  it('inserts an unspecified code fence when no language is selected', () => {
    const editor = mount('paragraph')

    expect(executeEditorCommand(editor, 'insertCodeBlockWithLanguage', '')).toBe(true)
    expect(getMarkdown(editor)).toContain('```\nparagraph\n```')
  })
})
