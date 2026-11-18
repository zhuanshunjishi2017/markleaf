import type { Editor } from '@markleaf/editor-core'
import { documentReplacement } from './vscode-text-edit'

type Doc = Editor['state']['doc']
type Transaction = Editor['state']['tr']

// Only index documents whose entire source maps one-to-one to top-level text
// paragraphs and ATX headings. Lists, references, marks and multi-line blocks
// take the normal Markdown parser path.
function locate(doc: Doc, source: string, target: { offset: number } | { index: number }) {
  let index = 0, pos = 0, candidate: { start: number; line: string; pos: number; text: string } | undefined
  let previousEnd = -1
  for (const match of source.matchAll(/[^\r\n]+/g)) {
    const line = match[0], start = match.index!
    if (!line.trim()) continue
    if (index >= doc.childCount || (previousEnd >= 0 && !/\n[^\S\n]*\n/.test(source.slice(previousEnd, start)))) return
    const node = doc.child(index)
    if (node.childCount !== 1 || !node.firstChild?.isText || node.firstChild.marks.length) return
    const text = node.firstChild.text!
    const isParagraph = node.type.name === 'paragraph'
    const prefix = node.type.name === 'heading' ? '#'.repeat(node.attrs.level) + ' ' : ''
    if (!isParagraph && !prefix || line !== prefix + text && line.trimEnd() !== prefix + text) return
    if (isParagraph && ('index' in target ? index === target.index : target.offset >= start && target.offset <= start + line.length)) {
      candidate = { start, line, pos: pos + 1, text }
    }
    pos += node.nodeSize
    index++
    previousEnd = start + line.length
  }
  return index === doc.childCount ? candidate : undefined
}

// The isolated line must still parse as one literal paragraph, including the
// parser's whitespace rules. Source constructs with cross-block semantics
// (reference definitions, HTML, fences, etc.) can never enter this path.
function parsedLiteral(editor: Editor, line: string): string | undefined {
  if (!/^[\p{L}\p{N}\p{M} .,!?，。！？、；：]+$/u.test(line) || /^\s| {2}$/.test(line)) return
  const blocks = editor.markdown?.parse(line).content
  const block = blocks?.[0], child = block?.content?.[0]
  if (blocks?.length === 1 && block?.type === 'paragraph' && block.content?.length === 1 && child?.type === 'text' && !child.marks?.length) return child.text
}

export function applyPlainSourceEdit(editor: Editor, before: string, after: string): boolean {
  const change = documentReplacement(before, after)
  if (!change) return true
  const block = locate(editor.state.doc, before, { offset: change.start })
  if (!block || change.end > block.start + block.line.length || parsedLiteral(editor, block.line) !== block.text) return false
  const next = parsedLiteral(editor, block.line.slice(0, change.start - block.start) + change.text + block.line.slice(change.end - block.start))
  if (next === undefined) return false
  const edit = documentReplacement(block.text, next)
  if (edit) editor.view.dispatch(editor.state.tr.insertText(edit.text, block.pos + edit.start, block.pos + edit.end)
    .setMeta('addToHistory', false).setMeta('preventUpdate', true))
  return true
}

export function sourceForPlainEdit(editor: Editor, transaction: Transaction, before: string): string | undefined {
  if (transaction.steps.length !== 1) return
  let range: { from: number; oldSize: number; newSize: number } | undefined
  let count = 0
  transaction.steps[0]!.getMap().forEach((from, to, newFrom, newTo) => {
    range = { from, oldSize: to - from, newSize: newTo - newFrom }
    count++
  })
  if (!range || count !== 1) return
  const { from, oldSize, newSize } = range
  const old = transaction.before.resolve(from), next = transaction.doc.resolve(from)
  if (old.depth !== 1 || next.depth !== 1 || old.parent.type.name !== 'paragraph' || next.parent.type !== old.parent.type
    || old.parentOffset + oldSize > old.parent.content.size || next.parentOffset + newSize > next.parent.content.size
    || next.parent.childCount !== 1 || next.parent.firstChild?.marks.length) return
  const block = locate(transaction.before, before, { index: old.index(0) })
  if (!block || parsedLiteral(editor, block.line) !== block.text) return
  const inserted = next.parent.textBetween(next.parentOffset, next.parentOffset + newSize)
  const line = block.line.slice(0, old.parentOffset) + inserted + block.line.slice(old.parentOffset + oldSize)
  if (parsedLiteral(editor, line) !== next.parent.textContent) return
  return before.slice(0, block.start) + line + before.slice(block.start + block.line.length)
}
