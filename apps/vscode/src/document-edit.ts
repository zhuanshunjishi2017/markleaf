/** Preserve the document's EOL convention and existing final newline. */
export function normalizeDocumentMarkdown(markdown: string, original: string, eol: string): string {
  let normalized = markdown.replace(/\r\n?/g, '\n')
  if (original.endsWith('\n') && !normalized.endsWith('\n')) normalized += '\n'
  return eol === '\r\n' ? normalized.replace(/\n/g, '\r\n') : normalized
}

/** A single minimal contiguous replacement, expressed as UTF-16 offsets. */
export function documentReplacement(before: string, after: string): { start: number; end: number; text: string } | undefined {
  if (before === after) return undefined
  let start = 0
  while (start < before.length && start < after.length && before[start] === after[start]) start++
  // Do not put VS Code ranges inside a surrogate pair or a CRLF sequence.
  if (start > 0 && (/[\uD800-\uDBFF]/.test(before[start - 1]!) || before[start - 1] === '\r')) start--
  let end = before.length
  let afterEnd = after.length
  while (end > start && afterEnd > start && before[end - 1] === after[afterEnd - 1]) { end--; afterEnd-- }
  if (end < before.length && (/[\uDC00-\uDFFF]/.test(before[end]!) || before[end] === '\n')) { end++; afterEnd++ }
  return { start, end, text: after.slice(start, afterEnd) }
}
