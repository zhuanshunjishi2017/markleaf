import type { Editor } from '@tiptap/core'
import { syncDomSelectionToEditor } from './dom-selection-sync'
import type { FormatPainterController } from './format-painter'

export function applyFormatPainterFromDomSelection(
  editor: Editor,
  painter: FormatPainterController,
  domSelection: Selection | null,
): boolean {
  if (!painter.isArmed) return false

  if (domSelection && !domSelection.isCollapsed
    && !syncDomSelectionToEditor(editor, domSelection)) {
    return false
  }

  return painter.applyOnSelection(editor)
}
