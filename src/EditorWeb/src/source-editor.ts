import { defaultKeymap, history, historyKeymap, indentLess, indentMore, indentWithTab } from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { defaultHighlightStyle, indentUnit, syntaxHighlighting } from '@codemirror/language'
import { EditorState, RangeSetBuilder, StateEffect } from '@codemirror/state'
import {
  Decoration,
  DecorationSet,
  EditorView,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
  ViewPlugin,
  ViewUpdate,
} from '@codemirror/view'

const sourceSelectionMark = Decoration.mark({ class: 'ml-source-selection' })

/// 源码模式选区：用真实 DOM span 装饰绘制主题化背景。
/// WKWebView 对 contenteditable 忽略 ::selection，而 drawSelection() 的绝对定位
/// 图层在 WKWebView 中坐标测量不稳（反向拖选偏移、整行/折行选区缺失左侧），
/// 因此改为与 WYSIWYG 相同的装饰方案，由浏览器原生布局保证几何正确。
const themedSourceSelection = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet

    constructor(view: EditorView) {
      this.decorations = buildSelectionDecorations(view)
    }

    update(update: ViewUpdate) {
      if (update.docChanged || update.selectionSet || update.viewportChanged) {
        this.decorations = buildSelectionDecorations(update.view)
      }
    }
  },
  { decorations: (view) => view.decorations },
)

function buildSelectionDecorations(view: EditorView): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>()
  for (const range of view.state.selection.ranges) {
    if (!range.empty) {
      builder.add(range.from, range.to, sourceSelectionMark)
    }
  }
  return builder.finish()
}

export class SourceEditor {
  readonly view: EditorView
  private readonly onChange: (documentChanged: boolean) => void

  constructor(parent: HTMLElement, content: string, onChange: (documentChanged: boolean) => void, indentWidth = 2) {
    this.onChange = onChange
    this.view = new EditorView({
      parent,
      state: EditorState.create({
        doc: content,
        extensions: this.buildExtensions(indentWidth),
      }),
    })
  }

  private buildExtensions(indentWidth: number) {
    const width = Math.max(1, Math.min(8, Math.round(indentWidth) || 2))
    return [
      lineNumbers(),
      themedSourceSelection,
      highlightActiveLine(),
      highlightActiveLineGutter(),
      history(),
      markdown(),
      syntaxHighlighting(defaultHighlightStyle),
      keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
      EditorView.lineWrapping,
      EditorState.tabSize.of(width),
      indentUnit.of(' '.repeat(width)),
      EditorView.updateListener.of(update => {
        if (update.docChanged || update.selectionSet) this.onChange(update.docChanged)
      }),
      EditorView.theme({
        '&': { height: '100%' },
        '.cm-scroller': { fontFamily: 'Cascadia Mono, Consolas, monospace', lineHeight: '1.65' },
        '.cm-content': { padding: '44px 24px 96px', maxWidth: '920px', margin: '0 auto' },
        '.cm-gutters': {
          background: 'var(--bg-hover)',
          borderRight: '1px solid var(--bg-selected)',
          color: 'var(--text-tertiary)',
        },
        '.cm-activeLineGutter': {
          background: 'var(--bg-selected)',
          color: 'var(--text-secondary)',
        },
        '.cm-activeLine': { background: 'var(--bg-hover)' },
      }),
    ]
  }

  /// 精确放置光标/选区（供宿主命令与大纲联动使用）。
  setSelection(from: number, to?: number): void {
    this.view.dispatch({ selection: { anchor: from, head: to ?? from }, scrollIntoView: true })
    this.focus()
  }

  /// 偏好设置变更时更新缩进宽度（对应首选项「源码模式 > 默认缩进宽度」）。
  setIndentWidth(indentWidth: number): void {
    this.view.dispatch({
      effects: StateEffect.reconfigure.of(this.buildExtensions(indentWidth)),
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

  insertTab(): void {
    indentMore(this.view)
  }

  insertShiftTab(): void {
    indentLess(this.view)
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
