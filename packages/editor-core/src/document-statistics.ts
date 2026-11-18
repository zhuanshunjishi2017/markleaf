import type { Node } from '@tiptap/pm/model'

type TextCounts = { length: number; nonWhitespace: number; breaks: number; lastLineLength: number }
function textCounts(text: string): TextCounts {
  let length = 0, nonWhitespace = 0, breaks = 0, lastLineLength = 0
  for (const char of text) {
    length++
    if (!/\s/u.test(char)) nonWhitespace++
    if (char === '\n') { breaks++; lastLineLength = 0 } else lastLineLength++
  }
  return { length, nonWhitespace, breaks, lastLineLength }
}

function blockStatistics(node: Node) {
  const text = node.textBetween(0, node.content.size, '\n', '\n')
  let formulaCount = 0, codeLineCount = 0, paragraphCount = 0
  function visit(child: Node) {
    if (child.type.name === 'frontMatter') return false
    if (child.type.name === 'mathInline' || child.type.name === 'mathBlock') formulaCount++
    if (child.type.name === 'codeBlock' || child.type.name === 'mermaid') codeLineCount += child.textContent.split('\n').length
    if (child.type.name === 'paragraph') paragraphCount++
    return true
  }
  visit(node)
  node.descendants(visit)
  return { ...textCounts(text), formulaCount, codeLineCount, paragraphCount,
    cjkCharacterCount: text.match(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]/gu)?.length ?? 0,
    westernWordCount: text.match(/[\p{Script=Latin}][\p{Script=Latin}\p{Mark}'’-]*/gu)?.length ?? 0 }
}

const blocks = new WeakMap<Node, ReturnType<typeof blockStatistics>>()
function counts(node: Node) {
  let result = blocks.get(node)
  if (!result) { result = blockStatistics(node); blocks.set(node, result) }
  return result
}

function summarize(doc: Node) {
  const result = { totalCharacterCount: 0, nonWhitespaceCharacterCount: 0, cjkCharacterCount: 0,
    westernWordCount: 0, formulaCount: 0, codeLineCount: 0, paragraphCount: 0 }
  let hasTextBlock = false
  doc.forEach(node => {
    if (node.type.name === 'frontMatter') return
    const block = counts(node)
    if (node.content.size) {
      result.totalCharacterCount += block.length + Number(hasTextBlock)
      hasTextBlock = true
    }
    result.nonWhitespaceCharacterCount += block.nonWhitespace
    result.cjkCharacterCount += block.cjkCharacterCount
    result.westernWordCount += block.westernWordCount
    result.formulaCount += block.formulaCount
    result.codeLineCount += block.codeLineCount
    result.paragraphCount += block.paragraphCount
  })
  return result
}
const documents = new WeakMap<Node, ReturnType<typeof summarize>>()
export function documentStatistics(doc: Node) {
  let result = documents.get(doc)
  if (!result) { result = summarize(doc); documents.set(doc, result) }
  return result
}

// Full top-level nodes reuse their immutable counts. Only the two clipped ends
// of a selection need text extraction. Separators match textBetween's block join.
export function rangeStatistics(doc: Node, from: number, to: number) {
  let line = 1, column = 1, characters = 0, hasText = false
  if (from < to) doc.forEach((node, offset) => {
    if (node.type.name === 'frontMatter') return
    const start = Math.max(0, from - offset - 1), end = Math.min(node.content.size, to - offset - 1)
    if (start >= end) return
    const block = start === 0 && end === node.content.size ? counts(node) : textCounts(node.textBetween(start, end, '\n', '\n'))
    line += block.breaks + Number(hasText)
    column = block.lastLineLength + 1
    characters += block.nonWhitespace
    hasText = true
  })
  return { line, column, characters }
}
