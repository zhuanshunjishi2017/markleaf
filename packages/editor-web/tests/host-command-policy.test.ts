import { describe, expect, it } from 'vitest'
import { isHostCommandAllowed } from '../src/host-command-policy'

describe('host command policy', () => {
  it('allows only non-mutating Mermaid and display commands in read-only documents', () => {
    expect(isHostCommandAllowed('setCodeHighlightVisible', {
      readOnly: true,
      documentType: 'markdown',
    })).toBe(true)
    expect(isHostCommandAllowed('rerenderAllMermaid', {
      readOnly: true,
      documentType: 'markdown',
    })).toBe(true)

    for (const command of ['insertMermaid', 'editMermaid', 'deleteMermaid']) {
      expect(isHostCommandAllowed(command, {
        readOnly: true,
        documentType: 'markdown',
      })).toBe(false)
    }
  })

  it('rejects Mermaid insertion for a writable plain-text document', () => {
    expect(isHostCommandAllowed('insertMermaid', {
      readOnly: false,
      documentType: 'plainText',
    })).toBe(false)
    expect(isHostCommandAllowed('insertMermaid', {
      readOnly: false,
      documentType: 'markdown',
    })).toBe(true)
  })
})
