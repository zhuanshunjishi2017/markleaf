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
  plannedRanges: Array<{ from: number; to: number }>
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
  plannedRanges: [],
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

  doc.nodesBetween(0, doc.content.size, (node, position) => {
    if (!node.isBlock) return true
    if (node.type.name !== 'paragraph') {
      // Container nodes (lists, blockquotes, table cells) are visited through
      // their children. Only leaf text blocks remain explicit fallbacks.
      if (node.isTextblock) {
        paragraphs.push({
          nodeFrom: position,
          nodeTo: position + node.nodeSize,
          from: position + 1,
          to: position + node.nodeSize - 1,
          text: node.textContent,
          clusterOffsets: [0],
          eligible: false,
          fallbackReason: 'unsupported-block',
        })
      }
      return true
    }

    const nodeFrom = position
    const nodeTo = position + node.nodeSize
    const makeParagraph = (
      from: number,
      to: number,
      text: string,
      eligible: boolean,
      fallbackReason?: ExtractedTypographyParagraph['fallbackReason'],
    ): ExtractedTypographyParagraph => {
      const clusterOffsets = [0]
      if (eligible) {
        for (const cluster of splitGraphemes(text)) {
          const previous = clusterOffsets.at(-1) ?? 0
          clusterOffsets.push(previous + cluster.length)
        }
      }
      return { nodeFrom, nodeTo, from, to, text, clusterOffsets, eligible, fallbackReason }
    }

    // Phase 1A measures one top-level content width. Nested textblocks have a
    // smaller measure, so keep them on native justified fallback until the
    // measurement contract carries per-paragraph widths.
    if (doc.resolve(position).depth > 1) {
      paragraphs.push(makeParagraph(position + 1, position + node.nodeSize - 1,
        node.textContent, false, 'unsupported-block'))
      return true
    }

    let hasInlineAtom = false
    let hasInlineCode = false
    node.forEach(child => {
      if (!child.isText && child.type.name !== 'hardBreak') hasInlineAtom = true
      if (child.isText && child.marks.some(mark => mark.type.name === 'code')) hasInlineCode = true
    })

    if (hasInlineAtom || hasInlineCode) {
      paragraphs.push(makeParagraph(position + 1, position + node.nodeSize - 1,
        node.textContent, false, hasInlineAtom ? 'inline-atom' : 'inline-code'))
      return true
    }

    // A hard break is an explicit paragraph-local break. Preserve it as a
    // segment boundary, but still run every text segment through the typography
    // engine so CJK word boundaries and punctuation rules are not lost.
    let segmentText = ''
    let segmentFrom = position + 1
    node.forEach((child, offset) => {
      if (child.isText) {
        segmentText += child.text ?? ''
        return
      }
      if (child.type.name === 'hardBreak') {
        const segmentTo = position + 1 + offset
        paragraphs.push(makeParagraph(segmentFrom, segmentTo, segmentText, true))
        segmentFrom = segmentTo + child.nodeSize
        segmentText = ''
      }
    })
    paragraphs.push(makeParagraph(segmentFrom, position + node.nodeSize - 1, segmentText, true))
  })

  return paragraphs
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
    const plannedRanges: Array<{ from: number; to: number }> = []

    for (const paragraph of paragraphs) {
      if (!paragraph.eligible) {
        fallback += 1
        const fallbackNode = doc.nodeAt(paragraph.nodeFrom)
        if (fallbackNode?.type.name === 'paragraph') {
          decorations.push(Decoration.node(paragraph.nodeFrom, paragraph.nodeTo, {
            class: 'markleaf-typography-native',
          }))
        }
        continue
      }

    const nativeFallback = () => {
      fallback += 1
      decorations.push(Decoration.node(paragraph.nodeFrom, paragraph.nodeTo, {
        class: 'markleaf-typography-native',
      }))
    }

    const plan = layoutParagraph({
      text: paragraph.text,
      availableWidth: options.availableWidth,
      measure: options.measure,
    })

    if (plan.fallback || plan.lines.length === 0) {
      nativeFallback()
      continue
    }

    const adjustableLines = plan.lines.slice(0, -1)
    planned += 1
    lineBreaks += plan.lines.length - 1
    plannedRanges.push({ from: paragraph.from, to: paragraph.to })
    decorations.push(Decoration.node(
      paragraph.nodeFrom,
      paragraph.nodeTo,
      { class: 'markleaf-typography-planned' },
    ))

    for (const line of plan.lines.slice(0, -1)) {
      const fromPosition = paragraph.from + (paragraph.clusterOffsets[line.from] ?? 0)
      const contentPosition = paragraph.from + (paragraph.clusterOffsets[line.contentTo] ?? paragraph.text.length)
      const toPosition = paragraph.from + (paragraph.clusterOffsets[line.to] ?? paragraph.text.length)
      const clusterCount = Math.max(0, line.contentTo - line.from)
      const adjustmentAmount = line.adjustment.amount
      let wordSpacing = 0
      let characterSpacing = 0
      let adjustmentMode: 'word' | 'character' = 'character'
      if (adjustmentAmount > 0) {
        if (line.spaceCount > 0) {
          wordSpacing = adjustmentAmount / line.spaceCount
          adjustmentMode = 'word'
        } else if (clusterCount > 1) {
          characterSpacing = adjustmentAmount / (clusterCount - 1)
          adjustmentMode = 'character'
        }
      } else if (adjustmentAmount < 0 && clusterCount > 1) {
        characterSpacing = adjustmentAmount / (clusterCount - 1)
        adjustmentMode = 'character'
      }
      const plannedGapCount = adjustmentMode === 'word'
        ? Math.max(1, line.spaceCount)
        : Math.max(1, clusterCount - 1)
      const lastClusterLength = clusterCount > 0
        ? (paragraph.clusterOffsets[line.contentTo] ?? 0)
          - (paragraph.clusterOffsets[line.contentTo - 1] ?? 0)
        : 0
      decorations.push(Decoration.inline(
        fromPosition,
        contentPosition,
        {
          class: 'markleaf-typography-line markleaf-typography-line-words',
          'data-markleaf-typography-line': 'true',
          'data-markleaf-typography-line-id': `${paragraph.from}:${line.from}:${line.to}`,
          'data-markleaf-typography-space-count': String(line.spaceCount),
          'data-markleaf-typography-adjustment-mode': adjustmentMode,
          'data-markleaf-typography-gap-count': String(plannedGapCount),
          style: [
            `--ml-typography-line-adjust:${line.adjustment.amount}px`,
            `--ml-typography-space-count:${line.spaceCount}`,
            `--ml-typography-word-spacing:${wordSpacing}px`,
            `--ml-typography-character-spacing:${characterSpacing}px`,
          ].join(';'),
        },
        {
          key: `typography-line-${paragraph.from}-${line.from}-${line.to}`,
        },
      ))

      if (characterSpacing !== 0 && lastClusterLength > 0) {
        decorations.push(Decoration.inline(
          contentPosition - lastClusterLength,
          contentPosition,
          {
            class: 'markleaf-typography-line-tail',
            style: '--ml-typography-character-spacing:0px',
          },
          {
            key: `typography-line-tail-${paragraph.from}-${line.contentTo}`,
          },
        ))
      }

      if (toPosition > contentPosition) {
        decorations.push(Decoration.inline(contentPosition, toPosition, {
          class: 'markleaf-typography-consumed',
        }))
      }

      decorations.push(Decoration.widget(
        toPosition,
        () => {
          const element = document.createElement('br')
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
    plannedRanges,
    decorations: DecorationSet.create(doc, decorations),
  }
}

export function typographyPlannedParagraphRanges(state: unknown): Array<{ from: number; to: number }> {
  const projectionState = typographyProjectionKey.getState(state as never)
  return projectionState?.enabled ? projectionState.plannedRanges : []
}

export function shouldApplyTypographyProjection(input: {
  previousEditor: unknown
  nextEditor: unknown
  previousEnabled: boolean
  nextEnabled: boolean
  previousWidth: number
  nextWidth: number
}): boolean {
  return input.previousEditor !== input.nextEditor
    || input.previousEnabled !== input.nextEnabled
    || input.previousWidth !== input.nextWidth
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
