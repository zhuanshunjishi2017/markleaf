import { describe, expect, it } from 'vitest'
import { isHostCommandAllowed } from '../src/host-command-policy'

describe('host command policy', () => {
  it('allows read-only source viewing, link/footnote navigation and rerendering only', () => {
    const context = { readOnly: true, documentType: 'markdown' as const }
    for (const command of ['viewMathSource', 'viewMermaidSource', 'rerenderMermaid', 'goToFootnoteDefinition'])
      expect(isHostCommandAllowed(command, context)).toBe(true)
    for (const command of ['deleteMath', 'convertMath', 'setMathNumber', 'deleteFootnoteReference', 'removeLink'])
      expect(isHostCommandAllowed(command, context)).toBe(false)
  })
  it('allows the host to change read-only mode while read-only', () => {
    expect(isHostCommandAllowed('setReadOnly', {
      readOnly: true,
      documentType: 'markdown',
    })).toBe(true)
  })
})
