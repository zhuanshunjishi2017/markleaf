import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { EditorState } from '@codemirror/state'
import { drawSelection, EditorView, highlightActiveLine, keymap, lineNumbers } from '@codemirror/view'

export class SourceEditor {
  readonly view: EditorView

  constructor(parent: HTMLElement, content: string, onChange: (documentChanged: boolean) => void) {
    this.view = new EditorView({
      parent,
      state: EditorState.create({
        doc: content,
        extensions: [
          lineNumbers(),
          drawSelection(),
          highlightActiveLine(),
          history(),
          markdown(),
          keymap.of([...defaultKeymap, ...historyKeymap]),
          EditorView.lineWrapping,
          EditorView.updateListener.of(update => {
            if (update.docChanged || update.selectionSet) onChange(update.docChanged)
          }),
          EditorView.theme({
            '&': { height: '100%' },
            '.cm-scroller': { fontFamily: 'Cascadia Mono, Consolas, monospace', lineHeight: '1.65' },
            '.cm-content': { padding: '44px 24px 96px', maxWidth: '920px', margin: '0 auto' },
          }),
        ],
      }),
    })
  }

  getText(): string {
    return this.view.state.doc.toString()
  }

  getSelectedText(): string {
    const selection = this.view.state.selection.main
    return this.view.state.sliceDoc(selection.from, selection.to)
  }

  replaceSelection(text: string): boolean {
    const selection = this.view.state.selection.main
    this.view.dispatch({
      changes: { from: selection.from, to: selection.to, insert: text },
      selection: { anchor: selection.from + text.length },
    })
    this.focus()
    return true
  }

  deleteSelection(): boolean {
    if (this.view.state.selection.main.empty) return false
    return this.replaceSelection('')
  }

  focus(): void {
    this.view.focus()
  }

  destroy(): void {
    this.view.destroy()
  }

  find(query: string, caseSensitive: boolean, wholeWord: boolean, backwards = false): SourceMatchResult {
    const matches = findMatches(this.getText(), query, caseSensitive, wholeWord)
    if (matches.length === 0) return { current: 0, total: 0 }

    const selection = this.view.state.selection.main
    const candidates = backwards
      ? matches.filter(match => match.from < selection.from)
      : matches.filter(match => match.from > selection.from || selection.empty && match.from >= selection.from)
    const match = backwards ? candidates.at(-1) ?? matches.at(-1)! : candidates[0] ?? matches[0]!
    this.view.dispatch({ selection: { anchor: match.from, head: match.to }, scrollIntoView: true })
    this.focus()
    return { current: matches.indexOf(match) + 1, total: matches.length }
  }

  replaceCurrent(query: string, replacement: string, caseSensitive: boolean, wholeWord: boolean): SourceMatchResult {
    const selection = this.view.state.selection.main
    const selected = this.view.state.sliceDoc(selection.from, selection.to)
    if (!selection.empty && isExactMatch(selected, query, caseSensitive, wholeWord)) {
      this.view.dispatch({ changes: { from: selection.from, to: selection.to, insert: replacement } })
    }
    return this.find(query, caseSensitive, wholeWord)
  }

  replaceAll(query: string, replacement: string, caseSensitive: boolean, wholeWord: boolean): number {
    const matches = findMatches(this.getText(), query, caseSensitive, wholeWord)
    if (matches.length === 0) return 0
    this.view.dispatch({ changes: matches.map(match => ({ ...match, insert: replacement })) })
    return matches.length
  }
}

type TextMatch = { from: number; to: number }
export type SourceMatchResult = { current: number; total: number }

function findMatches(text: string, query: string, caseSensitive: boolean, wholeWord: boolean): TextMatch[] {
  if (!query) return []
  const flags = caseSensitive ? 'gu' : 'giu'
  const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const expression = new RegExp(wholeWord ? `(?<![\\p{L}\\p{N}_])${escaped}(?![\\p{L}\\p{N}_])` : escaped, flags)
  return Array.from(text.matchAll(expression), match => ({ from: match.index, to: match.index + match[0].length }))
}

function isExactMatch(text: string, query: string, caseSensitive: boolean, wholeWord: boolean): boolean {
  return findMatches(text, query, caseSensitive, wholeWord).some(match => match.from === 0 && match.to === text.length)
}
