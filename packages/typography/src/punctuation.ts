import type { PunctuationCompression } from './model'

export const lineStartForbidden = new Set([
  '!', '%', ')', ',', '.', ':', ';', '?', ']', '}', '’', '”',
  '、', '。', '，', '：', '；', '？', '！', '）', '】', '》',
  '〉', '』', '」', '～', '…', '—',
])

export const lineEndForbidden = new Set([
  '(', '[', '{', '‘', '“',
  '（', '【', '《', '〈', '『', '「',
])

const halfCompression = new Set([
  '，', '、', '。', '．', '：', '；', '！', '？',
])

const terminalCompression = new Set([
  '。', '！', '？',
])

const hanging = new Set([
  '，', '、', '。', '．', '：', '；', '！', '？',
])

export function compressionMode(character: string): PunctuationCompression {
  if (terminalCompression.has(character)) return 'terminal'
  if (halfCompression.has(character)) return 'half'
  return 'none'
}

export function canHang(character: string): boolean {
  return hanging.has(character)
}

export function canBreakBefore(character: string): boolean {
  return !lineStartForbidden.has(character)
}

export function canBreakAfter(character: string): boolean {
  return !lineEndForbidden.has(character)
}
