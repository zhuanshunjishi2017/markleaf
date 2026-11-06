import { mathInlineTokenizer, mathBlockTokenizer, normalizeMathSource } from './document/markdown-syntax'
import { InputRule, Node } from '@tiptap/core'
import { Plugin } from '@tiptap/pm/state'
import type { EditorView } from '@tiptap/pm/view'
import { renderMathElement } from './katex-runtime'

type MathNodeContent = { content?: Array<{ text?: string }> }

export function mathNumberFromLatex(latex: string): string | null {
  const match = /\\tag\{([^{}]*)\}\s*$/.exec(latex)
  return match?.[1]?.trim() || null
}

function nodeLatex(node: MathNodeContent): string {
  return node.content?.map(child => child.text ?? '').join('') ?? ''
}

function escapedAt(text: string, offset: number): boolean {
  let backslashes = 0
  for (let index = offset - 1; index >= 0 && text[index] === '\\'; index--) backslashes++
  return backslashes % 2 === 1
}

/**
 * Safari can commit an IME range as `$...$` followed by ordinary text in one
 * composition. The regular input rule only sees the final caret and therefore
 * misses the formula when that text follows its closing delimiter. Revisit the
 * short range touched by the composition and convert the newest closed pair.
 */
function closeInlineMathAfterComposition(view: EditorView, compositionStart: number): void {
  if (view.isDestroyed || !view.editable) return
  const { selection, schema } = view.state
  if (!selection.empty) return
  const $from = selection.$from
  if (!$from.parent.isTextblock || $from.parent.type.spec.code) return

  const parentStart = $from.start($from.depth)
  const cursorOffset = $from.parentOffset
  const compositionOffset = Math.max(0, compositionStart - parentStart)
  const text = $from.parent.textContent
  const closing = text.lastIndexOf('$', cursorOffset - 1)
  if (closing < compositionOffset || closing <= 0 || escapedAt(text, closing)) return
  const opening = text.lastIndexOf('$', closing - 1)
  if (opening < 0 || opening >= closing || escapedAt(text, opening)) return
  if (text[opening - 1] === '$' || text[closing + 1] === '$') return
  const latex = text.slice(opening + 1, closing)
  if (!latex || /[\n$]/.test(latex)) return

  const mathType = schema.nodes.mathInline
  if (!mathType) return
  view.dispatch(view.state.tr.replaceWith(
    parentStart + opening,
    parentStart + closing + 1,
    mathType.create(null, schema.text(normalizeMathSource(latex))),
  ))
}



function renderMathNode(element: HTMLElement, latex: string, displayMode: boolean): void {
  if (latex.length === 0) {
    element.textContent = '...'
    element.classList.add('markleaf-math-placeholder')
    return
  }
  element.classList.remove('markleaf-math-placeholder')
  renderMathElement(element, latex, displayMode)
}

/// 同一帧内请求拟合的块统一处理：逐块拟合时每个块都要「读宽度 → 写测量样式 →
/// 读自然宽度」，每次读取都会强制整篇文档重排，公式一多就是数千次全量重排。
/// 合批后先统一读取可用宽度，再统一进入测量样式、统一读取自然宽度、统一写回，
/// 整批只触发固定次重排，而每个块的判定结果与逐块处理完全一致。
const pendingBlockMathFits = new Set<HTMLElement>()
let blockMathFitHandle = 0

function scheduleBlockMathFit(container: HTMLElement): void {
  pendingBlockMathFits.add(container)
  if (blockMathFitHandle) return
  blockMathFitHandle = requestAnimationFrame(() => {
    blockMathFitHandle = 0
    const batch = [...pendingBlockMathFits]
    pendingBlockMathFits.clear()
    fitBlockMathBatch(batch)
  })
}

type BlockMathMeasurement = {
  container: HTMLElement
  display: string
  width: string
  available: number
}

/// 让块级公式缩放到正好放下：KaTeX 内部全部用 em 单位布局，
/// 缩放容器 font-size 即可按比例缩放整段公式，避免长公式产生横向滚动条。
function fitBlockMathBatch(containers: HTMLElement[]): void {
  const measurements: BlockMathMeasurement[] = []
  for (const container of containers) {
    // 块级容器的可用宽度与自身 font-size 无关，因此在改写样式前读取即可。
    const available = container.clientWidth
    if (available <= 0) continue
    measurements.push({
      container,
      display: container.style.display,
      width: container.style.width,
      available,
    })
  }
  if (measurements.length === 0) return

  for (const item of measurements) {
    item.container.style.fontSize = ''
    item.container.style.display = 'inline-block'
    item.container.style.width = 'max-content'
  }

  // 读取阶段不夹杂任何写入，重排只在这两个循环各发生一次。
  const naturals: number[] = []
  const bases: number[] = []
  for (const item of measurements) {
    const natural = item.container.getBoundingClientRect().width
    naturals.push(natural)
    bases.push(
      natural > item.available
        ? Number.parseFloat(getComputedStyle(item.container).fontSize) || 16
        : 0,
    )
  }

  for (const [index, item] of measurements.entries()) {
    item.container.style.display = item.display
    item.container.style.width = item.width
    const natural = naturals[index]!
    if (natural <= item.available) continue
    item.container.style.fontSize = `${((bases[index]! * item.available) / natural).toFixed(2)}px`
  }
}

/// 行内数学公式：`$...$` 或 `\(...\)`。
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
    const latex = normalizeMathSource(token.text ?? '')
    return helpers.createNode(
      'mathInline',
      null,
      latex ? [helpers.createTextNode(latex)] : [],
    )
  },

  renderMarkdown(node) {
    // Inline math must remain on one Markdown line; otherwise the closing
    // delimiter no longer parses as part of the inline formula. This is the
    // sole normalization allowed for inline formulas. Block math remains raw.
    const latex = nodeLatex(node).replace(/\r?\n/g, '')
    return `$${latex || '...'}$`
  },

  markdownTokenizer: mathInlineTokenizer,

  addInputRules() {
    return [
      new InputRule({
        // The first `$` must not be the second half of a display-math opener.
        // Otherwise `$$x$` is incorrectly converted to inline math before the
        // second closing `$` can complete the block formula.
        find: /(?<!\$)\$([^$\n]+?)\$(?!\$)$/,
        handler: ({ state, range, match }) => {
          const latex = normalizeMathSource(match[1] ?? '')
          const mathType = state.schema.nodes.mathInline
          if (!mathType) return null
          state.tr.replaceWith(
            range.from,
            range.to,
            mathType.create(null, state.schema.text(latex)),
          )
        },
      }),
      new InputRule({
        find: /\\\(((?:\\(?!\))|[^\\\n])*?)\\\)$/,
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

  addProseMirrorPlugins() {
    let compositionStart: number | null = null
    return [new Plugin({
      props: {
        handleDOMEvents: {
          compositionstart(view) {
            compositionStart = view.state.selection.from
            return false
          },
          compositionend(view) {
            const start = compositionStart
            compositionStart = null
            if (start === null) return false
            window.setTimeout(() => closeInlineMathAfterComposition(view, start), 0)
            return false
          },
        },
      },
    })]
  },

  addNodeView() {
    return ({ node }) => {
      const span = document.createElement('span')
      span.className = 'markleaf-math markleaf-math-inline'
      span.contentEditable = 'false'
      renderMathNode(span, node.textContent, false)
      return { dom: span }
    }
  },
})

/// 块级数学公式：`$$...$$` 或 `\[...\]`。
export const MathBlock = Node.create({
  name: 'mathBlock',
  group: 'block',
  atom: true,
  selectable: true,
  content: 'text*',

  addAttributes() {
    return {
      number: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-math-number'),
        renderHTML: (attributes: Record<string, unknown>) => ({
          'data-math-number': attributes.number ?? null,
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'div[data-math-block]' }]
  },

  renderHTML({ node, HTMLAttributes }) {
    return ['div', { 'data-math-block': '1', ...HTMLAttributes }, node.textContent]
  },

  parseMarkdown(token, helpers) {
    const latex = normalizeMathSource(token.text ?? '')
    return helpers.createNode(
      'mathBlock',
      { number: null },
      latex ? [helpers.createTextNode(latex)] : [],
    )
  },

  renderMarkdown(node) {
    const body = nodeLatex(node)
    return `$$${body || '...'}$$`
  },

  markdownTokenizer: mathBlockTokenizer,

  addInputRules() {
    return [
      new InputRule({
        find: /\$\$([^$]+?)\$\$$/,
        handler: ({ state, range, match }) => {
          const latex = normalizeMathSource(match[1] ?? '')
          const mathType = state.schema.nodes.mathBlock
          if (!mathType) return null
          state.tr.replaceRangeWith(
            range.from,
            range.to,
            mathType.create(null, state.schema.text(latex)),
          )
        },
      }),
      new InputRule({
        find: /\\\[([\s\S]+?)\\\]$/,
        handler: ({ state, range, match }) => {
          const latex = normalizeMathSource(match[1]!)
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

      const render = (currentNode: { textContent: string; attrs?: Record<string, unknown> }) => {
        renderMathNode(div, currentNode.textContent, true)
      }
      render(node)

      const fit = () => scheduleBlockMathFit(div)
      let lastWidth = -1
      const observer = new ResizeObserver((entries) => {
        const width = entries[0]?.contentRect.width ?? 0
        // 仅响应宽度变化，避免 font-size 调整引起的高度变化触发无限重排。
        if (width === lastWidth) return
        lastWidth = width
        fit()
      })
      observer.observe(div)
      fit()

      return {
        dom: div,
        update: (updatedNode: { type: { name: string }; textContent: string; attrs?: Record<string, unknown> }) => {
          if (updatedNode.type.name !== 'mathBlock') return false
          render(updatedNode)
          fit()
          return true
        },
        destroy: () => {
          pendingBlockMathFits.delete(div)
          observer.disconnect()
        },
      }
    }
  },
})
