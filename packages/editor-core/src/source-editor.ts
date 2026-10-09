import {
  defaultKeymap,
  history,
  historyKeymap,
  indentLess,
  indentMore,
  indentWithTab,
  redo,
  redoDepth,
  undo,
  undoDepth,
} from '@codemirror/commands'
import { markdown } from '@codemirror/lang-markdown'
import { HighlightStyle, indentUnit, syntaxHighlighting, syntaxTree } from '@codemirror/language'
import { tags as t } from '@lezer/highlight'
import { Compartment, EditorState, RangeSetBuilder, StateEffect } from '@codemirror/state'
import type { ReadingAnchor, ReadingAnchorInput } from './reading-anchor'
import { normalizeReadingAnchor, resolveReadingAnchorOrdinal } from './reading-anchor'
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

/// 主题感知的源码语法高亮：class-based HighlightStyle，颜色由 .tok-* CSS（主题变量）控制。
/// 使用 defaultHighlightStyle 会在 WKWebView 中生成非主题的硬编码色（深色主题下看不清），
/// 因此改为自定义 tok-* 类，由 base.css / styles.css 按 --theme-* 变量着色。
const markleafHighlightStyle = HighlightStyle.define([
  { tag: t.heading, class: 'tok-heading' },
  { tag: t.comment, class: 'tok-comment' },
  { tag: t.strong, class: 'tok-strong' },
  { tag: t.emphasis, class: 'tok-emphasis' },
  { tag: t.strikethrough, class: 'tok-strikethrough' },
  { tag: t.link, class: 'tok-link' },
  { tag: t.keyword, class: 'tok-keyword' },
  { tag: [t.atom, t.bool, t.number, t.url, t.labelName], class: 'tok-number' },
  { tag: [t.literal, t.inserted, t.string, t.deleted], class: 'tok-string' },
  { tag: [t.typeName, t.namespace, t.className], class: 'tok-type' },
  { tag: [t.definition(t.variableName), t.local(t.variableName)], class: 'tok-variable' },
  { tag: t.function(t.variableName), class: 'tok-function' },
  { tag: t.propertyName, class: 'tok-property' },
  { tag: t.operator, class: 'tok-operator' },
  { tag: t.punctuation, class: 'tok-punctuation' },
  { tag: t.meta, class: 'tok-meta' },
  { tag: t.invalid, class: 'tok-invalid' },
])

const sourceSelectionMark = Decoration.mark({ class: 'ml-source-selection' })

export type UnsafeEmphasisKind = 'bold' | 'italic'
export type UnsafeEmphasisAction = 'literal' | 'html'
export type UnsafeEmphasisRequest = {
  id: string
  kind: UnsafeEmphasisKind
}

export type SourceEditorStatus = {
  characterCount: number
  selectedCharacterCount: number
  totalCharacterCount: number
  nonWhitespaceCharacterCount: number
  cjkCharacterCount: number
  westernWordCount: number
  formulaCount: number
  codeLineCount: number
  paragraphCount: number
  blockType: 'paragraph'
  line: number
  column: number
}

export type SourceChapter = { level: number; text: string; position: number }

type UnsafeEmphasisMatch = {
  from: number
  to: number
  content: string
  kind: UnsafeEmphasisKind
  marker: '*' | '**'
}

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
  private documentStatistics: Omit<SourceEditorStatus, 'characterCount' | 'selectedCharacterCount' | 'line' | 'column' | 'blockType'>
  private readOnly: boolean
  private readonly readOnlyCompartment = new Compartment()
  private readonly onUnsafeEmphasis?: (request: UnsafeEmphasisRequest) => void
  private readonly detectUnsafeEmphasisEnabled: boolean
  private readonly onSelectionChanged?: (from: number, to: number) => void
  private readonly pendingUnsafeEmphasis = new Map<string, UnsafeEmphasisMatch>()
  private cachedSourceChapters: SourceChapter[] | null = null
  private chapterScrollFrame = 0

  constructor(
    parent: HTMLElement,
    content: string,
    onChange: (documentChanged: boolean) => void,
    indentWidth = 2,
    readOnly = false,
    onUnsafeEmphasis?: (request: UnsafeEmphasisRequest) => void,
    detectUnsafeEmphasis = true,
    onSelectionChanged?: (from: number, to: number) => void,
  ) {
    this.onChange = onChange
    this.documentStatistics = getSourceDocumentStatistics(content)
    this.readOnly = readOnly
    this.onUnsafeEmphasis = onUnsafeEmphasis
    this.detectUnsafeEmphasisEnabled = detectUnsafeEmphasis
    this.onSelectionChanged = onSelectionChanged
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
      this.readOnlyCompartment.of(EditorState.readOnly.of(this.readOnly)),
      lineNumbers(),
      themedSourceSelection,
      highlightActiveLine(),
      highlightActiveLineGutter(),
      history(),
      markdown(),
      syntaxHighlighting(markleafHighlightStyle),
      keymap.of([...defaultKeymap, ...historyKeymap, indentWithTab]),
      EditorView.domEventHandlers({
        paste: (event, view) => this.handlePaste(event, view),
        dragstart: (event) => {
          if (!this.readOnly) return false
          event.dataTransfer?.clearData()
          event.preventDefault()
          return true
        },
        // 失焦时折叠选区：让主题化选中装饰随选区清空而消失。
        blur: (_event, view) => {
          const selection = view.state.selection.main
          if (!selection.empty) {
            view.dispatch({ selection: { anchor: selection.from } })
          }
          return false
        },
      }),
      EditorView.lineWrapping,
      EditorState.tabSize.of(width),
      indentUnit.of(' '.repeat(width)),
      EditorView.updateListener.of(update => {
        if (update.docChanged) this.cachedSourceChapters = null
        if (update.docChanged) {
          this.documentStatistics = getSourceDocumentStatistics(update.state.doc.toString())
        }
        if (update.docChanged || update.selectionSet) this.onChange(update.docChanged)
        if (update.docChanged || update.selectionSet) {
          const selection = update.state.selection.main
          this.onSelectionChanged?.(selection.from, selection.to)
        }
        if (update.docChanged) this.detectUnsafeEmphasis(update)
      }),
      EditorView.theme({
        '&': { height: '100%' },
        '.cm-scroller': { fontFamily: 'Cascadia Mono, Consolas, monospace', lineHeight: '1.65' },
        // 正文不居中：行号栏固定在左侧，若把正文居中，侧边栏关闭（窗口变宽）时
        // 行号与正文之间会出现一大段空白。
        '.cm-content': { padding: '44px 24px 96px' },
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

  private handlePaste(event: ClipboardEvent, view: EditorView): boolean {
    if (this.readOnly) return false
    const clipboard = event.clipboardData
    if (!clipboard) return false

    const plain = clipboard.getData('text/plain')
    const html = clipboard.getData('text/html')
    const text = normalizeInsertedText(plain.length > 0 ? plain : htmlToPlainText(html))
    if (text.length === 0) return false

    event.preventDefault()
    const selection = view.state.selection.main
    view.dispatch({
      changes: { from: selection.from, to: selection.to, insert: text },
      selection: { anchor: selection.from + text.length },
    })
    this.focus()
    return true
  }

  /// 精确放置光标/选区（供宿主命令与大纲联动使用）。
  setSelection(from: number, to?: number): void {
    const length = this.view.state.doc.length
    const anchor = Math.max(0, Math.min(length, Math.trunc(from)))
    const head = Math.max(0, Math.min(length, Math.trunc(to ?? from)))
    this.view.dispatch({ selection: { anchor, head }, scrollIntoView: true })
    this.focus()
  }

  setReadOnly(enabled: boolean): void {
    if (this.readOnly === enabled) return
    this.readOnly = enabled
    this.view.dispatch({
      effects: this.readOnlyCompartment.reconfigure(
        EditorState.readOnly.of(enabled),
      ),
    })
  }

  setScrollTop(top: number): void {
    this.view.scrollDOM.scrollTop = Math.max(0, top)
  }

  getSourceChapters(): SourceChapter[] {
    if (this.cachedSourceChapters) return this.cachedSourceChapters
    const chapters: SourceChapter[] = []
    let inFence = false

    for (let lineNumber = 1; lineNumber <= this.view.state.doc.lines; lineNumber += 1) {
      const line = this.view.state.doc.line(lineNumber)
      const trimmed = line.text.trim()
      if (isFenceLine(trimmed)) {
        inFence = !inFence
        continue
      }
      if (inFence || !trimmed) continue

      const heading = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(trimmed)
      const chapter = /^(第\s*[0-9〇零一二三四五六七八九十百千]+\s*[章节回篇卷])(?:[\s:：、.]+(.+))?$/i.exec(trimmed)
      const englishChapter = /^(chapter|section)\s+\d+(?:\.\d+)*\b(.*)$/i.exec(trimmed)
      if (!heading && !chapter && !englishChapter) continue

      const level = heading?.[1] ? heading[1].length : 1
      const marker = chapter?.[1] ?? ''
      const suffix = chapter?.[2] ?? englishChapter?.[2] ?? ''
      const text = (heading?.[2] ?? `${marker}${suffix ? ` ${suffix.trim()}` : ''}`).trim()
      chapters.push({ level, text, position: line.from })
    }

    this.cachedSourceChapters = chapters
    return chapters
  }

  getActiveSourceChapterPosition(source: 'cursor' | 'scroll' = 'cursor'): number | null {
    const from = source === 'scroll'
      ? this.view.lineBlockAtHeight(this.view.scrollDOM.scrollTop + this.view.documentTop).from
      : this.view.state.selection.main.from
    let active: number | null = null
    for (const chapter of this.getSourceChapters()) {
      if (chapter.position <= from) active = chapter.position
      else break
    }
    return active
  }

  gotoSourceChapter(chapter: SourceChapter): void {
    this.gotoSourcePosition(chapter.position)
  }

  gotoSourceHeading(text: string, approximatePosition?: number): boolean {
    const chapters = this.getSourceChapters().filter(chapter => chapter.text === text)
    if (chapters.length === 0) return false
    const chapter = chapters.reduce((nearest, candidate) => {
      if (approximatePosition === undefined) return nearest
      return Math.abs(candidate.position - approximatePosition) < Math.abs(nearest.position - approximatePosition)
        ? candidate
        : nearest
    })
    return this.gotoSourcePosition(chapter.position)
  }

  gotoSourcePosition(position: number): boolean {
    if (!Number.isFinite(position)) return false
    window.cancelAnimationFrame(this.chapterScrollFrame)
    const length = this.view.state.doc.length
    const target = Math.max(0, Math.min(length, Math.trunc(position)))
    const line = this.view.state.doc.lineAt(target)
    this.view.dispatch({
      selection: { anchor: line.from, head: line.from },
    })

    this.scrollSourceChapterIntoView(line.from)
    return true
  }

  private scrollSourceChapterIntoView(position: number, attempt = 0): void {
    // Jumping far into a large document can first render the target using an
    // estimated height. After CodeMirror scrolls it into view, calibrate with
    // the real rendered coordinates so wrapped paragraphs above cannot leave
    // the heading one or two paragraphs away from the top.
    const document = this.view.state.doc
    const line = document.lineAt(position)
    // A position at line.from sits on the boundary between the previous line
    // and the heading. CodeMirror may resolve that boundary to the previous
    // paragraph's rect, which makes sidebar jumps land one or two blocks high.
    // Scroll to the heading's first character and prefer the downward side.
    const scrollTarget = Math.min(line.to, line.from + 1)
    this.view.dispatch({
      effects: EditorView.scrollIntoView(scrollTarget, { y: 'start' }),
    })
    this.chapterScrollFrame = window.requestAnimationFrame(() => {
      this.chapterScrollFrame = 0
      // A document edit invalidates both the target position and its geometry.
      if (this.view.state.doc !== document) return
      const coords = this.view.coordsAtPos(scrollTarget, 1)
      const viewport = this.view.scrollDOM.getBoundingClientRect()
      if (coords) {
        const delta = coords.top - viewport.top - 56
        if (Math.abs(delta) > 1) this.view.scrollDOM.scrollTop += delta
      }
      // CodeMirror virtualizes distant lines. The first measured coordinate
      // can still be based on estimated block heights; wrapping and table
      // layout may settle over several frames. Keep correcting until the
      // layout has had time to converge instead of accepting that first value.
      if (attempt < 8) {
        this.chapterScrollFrame = window.requestAnimationFrame(() => {
          this.chapterScrollFrame = 0
          if (this.view.state.doc === document) this.scrollSourceChapterIntoView(position, attempt + 1)
        })
      }
    })
  }

  setSelectionToRenderedLineEnd(lineNumber: number, center = false): void {
    const line = this.lineForRenderedLine(lineNumber)
    this.setSelectionToLine(line, center)
  }

  setSelectionToTableEnd(tableIndex: number, center = false): void {
    const line = this.lineForTableEnd(tableIndex)
    this.setSelectionToLine(line, center)
  }

  setSelectionAfterTableRenderedLines(tableIndex: number, lineOffset: number, center = false): void {
    const tableEnd = this.lineForTableEnd(tableIndex)
    const line = this.lineForRenderedLineAfter(tableEnd.number, lineOffset)
    this.setSelectionToLine(line, center)
  }

  private setSelectionToLine(line: { to: number }, center: boolean): void {
    const effects = center ? [EditorView.scrollIntoView(line.to, { y: 'center' })] : undefined
    this.view.dispatch({
      selection: { anchor: line.to },
      effects,
      scrollIntoView: !center,
    })
    this.focus()
  }

  private lineForRenderedLine(lineNumber: number) {
    const targetLine = Math.max(1, Math.floor(lineNumber) || 1)
    let renderedLine = 0
    let fallback = this.view.state.doc.line(1)
    let inFencedCode = false

    for (let rawLine = 1; rawLine <= this.view.state.doc.lines; rawLine += 1) {
      const line = this.view.state.doc.line(rawLine)
      const isFence = isFenceLine(line.text)
      if (isFence) {
        inFencedCode = !inFencedCode
        continue
      }

      const countsAsRenderedLine = shouldCountRenderedSourceLine(line.text, inFencedCode)
      if (countsAsRenderedLine) {
        renderedLine += 1
        fallback = line
        if (renderedLine === targetLine) return line
      }
    }

    return fallback
  }

  private lineForRenderedLineAfter(startLineNumber: number, lineOffset: number) {
    const targetOffset = Math.max(0, Math.floor(lineOffset) || 0)
    if (targetOffset === 0) return this.view.state.doc.line(Math.max(1, Math.min(startLineNumber, this.view.state.doc.lines)))

    let renderedLine = 0
    let fallback = this.view.state.doc.line(Math.max(1, Math.min(startLineNumber, this.view.state.doc.lines)))
    let inFencedCode = false

    for (let rawLine = startLineNumber + 1; rawLine <= this.view.state.doc.lines; rawLine += 1) {
      const line = this.view.state.doc.line(rawLine)
      const isFence = isFenceLine(line.text)
      if (isFence) {
        inFencedCode = !inFencedCode
        continue
      }

      const countsAsRenderedLine = shouldCountRenderedSourceLine(line.text, inFencedCode)
      if (countsAsRenderedLine) {
        renderedLine += 1
        fallback = line
        if (renderedLine === targetOffset) return line
      }
    }

    return fallback
  }

  private lineForTableEnd(tableIndex: number) {
    const targetIndex = Math.max(0, Math.floor(tableIndex) || 0)
    let currentIndex = -1
    let tableEnd = this.view.state.doc.line(1)

    for (let lineNumber = 1; lineNumber <= this.view.state.doc.lines; lineNumber += 1) {
      const line = this.view.state.doc.line(lineNumber)
      if (!isTableRowLine(line.text)) continue

      const previous = lineNumber > 1 ? this.view.state.doc.line(lineNumber - 1).text : ''
      if (!isTableRowLine(previous)) {
        currentIndex += 1
      }
      if (currentIndex === targetIndex) {
        tableEnd = line
      } else if (currentIndex > targetIndex) {
        break
      }
    }

    return tableEnd
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

  getReadingAnchor(): ReadingAnchor | null {
    if (this.view.state.doc.lines === 0) return null
    const scrollTop = this.view.scrollDOM.scrollTop
    const block = this.view.lineBlockAtHeight(scrollTop + this.view.documentTop)
    const line = this.view.state.doc.lineAt(block.from)
    const fraction = block.height > 0
      ? Math.min(1, Math.max(0, (scrollTop + this.view.documentTop - block.top) / block.height))
      : 0
    return normalizeReadingAnchor({
      kind: 'source',
      ordinal: line.number - 1,
      total: this.view.state.doc.lines,
      token: line.text,
      fraction,
    })
  }

  setReadingAnchor(input: ReadingAnchorInput): void {
    if (this.view.state.doc.lines === 0) return
    const anchor = normalizeReadingAnchor(input)
    const texts = Array.from(
      { length: this.view.state.doc.lines },
      (_, index) => this.view.state.doc.line(index + 1).text,
    )
    const ordinal = resolveReadingAnchorOrdinal(anchor, texts)
    const line = this.view.state.doc.line(ordinal + 1)
    const block = this.view.lineBlockAt(line.from)
    this.view.scrollDOM.scrollTop = Math.max(
      0,
      block.top - this.view.documentTop + anchor.fraction * block.height,
    )
  }

  getSelectedText(): string {
    const selection = this.view.state.selection.main
    return this.view.state.sliceDoc(selection.from, selection.to)
  }

  getStatus(): SourceEditorStatus {
    const selection = this.view.state.selection.main
    const line = this.view.state.doc.lineAt(selection.from)
    return {
      characterCount: this.documentStatistics.nonWhitespaceCharacterCount,
      selectedCharacterCount: Array.from(this.getSelectedText()).filter(character => !/\s/u.test(character)).length,
      ...this.documentStatistics,
      blockType: 'paragraph',
      line: line.number,
      column: Array.from(line.text.slice(0, selection.from - line.from)).length + 1,
    }
  }

  replaceSelection(text: string): boolean {
    if (this.readOnly) return false
    const selection = this.view.state.selection.main
    const normalizedText = normalizeInsertedText(text)
    this.view.dispatch({
      changes: { from: selection.from, to: selection.to, insert: normalizedText },
      selection: { anchor: selection.from + normalizedText.length },
    })
    this.focus()
    return true
  }

  insertMermaidCodeBlock(): boolean {
    if (this.readOnly) return false
    const selection = this.view.state.selection.main
    const currentLine = this.view.state.doc.lineAt(selection.from)
    const prefix = selection.from > currentLine.from ? '\n' : ''
    const suffix = selection.to < currentLine.to ? '\n' : ''
    const insertion = `${prefix}\`\`\`mermaid\n\n\`\`\`${suffix}`
    const cursor = selection.from + prefix.length + '```mermaid\n'.length
    this.view.dispatch({
      changes: { from: selection.from, to: selection.to, insert: insertion },
      selection: { anchor: cursor },
      scrollIntoView: true,
    })
    this.focus()
    return true
  }

  canUndo(): boolean {
    return undoDepth(this.view.state) > 0
  }

  canRedo(): boolean {
    return redoDepth(this.view.state) > 0
  }

  undo(): boolean {
    if (this.readOnly) return false
    const success = undo(this.view)
    if (success) this.focus()
    return success
  }

  redo(): boolean {
    if (this.readOnly) return false
    const success = redo(this.view)
    if (success) this.focus()
    return success
  }

  deleteSelection(): boolean {
    if (this.readOnly) return false
    if (this.view.state.selection.main.empty) return false
    return this.replaceSelection('')
  }

  selectAll(): boolean {
    this.view.dispatch({ selection: { anchor: 0, head: this.view.state.doc.length } })
    this.focus()
    return true
  }

  /// Esc 折叠源码选区（供 main.ts 的全局 keydown 调用）。
  collapseSelection(): boolean {
    const selection = this.view.state.selection.main
    if (selection.empty) {
      return false
    }
    this.setSelection(selection.from)
    return true
  }

  insertTab(): void {
    if (this.readOnly) return
    indentMore(this.view)
  }

  insertShiftTab(): void {
    if (this.readOnly) return
    indentLess(this.view)
  }

  focus(): void {
    this.view.focus()
  }

  destroy(): void {
    window.cancelAnimationFrame(this.chapterScrollFrame)
    this.chapterScrollFrame = 0
    this.view.destroy()
  }

  resolveUnsafeEmphasis(requestId: string, action: UnsafeEmphasisAction): void {
    if (this.readOnly) return
    const match = this.pendingUnsafeEmphasis.get(requestId)
    if (!match) return
    this.pendingUnsafeEmphasis.delete(requestId)
    if (action !== 'html') return

    const current = this.view.state.sliceDoc(match.from, match.to)
    if (current !== `${match.marker}${match.content}${match.marker}`) return
    const replacement = match.kind === 'bold'
      ? `<strong>${markdownInlineToHtmlText(match.content)}</strong>`
      : `<em>${markdownInlineToHtmlText(match.content)}</em>`
    this.view.dispatch({
      changes: { from: match.from, to: match.to, insert: replacement },
      selection: { anchor: match.from + replacement.length },
    })
    this.focus()
  }

  private detectUnsafeEmphasis(update: ViewUpdate): void {
    if (!this.detectUnsafeEmphasisEnabled || !this.onUnsafeEmphasis) return
    const match = findUnsafeEmphasisInChangedLines(update)
    if (!match) return
    const requestId = `${match.kind}:${match.from}:${match.to}:${match.content}`
    if (this.pendingUnsafeEmphasis.has(requestId)) return
    this.pendingUnsafeEmphasis.set(requestId, match)
    this.onUnsafeEmphasis({ id: requestId, kind: match.kind })
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

function htmlToPlainText(html: string): string {
  if (!html) return ''
  const template = document.createElement('template')
  template.innerHTML = html
  return (template.content.textContent ?? '').trim()
}

function normalizeInsertedText(text: string): string {
  return text.replace(/\r\n?/g, '\n')
}

function getSourceDocumentStatistics(text: string) {
  const documentText = stripLeadingFrontMatter(text)
  return {
    totalCharacterCount: Array.from(documentText).length,
    nonWhitespaceCharacterCount: Array.from(documentText).filter(character => !/\s/u.test(character)).length,
    cjkCharacterCount: Array.from(documentText.matchAll(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]/gu)).length,
    westernWordCount: Array.from(documentText.matchAll(/[\p{Script=Latin}][\p{Script=Latin}\p{Mark}'’-]*/gu)).length,
    formulaCount: countMarkdownFormulas(documentText),
    codeLineCount: countFencedCodeLines(documentText),
    paragraphCount: countMarkdownParagraphs(documentText),
  }
}

function stripLeadingFrontMatter(text: string): string {
  return text.replace(/^---[ \t]*\r?\n[\s\S]*?\r?\n---[ \t]*(?:\r?\n|$)/, '')
}

function countMarkdownFormulas(text: string): number {
  let count = 0
  const withoutCode = text.replace(/^ {0,3}(```+|~~~+)[\s\S]*?^ {0,3}\1.*$/gm, '')
  for (const _ of withoutCode.matchAll(/\$\$[\s\S]*?\$\$/g)) count += 1
  const inline = withoutCode.replace(/\$\$[\s\S]*?\$\$/g, '')
  for (const _ of inline.matchAll(/(?<!\\)\$(?!\s)(?:\\.|[^\n$])+(?<!\s)(?<!\\)\$/g)) count += 1
  return count
}

function countFencedCodeLines(text: string): number {
  let count = 0
  const lines = text.split('\n')
  let inFence = false
  for (const line of lines) {
    if (isFenceLine(line)) {
      inFence = !inFence
      continue
    }
    if (inFence) count += 1
  }
  return count
}

function countMarkdownParagraphs(text: string): number {
  let count = 0
  let inFence = false
  let inParagraph = false
  for (const line of text.split('\n')) {
    if (isFenceLine(line)) {
      if (inParagraph) {
        count += 1
        inParagraph = false
      }
      inFence = !inFence
      continue
    }
    if (inFence || line.trim().length === 0 || isBlankBlockquoteLine(line)) {
      if (inParagraph) {
        count += 1
        inParagraph = false
      }
      continue
    }
    inParagraph = true
  }
  if (inParagraph) count += 1
  return count
}

function isFenceLine(text: string): boolean {
  return /^ {0,3}(```+|~~~+)/.test(text)
}

function isTableRowLine(text: string): boolean {
  const trimmed = text.trim()
  return trimmed.includes('|') && trimmed.startsWith('|') && trimmed.endsWith('|')
}

function isBlankBlockquoteLine(text: string): boolean {
  return /^ {0,3}(?:> ?)+$/.test(text.trimEnd())
}

function shouldCountRenderedSourceLine(text: string, inFencedCode: boolean): boolean {
  if (inFencedCode) return true
  if (text.trim().length === 0) return false
  if (isBlankBlockquoteLine(text)) return false
  return true
}

function findUnsafeEmphasisInChangedLines(update: ViewUpdate): UnsafeEmphasisMatch | null {
  const checkedLines = new Set<number>()
  for (const transaction of update.transactions) {
    transaction.changes.iterChanges((fromA, toA, fromB, toB) => {
      const scanFrom = Math.min(fromB, update.state.doc.length)
      const scanTo = Math.min(Math.max(fromB, toB), update.state.doc.length)
      addChangedLines(update.state, checkedLines, scanFrom, scanTo)
      if (fromA !== toA && fromB === toB) {
        addChangedLines(update.state, checkedLines, scanFrom, scanFrom)
      }
    })
  }

  const changedLineNumbers = Array.from(checkedLines).sort((a, b) => a - b)
  if (changedLineNumbers.length === 0) return null

  const lastChangedLine = changedLineNumbers[changedLineNumbers.length - 1]!
  const fencedCodeLines = collectFencedCodeLines(update.state, lastChangedLine)
  for (const lineNumber of changedLineNumbers) {
    // 围栏代码块（含 ` ```mermaid ` 图表）内的内容是代码而非 Markdown 强调，
    // 若逐行检查会把 `**` 等符号误判为未闭合/不安全的粗斜体。
    if (fencedCodeLines.has(lineNumber)) continue
    const line = update.state.doc.line(lineNumber)
    const match = findUnsafeEmphasisInLine(update.state, line.from, line.text)
    if (match) return match
  }

  return null
}

function collectFencedCodeLines(state: EditorState, maxLine: number): Set<number> {
  const fenced = new Set<number>()
  let inFence = false
  for (let lineNumber = 1; lineNumber <= maxLine; lineNumber += 1) {
    const line = state.doc.line(lineNumber)
    if (inFence) {
      fenced.add(lineNumber)
      if (isFenceLine(line.text)) inFence = false
    } else if (isFenceLine(line.text)) {
      inFence = true
    }
  }
  return fenced
}

function addChangedLines(state: EditorState, lines: Set<number>, from: number, to: number): void {
  const startLine = state.doc.lineAt(Math.max(0, Math.min(from, state.doc.length)))
  const endLine = state.doc.lineAt(Math.max(0, Math.min(to, state.doc.length)))
  for (let lineNumber = startLine.number; lineNumber <= endLine.number; lineNumber += 1) {
    lines.add(lineNumber)
  }
}

function findUnsafeEmphasisInLine(state: EditorState, lineFrom: number, lineText: string): UnsafeEmphasisMatch | null {
  return findUnsafeEmphasisInLineForMarker(state, lineFrom, lineText, '**', 'bold')
    ?? findUnsafeEmphasisInLineForMarker(state, lineFrom, lineText, '*', 'italic')
}

function findUnsafeEmphasisInLineForMarker(
  state: EditorState,
  lineFrom: number,
  lineText: string,
  marker: '*' | '**',
  kind: UnsafeEmphasisKind,
): UnsafeEmphasisMatch | null {
  const markerLength = marker.length
  for (let openOffset = 0; openOffset <= lineText.length - markerLength; openOffset += 1) {
    if (!isEmphasisMarkerAt(lineText, openOffset, marker)) continue
    const contentStart = openOffset + markerLength
    for (let closeOffset = contentStart; closeOffset <= lineText.length - markerLength; closeOffset += 1) {
      if (!isEmphasisMarkerAt(lineText, closeOffset, marker)) continue
      const content = lineText.slice(contentStart, closeOffset)
      if (content.length === 0) continue
      const from = lineFrom + openOffset
      const to = lineFrom + closeOffset + markerLength
      const closingFrom = lineFrom + closeOffset
      if (isParsedEmphasisMarker(state, from, markerLength)
        || isParsedEmphasisMarker(state, closingFrom, markerLength)) {
        openOffset = closeOffset + markerLength - 1
        break
      }
      return { from, to, content, kind, marker }
    }
  }
  return null
}

function isParsedEmphasisMarker(state: EditorState, from: number, length: number): boolean {
  let parsed = false
  syntaxTree(state).iterate({
    from,
    to: from + length,
    enter(node) {
      if (node.name === 'EmphasisMark' && node.from <= from && node.to >= from + length) {
        parsed = true
        return false
      }
      return !parsed
    },
  })
  return parsed
}

function isEmphasisMarkerAt(lineText: string, offset: number, marker: '*' | '**'): boolean {
  if (!lineText.startsWith(marker, offset) || isEscaped(lineText, offset)) return false
  return marker === '**'
    ? lineText[offset + 2] !== '*'
    : lineText[offset - 1] !== '*' && lineText[offset + 1] !== '*'
}

function isEscaped(text: string, index: number): boolean {
  let slashCount = 0
  for (let cursor = index - 1; cursor >= 0 && text[cursor] === '\\'; cursor -= 1) {
    slashCount += 1
  }
  return slashCount % 2 === 1
}

function markdownInlineToHtmlText(markdown: string): string {
  return markdown
    .replace(/\\([\\`*_[\]{}()#+\-.!<>|])/g, '$1')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}
