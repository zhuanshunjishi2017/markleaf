import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { Editor } from '@tiptap/core'
import type { Node as PMNode } from '@tiptap/pm/model'
import type { LayoutPlan, ScriptClass, TextMetrics } from '@markleaf/typography'
import { layoutParagraph, splitGraphemes } from '@markleaf/typography'

export type TypographyProjectionStatus =
  | 'disabled'
  | 'planned'
  | 'partial'
  | 'fallback'

export type TypographyProjectionDiagnostics = {
  status: TypographyProjectionStatus
  eligibleParagraphs: number
  plannedParagraphs: number
  fallbackParagraphs: number
  lineBreaks: number
}

export type TypographyMeasurement = (
  text: string,
  script: ScriptClass,
) => TextMetrics

export type TypographyProjectionOptions = {
  availableWidth: number
  measure: TypographyMeasurement
}

export type ExtractedTypographyParagraph = {
  nodeFrom: number
  nodeTo: number
  from: number
  to: number
  text: string
  clusterOffsets: number[]
  eligible: boolean
  fallbackReason?: 'unsupported-block' | 'inline-atom' | 'inline-code'
}

type ProjectionMeta = {
  enabled: boolean
  options?: TypographyProjectionOptions
}

type ProjectionState = {
  enabled: boolean
  options?: TypographyProjectionOptions
  diagnostics: TypographyProjectionDiagnostics
  decorations: DecorationSet
}

export const typographyProjectionKey = new PluginKey<ProjectionState>('markleaf-typography-projection')

const disabledState = (): ProjectionState => ({
  enabled: false,
  diagnostics: {
    status: 'disabled',
    eligibleParagraphs: 0,
    plannedParagraphs: 0,
    fallbackParagraphs: 0,
    lineBreaks: 0,
  },
  decorations: DecorationSet.empty,
})

export const TypographyProjection = Extension.create({
  name: 'markleafTypographyProjection',
  addProseMirrorPlugins() {
    return [new Plugin<ProjectionState>({
      key: typographyProjectionKey,
      state: {
        init: disabledState,
        apply: (transaction, previous) => {
          const meta = transaction.getMeta(typographyProjectionKey) as ProjectionMeta | undefined
          if (meta) {
            return projectionStateFor(transaction.doc, meta.enabled, meta.options)
          }
          if (!previous.enabled || !previous.options) return previous
          if (transaction.docChanged) {
            return projectionStateFor(transaction.doc, true, previous.options)
          }
          return {
            ...previous,
            decorations: previous.decorations.map(transaction.mapping, transaction.doc),
          }
        },
      },
      props: {
        decorations: state => typographyProjectionKey.getState(state)?.decorations ?? DecorationSet.empty,
      },
    })]
  },
})

export function getTypographyProjectionDiagnostics(editor: Editor): TypographyProjectionDiagnostics {
  return typographyProjectionKey.getState(editor.state)?.diagnostics ?? disabledState().diagnostics
}

export function extractTypographyParagraphs(doc: PMNode): ExtractedTypographyParagraph[] {
  const paragraphs: ExtractedTypographyParagraph[] = []

  doc.forEach((node, offset) => {
    const paragraph: ExtractedTypographyParagraph = {
      nodeFrom: offset,
      nodeTo: offset + node.nodeSize,
      from: offset + 1,
      to: offset + node.nodeSize - 1,
      text: node.textContent,
      clusterOffsets: [0],
      eligible: node.type.name === 'paragraph',
      fallbackReason: node.type.name === 'paragraph' ? undefined : 'unsupported-block',
    }

    if (paragraph.eligible) {
      let hasInlineAtom = false
      let hasInlineCode = false
      node.forEach(child => {
        if (!child.isText) hasInlineAtom = true
        if (child.marks.some(mark => mark.type.name === 'code')) hasInlineCode = true
      })
      if (hasInlineAtom) {
        paragraph.eligible = false
        paragraph.fallbackReason = 'inline-atom'
      } else if (hasInlineCode) {
        paragraph.eligible = false
        paragraph.fallbackReason = 'inline-code'
      }
    }

    if (paragraph.eligible) {
      for (const cluster of splitGraphemes(paragraph.text)) {
        const previous = paragraph.clusterOffsets.at(-1) ?? 0
        paragraph.clusterOffsets.push(previous + cluster.length)
      }
    }

    paragraphs.push(paragraph)
  })

  return paragraphs
}

type PlannedParagraph = {
  nodeFrom: number
  nodeTo: number
  textFrom: number
  plan: LayoutPlan
  clusterOffsets: number[]
}

function projectionStateFor(
  doc: PMNode,
  enabled: boolean,
  options?: TypographyProjectionOptions,
): ProjectionState {
  if (!enabled || !options || !Number.isFinite(options.availableWidth) || options.availableWidth <= 0) {
    return disabledState()
  }

  const decorations: Decoration[] = []
  const paragraphs = extractTypographyParagraphs(doc)
  let planned = 0
  let fallback = 0
  let lineBreaks = 0

  for (const paragraph of paragraphs) {
    if (!paragraph.eligible) {
      fallback += 1
      continue
    }

    const plan = layoutParagraph({
      text: paragraph.text,
      availableWidth: options.availableWidth,
      measure: options.measure,
    })

    if (plan.fallback || plan.lines.length === 0) {
      fallback += 1
      continue
    }

    planned += 1
    lineBreaks += plan.lines.length - 1
    decorations.push(Decoration.node(
      paragraph.nodeFrom,
      paragraph.nodeTo,
      { class: 'markleaf-typography-planned' },
    ))

    for (const line of plan.lines.slice(0, -1)) {
      const position = paragraph.from + (paragraph.clusterOffsets[line.to] ?? paragraph.text.length)
      decorations.push(Decoration.widget(
        position,
        () => {
          const element = document.createElement('span')
          element.className = 'markleaf-typography-break'
          element.setAttribute('aria-hidden', 'true')
          element.setAttribute('contenteditable', 'false')
          return element
        },
        {
          side: 1,
          ignoreSelection: true,
          marks: [],
          key: `typography-break-${paragraph.from}-${line.to}`,
        },
      ))
    }
  }

  const status: TypographyProjectionStatus = planned === 0
    ? 'fallback'
    : fallback === 0 ? 'planned' : 'partial'

  return {
    enabled: true,
    options,
    diagnostics: {
      status,
      eligibleParagraphs: paragraphs.filter(paragraph => paragraph.eligible).length,
      plannedParagraphs: planned,
      fallbackParagraphs: fallback,
      lineBreaks,
    },
    decorations: DecorationSet.create(doc, decorations),
  }
}

export function setTypographyProjectionEnabled(
  editor: Editor,
  enabled: boolean,
  options?: TypographyProjectionOptions,
): void {
  editor.view.dispatch(editor.state.tr
    .setMeta(typographyProjectionKey, { enabled, options })
    .setMeta('addToHistory', false))
}
