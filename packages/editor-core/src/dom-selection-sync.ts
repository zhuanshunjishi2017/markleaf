import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'

export function syncDomSelectionToEditor(
  editor: Editor,
  domSelection: Selection | null,
): boolean {
  if (!domSelection || domSelection.isCollapsed) return false

  const { anchorNode, focusNode } = domSelection
  if (!anchorNode || !focusNode) return false
  if (!editor.view.dom.contains(anchorNode) || !editor.view.dom.contains(focusNode)) {
    return false
  }

  try {
    const anchor = editor.view.posAtDOM(anchorNode, domSelection.anchorOffset)
    const head = editor.view.posAtDOM(focusNode, domSelection.focusOffset)
    const next = TextSelection.between(
      editor.state.doc.resolve(anchor),
      editor.state.doc.resolve(head),
    )
    editor.view.dispatch(
      editor.state.tr
        .setSelection(next)
        .setMeta('addToHistory', false),
    )
    return true
  } catch {
    return false
  }
}
