import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createEditor,
  executeEditorCommand,
  getMarkdown,
  setEditorSharedStrings,
} from '../src/editor'
import { renderMermaidInHtml, setMermaidStrings } from '../src/mermaid'
import { sharedEditorStrings } from '../src/shared-editor-strings'

const editors: ReturnType<typeof createEditor>[] = []

if (!globalThis.ResizeObserver) {
  class ResizeObserverStub {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }
  globalThis.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

afterEach(() => {
  for (const editor of editors.splice(0)) {
    editor.destroy()
  }
  document.body.innerHTML = ''
  const defaults = sharedEditorStrings('zh-Hans', 'ctrl')
  setEditorSharedStrings(defaults)
  setMermaidStrings(defaults)
})

function mount(markdown: string) {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, markdown)
  editors.push(editor)
  return editor
}

describe('Mermaid chart support', () => {
  it('parses a fenced mermaid block into its own node', () => {
    const editor = mount('```mermaid\ngraph TD\n  A-->B\n```')

    const nodes: string[] = []
    editor.state.doc.descendants((node) => {
      nodes.push(node.type.name)
    })
    expect(nodes).toContain('mermaid')
    expect(nodes).not.toContain('codeBlock')
    expect(getMarkdown(editor)).toBe('```mermaid\ngraph TD\n  A-->B\n```')
  })

  it('round-trips mermaid source without HTML-escaped characters', () => {
    const editor = mount('前文\n\n```mermaid\nsequenceDiagram\n  A->>B: hi\n```\n\n后文')
    expect(getMarkdown(editor)).toContain('A->>B: hi')
    expect(getMarkdown(editor)).not.toContain('&gt;')
  })

  it('keeps mermaid source untouched when the document mentions ```mermaid inline', () => {
    const editor = mount([
      '说明：` ```mermaid ` 围栏会被渲染。',
      '',
      '## 状态图',
      '',
      '```mermaid',
      'stateDiagram-v2',
      '  [*] --> 待渲染',
      '  成功 --> [*]',
      '```',
    ].join('\n'))

    const markdown = getMarkdown(editor)
    expect(markdown).toContain('[*] --> 待渲染')
    expect(markdown).toContain('成功 --> [*]')
    expect(markdown).not.toContain('<em>')
    expect(markdown).not.toContain('&gt;')
  })

  it('keeps non-mermaid fenced code blocks as regular codeBlock nodes', () => {
    const editor = mount('```js\nconst leaf = 1\n```')

    const nodes: string[] = []
    editor.state.doc.descendants((node) => {
      nodes.push(node.type.name)
    })
    expect(nodes).toContain('codeBlock')
    expect(nodes).not.toContain('mermaid')
    expect(getMarkdown(editor)).toBe('```js\nconst leaf = 1\n```')
  })

  it('serializes an empty mermaid block back to an empty fence', () => {
    const editor = mount('```mermaid\n```')
    expect(editor.state.doc.firstChild?.type.name).toBe('mermaid')
    expect(getMarkdown(editor)).toBe('```mermaid\n\n```')
  })

  it('inserts an empty mermaid code block at the cursor', () => {
    const editor = mount('before')
    editor.commands.setTextSelection(4)

    expect(executeEditorCommand(editor, 'insertMermaid')).toBe(true)
    expect(editor.state.selection.$from.parent.type.name).toBe('codeBlock')
    expect(editor.state.selection.$from.parent.attrs.language).toBe('mermaid')
    expect(editor.state.selection.$from.parent.textContent).toBe('')
    expect(getMarkdown(editor)).toBe('bef\n\n```mermaid\n\n```\n\nore')
  })

  it('round-trips mermaid through serialized HTML without losing source', () => {
    const editor = mount('前文\n\n```mermaid\ngraph TD\n  A-->B\n```\n\n后文')
    const html = editor.getHTML()
    expect(html).toContain('data-mermaid="1"')

    const reloaded = createEditor(document.createElement('div'), html)
    editors.push(reloaded)
    expect(reloaded.state.doc.child(1)?.type.name).toBe('mermaid')
    expect(getMarkdown(reloaded)).toContain('```mermaid\ngraph TD\n  A-->B\n```')
  })

  it('leaves export placeholders intact when mermaid cannot render in the test DOM', async () => {
    const editor = mount('```mermaid\ngraph TD\n  A-->B\n```')
    const html = await renderMermaidInHtml(editor.getHTML())
    expect(html).toContain('markleaf-mermaid')
    expect(html).toContain('Mermaid 图表文本格式错误')
    expect(html).not.toContain('graph TD')
  })

  it('renders a localized error message when mermaid render fails (jsdom)', async () => {
    const editor = mount('```mermaid\ngraph TD\n  A-->B\n```')
    const element = editor.view.dom.querySelector('.markleaf-mermaid') as HTMLElement
    expect(element).not.toBeNull()

    await vi.waitFor(() => {
      expect(element.querySelector('.markleaf-mermaid-message-error')?.textContent).toBe('Mermaid 图表文本格式错误')
    })
    expect(element.textContent).not.toContain('graph TD')
  })

  it('renders an empty message for empty mermaid blocks', async () => {
    const editor = mount('```mermaid\n```')
    const element = editor.view.dom.querySelector('.markleaf-mermaid') as HTMLElement
    expect(element).not.toBeNull()

    await vi.waitFor(() => {
      expect(element.querySelector('.markleaf-mermaid-message-empty')?.textContent).toBe('空 Mermaid 图表')
    })

    const html = await renderMermaidInHtml(editor.getHTML())
    expect(html).toContain('空 Mermaid 图表')
  })

  it('uses the active language for Mermaid controls, empty state, and export errors', async () => {
    const strings = sharedEditorStrings('en', 'meta')
    setEditorSharedStrings(strings)
    setMermaidStrings(strings)

    const emptyEditor = mount('```mermaid\n```')
    const emptyElement = emptyEditor.view.dom.querySelector('.markleaf-mermaid') as HTMLElement
    await vi.waitFor(() => {
      expect(emptyElement.querySelector('.markleaf-mermaid-message-empty')?.textContent)
        .toBe('Empty Mermaid diagram')
    })

    const codeEditor = mount('```\ngraph TD\n  A-->B\n```')
    expect(executeEditorCommand(codeEditor, 'setCodeBlockLanguage', 'mermaid')).toBe(true)
    expect(Array.from(codeEditor.view.dom.querySelectorAll<HTMLButtonElement>('button'))
      .some(button => button.textContent === 'Render as Diagram')).toBe(true)

    const failedExport = await renderMermaidInHtml(
      mount('```mermaid\ngraph TD\n  A-->B\n```').getHTML(),
    )
    expect(failedExport).toContain('Invalid Mermaid diagram text')
    expect(failedExport).not.toContain('Mermaid图表文本格式错误')
  })

  it('edits mermaid source as a regular code block and renders it back', async () => {
    const editor = mount('```mermaid\ngraph TD\n  A-->B\n```')
    editor.commands.setNodeSelection(0)

    expect(executeEditorCommand(editor, 'editMermaid')).toBe(true)
    expect(editor.state.doc.firstChild?.type.name).toBe('codeBlock')
    expect(editor.state.doc.firstChild?.attrs.language).toBe('mermaid')
    expect(editor.view.dom.querySelector('pre code')?.textContent).toContain('A-->B')

    Array.from(editor.view.dom.querySelectorAll<HTMLButtonElement>('button'))
      .find(button => button.textContent === '渲染为图表')
      ?.click()

    expect(editor.state.doc.firstChild?.type.name).toBe('mermaid')
    expect(getMarkdown(editor)).toContain('```mermaid\ngraph TD\n  A-->B\n```')
  })

  it('shows the render button after declaring a code block as mermaid', () => {
    const editor = mount('```\ngraph TD\n  A-->B\n```')

    expect(executeEditorCommand(editor, 'setCodeBlockLanguage', 'mermaid')).toBe(true)
    expect(editor.state.doc.firstChild?.attrs.language).toBe('mermaid')
    expect(Array.from(editor.view.dom.querySelectorAll<HTMLButtonElement>('button'))
      .some(button => button.textContent === '渲染为图表')).toBe(true)
  })

  it('toggles visual code block highlighting', () => {
    const editor = mount('```ts\nconst answer = 42\n```')

    expect(editor.view.dom.querySelector('.ml-code-keyword')).toBeNull()
    expect(executeEditorCommand(editor, 'setCodeHighlightVisible', '1')).toBe(true)
    expect(editor.view.dom.querySelector('.ml-code-keyword')?.textContent).toBe('const')
    expect(editor.view.dom.querySelector('.ml-code-number')?.textContent).toBe('42')
    expect(executeEditorCommand(editor, 'setCodeHighlightVisible', '0')).toBe(true)
    expect(editor.view.dom.querySelector('.ml-code-keyword')).toBeNull()
  })
})
