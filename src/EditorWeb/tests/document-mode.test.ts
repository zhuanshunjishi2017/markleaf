import { describe, expect, it } from 'vitest'
import { isPlainTextDocumentType } from '../src/document-mode'

describe('document mode', () => {
  it('identifies plain text documents without parsing their contents as Markdown', () => {
    expect(isPlainTextDocumentType('plainText')).toBe(true)
    expect(isPlainTextDocumentType('markdown')).toBe(false)
    expect(isPlainTextDocumentType(undefined)).toBe(false)
  })
})
