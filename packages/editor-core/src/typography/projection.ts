import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { Editor } from '@tiptap/core'
import type { ScriptClass, TextMetrics } from '@markleaf/typography'

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
          if (meta && !meta.enabled) return disabledState()
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
