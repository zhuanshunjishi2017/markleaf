import { InputRule, Node } from '@tiptap/core'
import katex from 'katex'
import 'katex/dist/katex.min.css'
import katexSelfContainedCss from 'virtual:katex-css'

type MathNodeContent = { content?: Array<{ text?: string }> }

function nodeLatex(node: MathNodeContent): string {
  return node.content?.map(child => child.text ?? '').join('') ?? ''
}

function renderKatex(element: HTMLElement, latex: string, displayMode: boolean): void {
  try {
    katex.render(latex, element, {
      displayMode,
      throwOnError: true,
      strict: false,
      trust: false,
    })
  } catch {
    // 解析失败时回退为原始 LaTeX 文本，避免内容不可见。
    element.textContent = latex
  }
}

/// 行内数学公式：`$...$`
export const MathInline = Node.create({
  name: 'mathInline',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  content: 'text*',

  parseHTML() {
    return [{ tag: 'span[data-math-inline]' }]
  },

  renderHTML({ node }) {
    return ['span', { 'data-math-inline': '1' }, node.textContent]
  },

  parseMarkdown(token, helpers) {
    return helpers.createNode('mathInline', null, [
      helpers.createTextNode((token.text ?? '').trim()),
    ])
  },

  renderMarkdown(node) {
    return `$${nodeLatex(node)}$`
  },

  markdownTokenizer: {
    name: 'mathInline',
    level: 'inline',
    start: (src: string) => src.indexOf('$'),
    tokenize: (src: string) => {
      const match = /^\$(?!\$)([^$\n]+?)\$/.exec(src)
      if (!match) return undefined
      return { type: 'mathInline', raw: match[0], text: match[1] }
    },
  },

  addInputRules() {
    return [
      new InputRule({
        find: /\$([^$\n]+?)\$$/,
        handler: ({ state, range, match }) => {
          const latex = match[1]!
          const mathType = state.schema.nodes.mathInline
          if (!mathType) return null
          state.tr.replaceWith(
            range.from,
            range.to,
            mathType.create(null, state.schema.text(latex)),
          )
        },
      }),
    ]
  },

  addNodeView() {
    return ({ node }) => {
      const span = document.createElement('span')
      span.className = 'markleaf-math markleaf-math-inline'
      span.contentEditable = 'false'
      renderKatex(span, node.textContent, false)
      return { dom: span }
    }
  },
})

/// 块级数学公式：`$$...$$`
export const MathBlock = Node.create({
  name: 'mathBlock',
  group: 'block',
  atom: true,
  selectable: true,
  content: 'text*',

  parseHTML() {
    return [{ tag: 'div[data-math-block]' }]
  },

  renderHTML({ node }) {
    return ['div', { 'data-math-block': '1' }, node.textContent]
  },

  parseMarkdown(token, helpers) {
    return helpers.createNode('mathBlock', null, [
      helpers.createTextNode((token.text ?? '').trim()),
    ])
  },

  renderMarkdown(node) {
    return `$$${nodeLatex(node)}$$`
  },

  markdownTokenizer: {
    name: 'mathBlock',
    level: 'block',
    start: (src: string) => src.indexOf('$$'),
    tokenize: (src: string) => {
      const match = /^\$\$([\s\S]+?)\$\$/.exec(src)
      if (!match) return undefined
      return { type: 'mathBlock', raw: match[0], text: match[1]!.trim() }
    },
  },

  addInputRules() {
    return [
      new InputRule({
        find: /\$\$([^$]+?)\$\$$/,
        handler: ({ state, range, match }) => {
          const latex = match[1]!
          const mathType = state.schema.nodes.mathBlock
          if (!mathType) return null
          state.tr.replaceRangeWith(
            range.from,
            range.to,
            mathType.create(null, state.schema.text(latex)),
          )
        },
      }),
    ]
  },

  addNodeView() {
    return ({ node }) => {
      const div = document.createElement('div')
      div.className = 'markleaf-math markleaf-math-block'
      div.contentEditable = 'false'
      renderKatex(div, node.textContent, true)
      return { dom: div }
    }
  },
})

/// 自包含的 KaTeX CSS（woff2 字体内联为 base64），用于导出 HTML/PDF。
export const katexCss = katexSelfContainedCss

function decodeHtmlEntities(text: string): string {
  const textarea = document.createElement('textarea')
  textarea.innerHTML = text
  return textarea.value
}

/// 将 `editor.getHTML()` 输出中的数学标记替换为 KaTeX 渲染结果。
/// 导出 HTML 由独立 WebView2 加载，无法复用编辑器的 KaTeX 运行时，因此需预渲染。
export function renderMathInHtml(html: string): string {
  return html
    .replace(/<span data-math-inline="1">([\s\S]*?)<\/span>/g, (_, latex: string) =>
      katex.renderToString(decodeHtmlEntities(latex), { throwOnError: false }),
    )
    .replace(/<div data-math-block="1">([\s\S]*?)<\/div>/g, (_, latex: string) =>
      katex.renderToString(decodeHtmlEntities(latex), { displayMode: true, throwOnError: false }),
    )
}
