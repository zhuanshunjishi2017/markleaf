import type { EditorView } from '@tiptap/pm/view'
import type { DiagnosticReason, ParagraphSnapshot, StyleRun } from '@markleaf/typography'
import { graphemeOffsets, supportsTypographyText, typographySpacingRules } from '@markleaf/typography'

export type ProjectionSnapshot = ParagraphSnapshot & Readonly<{
  nodeFrom: number; nodeTo: number; segmentFrom: number; segmentTo: number
}>
export type SnapshotResult =
  | { eligible: true; snapshot: ProjectionSnapshot }
  | { eligible: false; nodeFrom: number; nodeTo: number; reason: DiagnosticReason }

export const typographyGeometryProperties = [
  'font-family', 'font-size', 'font-weight', 'font-style', 'font-stretch', 'font-kerning',
  'font-feature-settings', 'font-variation-settings', 'font-variant', 'letter-spacing',
  'word-spacing', 'text-transform', 'line-height',
] as const

export function typographyRunStyle(element: Element): Readonly<Record<string, string>> {
  const computed = element.ownerDocument.defaultView!.getComputedStyle(element)
  const css = Object.fromEntries(typographyGeometryProperties.map(p => {
    let value = computed.getPropertyValue(p), ancestor = element.parentElement
    // CSSOM in non-layout consumers may omit inherited values. Never treat an
    // empty inherited font as an independent font run.
    while (!value && ancestor) { value = ancestor.ownerDocument.defaultView!.getComputedStyle(ancestor).getPropertyValue(p); ancestor = ancestor.parentElement }
    return [p, value]
  }))
  const owned = element.closest('[data-markleaf-typography-natural-space]')
  if (owned) css['word-spacing'] = owned.getAttribute('data-markleaf-typography-natural-space') ?? ''
  return Object.freeze(css)
}

export function extractProjectionSnapshots(view: EditorView, revision: number, fontEpoch: number): readonly SnapshotResult[] {
  const results: SnapshotResult[] = []
  const cjkAutoSpacing = view.dom.ownerDocument.documentElement.classList.contains('markleaf-cjk-autospace')
  view.state.doc.descendants((node, pos) => {
    if (!node.isTextblock) return true
    const fail = (reason: DiagnosticReason) => results.push({ eligible: false, nodeFrom: pos, nodeTo: pos + node.nodeSize, reason })
    if (node.type.name !== 'paragraph' || view.state.doc.resolve(pos).depth !== 0) {
      fail('unsupported-block'); return false
    }
    const element = view.nodeDOM(pos) as HTMLElement | null
    if (!element) { fail('unsupported-block'); return false }
    const computed = element.ownerDocument.defaultView!.getComputedStyle(element)
    const explicitAlign = String(node.attrs.textAlign ?? node.attrs.alignment ?? element.style.textAlign ?? '')
    if (explicitAlign && explicitAlign !== 'justify') { fail('author-alignment'); return false }
    if (computed.direction === 'rtl' || /vertical/.test(computed.writingMode)) { fail('unsupported-text'); return false }
    let unsupported = false
    node.forEach(child => {
      if ((!child.isText && child.type.name !== 'hardBreak') || child.marks.some(m => m.type.name === 'code')) unsupported = true
    })
    if (unsupported) { fail('unsupported-block'); return false }
    // Markdown soft breaks can live inside a text node. They are explicit
    // visual line boundaries, just like hardBreak nodes, not shaping input.
    if (!supportsTypographyText(node.textContent.replace(/\n/g, ''))) { fail('unsupported-text'); return false }
    const width = element.getBoundingClientRect().width - ['padding-left', 'padding-right', 'border-left-width', 'border-right-width']
      .reduce((n, p) => n + (parseFloat(computed.getPropertyValue(p)) || 0), 0)
    const language = element.closest('[lang]')?.getAttribute('lang') ?? 'zh'
    let text = '', segmentFrom = pos + 1
    let runs: StyleRun[] = []
    const finish = (segmentTo: number) => {
      const id = `${pos}:${segmentFrom}`
      const environmentKey = JSON.stringify([text, runs, width, language, fontEpoch, typographySpacingRules.version, view.dom.ownerDocument.defaultView!.devicePixelRatio, cjkAutoSpacing])
      const snapshot: ProjectionSnapshot = Object.freeze({
        id, revision, environmentKey, text, width, language, direction: 'ltr', cjkAutoSpacing,
        alignment: explicitAlign === 'justify' ? 'justify' : 'default',
        graphemeOffsets: Object.freeze([...graphemeOffsets(text)]), runs: Object.freeze(runs),
        nodeFrom: pos, nodeTo: pos + node.nodeSize, segmentFrom, segmentTo,
      })
      results.push({ eligible: true, snapshot }); text = ''; runs = []
    }
    node.forEach((child, offset) => {
      if (child.type.name === 'hardBreak') { finish(pos + 1 + offset); segmentFrom = pos + 1 + offset + child.nodeSize; return }
      const point = view.domAtPos(pos + 1 + offset, 1)
      let textDom = point.node
      if (textDom.nodeType === 1) textDom = textDom.childNodes[point.offset] ?? textDom
      while (textDom.nodeType === 1 && textDom.firstChild) textDom = textDom.firstChild
      const parent = textDom.nodeType === 1 ? textDom as Element : textDom.parentElement ?? element
      const css = typographyRunStyle(parent)
      const styleKey = JSON.stringify(css)
      const value = child.text ?? ''
      let start = 0
      for (let end = 0; end <= value.length; end++) {
        if (end !== value.length && value[end] !== '\n') continue
        if (end > start) {
          const from = text.length
          text += value.slice(start, end)
          const previous = runs.at(-1)
          if (previous?.styleKey === styleKey) runs[runs.length - 1] = Object.freeze({ ...previous, to: text.length })
          else runs.push(Object.freeze({ from, to: text.length, styleKey, css }))
        }
        if (end < value.length) {
          const boundary = pos + 1 + offset + end
          finish(boundary); segmentFrom = boundary + 1
        }
        start = end + 1
      }
    })
    finish(pos + node.nodeSize - 1)
    return false
  })
  return results
}
