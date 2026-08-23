import { Node, mergeAttributes } from '@tiptap/core'

type MermaidModule = typeof import('mermaid')

let mermaidPromise: Promise<MermaidModule> | null = null
let mermaidInitialized = false
let mermaidSequence = 0

async function loadMermaid(): Promise<MermaidModule> {
  mermaidPromise ??= import('mermaid')
  return mermaidPromise
}

async function ensureMermaidInitialized(module: MermaidModule): Promise<void> {
  if (mermaidInitialized) return
  module.default.initialize({
    startOnLoad: false,
    securityLevel: 'strict',
    themeVariables: {
      fontFamily: 'var(--font-sans, sans-serif)',
    },
  })
  mermaidInitialized = true
}

function nextMermaidId(prefix: string): string {
  mermaidSequence += 1
  return `${prefix}-${mermaidSequence}`
}

async function renderMermaidSvgInto(host: HTMLElement, source: string): Promise<boolean> {
  const module = await loadMermaid()
  await ensureMermaidInitialized(module)
  const id = nextMermaidId('markleaf-mermaid')
  return module.default.render(id, source).then(({ svg }) => {
    host.innerHTML = svg
    return true
  }).catch(() => false)
}

function renderMermaidSourceFallback(host: HTMLElement, source: string): void {
  const pre = document.createElement('pre')
  pre.className = 'markleaf-mermaid-fallback'
  const code = document.createElement('code')
  code.textContent = source
  pre.append(code)
  host.replaceChildren(pre)
}

const MermaidNodeView = ({ node }: { node: { textContent: string } }) => {
  const wrapper = document.createElement('div')
  wrapper.className = 'markleaf-mermaid'
  wrapper.contentEditable = 'false'

  let lastSource = node.textContent
  const section = document.createElement('div')
  section.className = 'markleaf-mermaid-view'
  wrapper.append(section)
  void renderMermaidSvgInto(section, lastSource).then((ok) => {
    if (!ok && section.isConnected) {
      renderMermaidSourceFallback(section, lastSource)
    }
  })

  return {
    dom: wrapper,
    update: (updated: { type?: { name: string }; textContent: string }) => {
      if (updated.type?.name !== 'mermaid') return false
      const source = updated.textContent
      if (source === lastSource) return true
      lastSource = source
      section.replaceChildren()
      void renderMermaidSvgInto(section, source).then((ok) => {
        if (!ok && section.isConnected) {
          renderMermaidSourceFallback(section, source)
        }
      })
      return true
    },
  }
}

export const Mermaid = Node.create({
  name: 'mermaid',
  priority: 1000,
  group: 'block',
  code: true,
  atom: true,
  selectable: true,
  content: 'text*',

  parseHTML() {
    return [{ tag: 'div[data-mermaid]' }]
  },

  renderHTML({ node }: any) {
    return [
      'div',
      mergeAttributes({ class: 'markleaf-mermaid', 'data-mermaid': '1' }),
      node.textContent,
    ]
  },

  markdownTokenName: 'code',

  parseMarkdown(token: any, helpers: any) {
    if (token.lang !== 'mermaid') return null
    if (!token.raw?.startsWith('```') && !token.raw?.startsWith('~~~')) return null
    return helpers.createNode(
      'mermaid',
      null,
      token.text ? [helpers.createTextNode(token.text)] : [],
    )
  },

  renderMarkdown(node: any, helpers: any) {
    const hasContent = Array.isArray(node.content) && node.content.length > 0
    const source = hasContent ? helpers.renderChildren(node.content) : ''
    return source.length > 0
      ? ['```mermaid', source, '```'].join('\n')
      : '```mermaid\n\n```'
  },

  addNodeView() {
    return MermaidNodeView
  },
})

export async function renderMermaidInHtml(html: string): Promise<string> {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  const placeholders = Array.from(parsed.body.querySelectorAll<HTMLElement>('.markleaf-mermaid[data-mermaid="1"]'))
  if (placeholders.length === 0) return html

  const module = await loadMermaid()
  await ensureMermaidInitialized(module)
  await Promise.all(placeholders.map(async (placeholder) => {
    const source = placeholder.textContent ?? ''
    if (!source.trim()) {
      placeholder.classList.add('markleaf-mermaid-error')
      return
    }

    try {
      const id = nextMermaidId('markleaf-export-mermaid')
      const { svg } = await module.default.render(id, source)
      const host = parsed.createElement('div')
      host.className = 'markleaf-mermaid markleaf-mermaid-export'
      host.innerHTML = svg
      placeholder.replaceWith(host)
    } catch {
      placeholder.classList.add('markleaf-mermaid-error')
    }
  }))

  return parsed.body.innerHTML
}
