import { describe, expect, it, vi } from 'vitest'
import { TextDocumentSync } from '../src/vscode-sync'
import type { DocumentSnapshot, WebviewMessage } from '../src/vscode-protocol'
import { documentReplacement, normalizeDocumentMarkdown } from '../../../apps/vscode/src/document-edit'

const snapshot = (markdown: string, version = 1): DocumentSnapshot => ({ type: 'document', markdown, version, writable: true })
function client() {
  const messages: WebviewMessage[] = []
  const render = vi.fn((document: DocumentSnapshot) => document.markdown.trimEnd())
  const sync = new TextDocumentSync({ post: message => messages.push(message), render, status() {} })
  sync.receiveDocument(snapshot('hello\n'))
  return { sync, messages, render }
}

describe('VS Code document synchronization', () => {
  it('does not write on opening, even when serialization normalizes the source', () => {
    const { sync, messages } = client()
    expect(sync.pending).toBe(false)
    sync.change('hello')
    expect(messages).toEqual([])
  })

  it('acknowledges edits without rendering or disturbing the current editor', () => {
    const { sync, messages, render } = client()
    sync.change('hello world')
    expect(messages).toEqual([{ type: 'edit', sequence: 1, baseVersion: 1, markdown: 'hello world' }])
    sync.accept(1, 2)
    expect(render).toHaveBeenCalledTimes(1)
    expect(sync.pending).toBe(false)
  })

  it('queues the latest typing behind the current edit and uses the acknowledged version', () => {
    const { sync, messages } = client()
    sync.change('hello a')
    sync.change('hello ab')
    sync.change('hello abc')
    expect(messages).toHaveLength(1)
    sync.accept(1, 2)
    expect(messages.at(-1)).toEqual({ type: 'edit', sequence: 2, baseVersion: 2, markdown: 'hello abc' })
    sync.accept(2, 3)
    expect(sync.pending).toBe(false)
  })

  it('renders source edits and undo without writing them back', () => {
    const { sync, messages, render } = client()
    sync.receiveDocument(snapshot('changed from source', 2))
    sync.receiveDocument(snapshot('hello', 3))
    expect(render).toHaveBeenCalledTimes(3)
    expect(sync.markdown).toBe('hello')
    expect(messages).toEqual([])
  })

  it('keeps local typing on an external conflict and prevents stale edits from continuing', () => {
    const { sync, messages, render } = client()
    sync.change('my draft')
    sync.change('my complete draft')
    sync.receiveDocument(snapshot('external change', 2))
    sync.accept(1, 3)
    expect(sync.conflict).toBeTruthy()
    expect(sync.markdown).toBe('my complete draft')
    expect(render).toHaveBeenCalledTimes(1)
    expect(messages).toHaveLength(1)
  })

  it('preserves a rejected edit and fails save flush rather than reporting synchronization', () => {
    const { sync, messages } = client()
    sync.change('draft')
    sync.flush(42)
    sync.reject(1, 'File is read-only')
    expect(sync.conflict).toBe('File is read-only')
    expect(sync.markdown).toBe('draft')
    expect(messages.at(-1)).toEqual({ type: 'flushed', requestId: 42, success: false })
  })

  it('submits a committed IME composition as one edit and waits for its acknowledgement before save', () => {
    const { sync, messages } = client()
    sync.setComposing(true)
    sync.change('hello n')
    sync.change('hello ni')
    sync.flush(1)
    expect(messages).toEqual([])
    sync.change('hello 你')
    sync.setComposing(false)
    expect(messages).toEqual([{ type: 'edit', sequence: 1, baseVersion: 1, markdown: 'hello 你' }])
    sync.accept(1, 2)
    expect(messages.at(-1)).toEqual({ type: 'flushed', requestId: 1, success: true })
  })

  it('does not replace a composing editor when a source change arrives', () => {
    const { sync, render, messages } = client()
    sync.setComposing(true)
    sync.receiveDocument(snapshot('external', 2))
    expect(render).toHaveBeenCalledTimes(1)
    sync.change('hello 中文')
    sync.setComposing(false)
    expect(sync.conflict).toBeTruthy()
    expect(sync.markdown).toBe('hello 中文')
    expect(messages).toEqual([])
  })

  it('can reload the authoritative document after opening the conflict as a draft', () => {
    const { sync, messages } = client()
    sync.change('draft')
    sync.reject(1, 'conflict')
    sync.reset(snapshot('external', 4))
    expect(sync.conflict).toBeUndefined()
    expect(sync.pending).toBe(false)
    sync.change('external plus local')
    expect(messages.at(-1)).toMatchObject({ type: 'edit', baseVersion: 4 })
  })

  it('ignores stale confirmations and stale snapshots', () => {
    const { sync, messages } = client()
    sync.receiveDocument(snapshot('newer', 5))
    sync.receiveDocument(snapshot('old', 2))
    sync.change('newer edit')
    sync.accept(100, 99)
    expect(sync.pending).toBe(true)
    expect(messages).toHaveLength(1)
    expect(sync.markdown).toBe('newer edit')
  })

  it('does not bind a failed render to the newer document version', () => {
    const { sync, messages, render } = client()
    render.mockImplementationOnce(() => { throw new Error('Invalid document') })
    expect(() => sync.receiveDocument(snapshot('cannot render', 2))).toThrow('Invalid document')
    expect(sync.markdown).toBe('hello')
    sync.change('old view edit')
    expect(messages.at(-1)).toMatchObject({ type: 'edit', baseVersion: 1 })
  })
})

describe('VS Code text replacements', () => {
  it.each([
    ['hello', 'hello world'], ['😀', '😁'], ['a\r\nb', 'a\r\nc'],
    ['你好，世界', '你好，朋友'], ['abc', ''], ['', 'abc'], ['abc', 'ac'],
  ])('reconstructs %j as %j without rewriting shared text', (before, after) => {
    const edit = documentReplacement(before, after)!
    expect(before.slice(0, edit.start) + edit.text + before.slice(edit.end)).toBe(after)
    expect(edit.text).not.toMatch(/^[\uDC00-\uDFFF]/)
  })

  it('retains CRLF and an existing final newline', () => {
    expect(normalizeDocumentMarkdown('# Title\n\nnew', '# Title\r\n\r\nold\r\n', '\r\n')).toBe('# Title\r\n\r\nnew\r\n')
    expect(normalizeDocumentMarkdown('new', 'old', '\n')).toBe('new')
    expect(documentReplacement('same', 'same')).toBeUndefined()
  })
})
