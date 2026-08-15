export type DocumentType = 'markdown' | 'plainText'

export function isPlainTextDocumentType(value: unknown): value is 'plainText' {
  return value === 'plainText'
}
