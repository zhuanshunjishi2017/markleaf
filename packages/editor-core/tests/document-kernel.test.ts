import { describe, expect, it } from 'vitest'
import { TextDecoder } from 'node:util'
import { readFileSync } from 'node:fs'
import { decodeDocument, detectEncoding, detectNewLine, encodeDocument, normalizeNewLines, readPreview, type NativeCodec } from '../src/document/encoding'
import { documentPlainText, documentSnippet } from '../src/document/projection'
import { dirtyAfterSave, ownsRecoveryFile, parseRecovery, serializeRecovery, validateSave } from '../src/document/transactions'

const codec: NativeCodec = {
  decode(bytes, name) {
    if (!['utf-8', 'utf-16le', 'utf-16be'].includes(name)) return null
    try { return new TextDecoder(name, { fatal: true, ignoreBOM: true }).decode(new Uint8Array(bytes)) } catch { return null }
  },
  encode(text, name) {
    if (name === 'utf-8') return [...Buffer.from(text, 'utf8')]
    if (name === 'utf-16le') return [...Buffer.from(text, 'utf16le')]
    if (name === 'utf-16be') return [...Buffer.from(text, 'utf16le').swap16()]
    return null
  },
}

describe('DOM-free document semantics', () => {
  it('uses one Markdown projection while retaining literal plain text and code', () => {
    expect(documentPlainText('---\ntitle: hidden\n---\n# Title **bold** [link](https://example.test)', true)).toBe('Title bold link')
    expect(documentPlainText('```text\n**literal** &amp;\n```', true)).toBe('**literal** &amp;')
    expect(documentPlainText('![description](image.png) $x_1$ ==marked==', true)).toBe('description x_1 marked')
    expect(documentPlainText('**literal** &amp; <tag>', false)).toBe('**literal** &amp; <tag>')
  })
  it('consumes the shared projection examples', () => {
    const fixture = JSON.parse(readFileSync(new URL('../../../tests/fixtures/workspace-text.json', import.meta.url), 'utf8'))
    for (const item of fixture.projections) expect(documentPlainText(item.source, item.isMarkdown), item.name).toBe(item.expected)
  })
  it('returns snippets without cutting an emoji', () => {
    expect(documentSnippet('😀abc needle xyz', 'needle')).toBe('c needle xyz')
    expect(documentSnippet('nothing', 'absent')).toBeNull()
  })
  it('round-trips both UTF-16 byte orders and their BOM variants', () => {
    for (const id of ['utf-8', 'utf-8-bom', 'utf-16', 'utf-16-bom', 'utf-16be', 'utf-16be-bom']) {
      const bytes = encodeDocument('A 中文 😀', id, codec)
      expect(decodeDocument(bytes, id, codec)).toBe('A 中文 😀')
      if (id.endsWith('-bom')) expect(detectEncoding(bytes, codec).id).toBe(id)
    }
    expect(() => detectEncoding([255], codec)).toThrow()
  })
  it('preserves mixed line endings until a target style is selected', () => {
    const source = 'one\r\ntwo\nthree\r'
    expect(detectNewLine(source)).toBe('Mixed')
    expect(normalizeNewLines(source, 'Mixed')).toBe(source)
    expect(normalizeNewLines(source, 'CRLF')).toBe('one\r\ntwo\r\nthree\r\n')
  })
  it('trims an incomplete preview character only for a bounded prefix', () => {
    const prefix = [...Buffer.from('text 中', 'utf8')].slice(0, -1)
    expect(readPreview(prefix, true, codec)).toBe('text ')
    expect(() => readPreview(prefix, false, codec)).toThrow()
  })
  it('rejects external changes and preserves edits arriving during save', () => {
    const facts = { sameTarget: true, forceOverwrite: false, targetExists: true, acceptedVersion: true, contentChanged: true, readOnly: false }
    expect(() => validateSave(facts)).toThrow('changed on disk')
    expect(() => validateSave({ ...facts, forceOverwrite: true })).not.toThrow()
    expect(() => validateSave({ ...facts, forceOverwrite: true, readOnly: true })).toThrow('read-only')
    expect(dirtyAfterSave('9007199254740992', '9007199254740993')).toBe(true)
    expect(dirtyAfterSave('4', '4')).toBe(false)
  })
  it('keeps empty snapshots and exact revisions in one recovery envelope', () => {
    const record = { documentId: '00000000-0000-0000-0000-000000000001', documentPath: null, markdown: '', revision: '9007199254740993', timestamp: '2026-09-11T00:00:00.000Z', displayName: null }
    expect(parseRecovery(serializeRecovery(record))).toEqual(record)
    expect(parseRecovery(JSON.stringify({ ...record, revision: 3 }), 'legacy').markdown).toBe('legacy')
    expect(() => parseRecovery('{"schema":2}')).toThrow('schema')
    expect(ownsRecoveryFile('doc-42-00000000000000000000000000000001.recovery.json', record.documentId)).toBe(true)
    expect(ownsRecoveryFile('unrelated.json')).toBe(false)
  })
})
