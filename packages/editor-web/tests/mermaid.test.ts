import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, getMarkdown } from '../src/editor'
import { renderMermaidInHtml } from '../src/mermaid'

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
  })

  it('renders a source fallback when mermaid render fails (jsdom)', async () => {
    const editor = mount('```mermaid\ngraph TD\n  A-->B\n```')
    const element = editor.view.dom.querySelector('.markleaf-mermaid') as HTMLElement
    expect(element).not.toBeNull()

    await vi.waitFor(() => {
      expect(element.querySelector('.markleaf-mermaid-fallback')).not.toBeNull()
    })
  })
})
