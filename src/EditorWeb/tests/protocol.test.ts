import { describe, expect, it } from 'vitest'
import { isHostMessage, protocolVersion } from '../src/protocol'

describe('editor protocol validation', () => {
  it('accepts a valid host command', () => {
    expect(isHostMessage({
      protocolVersion,
      type: 'command',
      documentId: 'document-id',
      revision: 3,
      payload: { command: 'toggleBold' },
    })).toBe(true)
  })

  it('accepts a host command with a response request id', () => {
    expect(isHostMessage({
      protocolVersion,
      type: 'command',
      requestId: 'insert-image-1',
      documentId: 'document-id',
      revision: 3,
      payload: { command: 'insertImage', text: 'image.png\nimage' },
    })).toBe(true)
  })

  it('rejects unknown types and invalid revisions', () => {
    expect(isHostMessage({
      protocolVersion,
      type: 'unknown',
      documentId: 'document-id',
      revision: 0,
    })).toBe(false)
    expect(isHostMessage({
      protocolVersion,
      type: 'requestSnapshot',
      documentId: 'document-id',
      revision: -1,
    })).toBe(false)
  })

  it('accepts a large plain-text document load within the supported document limit', () => {
    expect(isHostMessage({
      protocolVersion,
      type: 'loadDocument',
      documentId: 'document-id',
      revision: 0,
      payload: { markdown: 'x'.repeat(2 * 1024 * 1024), documentType: 'plainText' },
    })).toBe(true)
  })
})
