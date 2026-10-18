import { Extension, type Editor } from '@tiptap/core'
import { closeHistory } from '@tiptap/pm/history'
import { Plugin, PluginKey, TextSelection, type EditorState } from '@tiptap/pm/state'
import type { EditorView } from '@tiptap/pm/view'

type InputRun = { from: number; to: number }
const inputRunKey = new PluginKey<InputRun | null>('markleafContinuousInlineInput')

// These marks must have exactly one input owner. Their Markdown parsers,
// commands, keyboard shortcuts and paste rules are otherwise unchanged.
export const continuousInlineMarkNames = new Set(['bold', 'italic', 'strike', 'highlight', 'code'])

export function resetInlineInput(editor: Editor): void {
  if (editor.isDestroyed) return
  editor.view.dispatch(editor.state.tr.setMeta(inputRunKey, null).setMeta('addToHistory', false))
}

const markers: Array<[string, string[]]> = [
  ['***', ['bold', 'italic']], ['___', ['bold', 'italic']],
  ['**', ['bold']], ['__', ['bold']], ['~~', ['strike']],
  ['==', ['highlight']], ['`', ['code']], ['*', ['italic']], ['_', ['italic']],
]

function escaped(text: string, offset: number): boolean {
  let backslashes = 0
  for (let index = offset - 1; index >= 0 && text[index] === '\\'; index--) backslashes++
  return backslashes % 2 === 1
}

function conversion(state: EditorState, run: InputRun) {
  const $from = state.doc.resolve(run.from)
  const blockStart = $from.start()
  const blockText = $from.parent.textBetween(0, run.to - blockStart, '\n', '\ufffc')
  const text = state.doc.textBetween(run.from, run.to, '\n', '\ufffc')
  for (const [marker, markNames] of markers) {
    if (!text.endsWith(marker)) continue
    const character = marker.charAt(0)
    const closing = text.length - marker.length
    if (escaped(blockText, run.to - blockStart - marker.length)) continue
    const opening = text.lastIndexOf(marker, closing - 1)
    if (opening < 0) continue
    const content = text.slice(opening + marker.length, closing)
    // Do not reinterpret delimiter runs, whitespace boundaries, or text
    // across inline atoms/hard breaks. Incomplete ** / *** stays pending.
    if (!content || content.trim() !== content || content.includes(character) || /[\n\ufffc]/.test(content)) continue
    const offset = run.from - blockStart + opening
    const preceding = blockText[offset - 1] ?? ''
    if (preceding === character || escaped(blockText, offset)) continue
    if (character === '_' && /[A-Za-z0-9_]/.test(preceding)) continue
    const marks = markNames.map(name => state.schema.marks[name])
    if (marks.some(mark => !mark || !$from.parent.type.allowsMarkType(mark))) continue

    const from = run.from + opening
    const tr = closeHistory(state.tr)
      .delete(run.from + closing, run.to)
      .delete(from, from + marker.length)
    const to = from + content.length
    marks.forEach(mark => { if (mark) tr.addMark(from, to, mark.create()) })
    return tr.setSelection(TextSelection.create(tr.doc, to)).setStoredMarks([]).scrollIntoView()
  }
  return null
}

export const ContinuousInlineInput = Extension.create({
  name: 'markleafContinuousInlineInput',
  // Block input rules get first refusal (lists, headings, fences, math).
  // Only the remaining literal keystrokes establish a continuous input run.
  priority: 10,
  addProseMirrorPlugins() {
    let composing = false
    const reset = (view: EditorView) => {
      view.dispatch(view.state.tr.setMeta(inputRunKey, null).setMeta('addToHistory', false))
      return false
    }
    return [new Plugin<InputRun | null>({
      key: inputRunKey,
      state: {
        init: () => null,
        apply(tr, previous) {
          const run = tr.getMeta(inputRunKey) as InputRun | null | undefined
          if (run !== undefined) return run
          // Programmatic edits, navigation, selection replacement, undo/redo
          // and document reloads never confer provenance on existing text.
          return tr.docChanged || tr.selectionSet || tr.storedMarksSet ? null : previous
        },
      },
      props: {
        handleDOMEvents: {
          blur: reset,
          mousedown: reset,
          paste: reset,
          drop: reset,
          compositionstart: view => { composing = true; return reset(view) },
          compositionend: view => { composing = false; return reset(view) },
        },
        handleTextInput: (view, from, to, text) => {
          const { state } = view
          const $from = state.doc.resolve(from)
          if (composing || view.composing || !view.editable || from !== to || !state.selection.empty
            || !$from.parent.isTextblock || $from.parent.type.spec.code || $from.parent.type.spec.atom
            || (state.storedMarks ?? $from.marks()).some(mark => mark.type.spec.code)) {
            reset(view)
            return false
          }
          const previous = inputRunKey.getState(state)
          const run = { from: previous?.to === from ? previous.from : from, to: from + text.length }
          view.dispatch(state.tr.insertText(text, from, to).setMeta(inputRunKey, run).scrollIntoView())
          // Keep the literal closing marker in history, then make the format
          // transform its own undo step. Undo restores syntax, not earlier text.
          const current = inputRunKey.getState(view.state)
          if (current && /[*_~=`]$/.test(text)) {
            const tr = conversion(view.state, current)
            if (tr) {
              view.dispatch(tr)
              view.dispatch(closeHistory(view.state.tr).setMeta('addToHistory', false))
            }
          }
          return true
        },
      },
    })]
  },
})
