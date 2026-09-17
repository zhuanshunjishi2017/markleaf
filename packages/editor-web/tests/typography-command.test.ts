import { describe, expect, it } from 'vitest'
import { parseTypographyProjectionCommand } from '@markleaf/editor-core'

describe('typography projection command', () => {
  it('accepts explicit on and off commands', () => {
    expect(parseTypographyProjectionCommand({ command: 'setTypographyProjection', text: '1' })).toEqual({ enabled: true })
    expect(parseTypographyProjectionCommand({ command: 'setTypographyProjection', text: '0' })).toEqual({ enabled: false })
  })

  it('rejects missing or invalid commands', () => {
    expect(parseTypographyProjectionCommand(undefined)).toBeNull()
    expect(parseTypographyProjectionCommand({ command: 'other', text: '1' })).toBeNull()
    expect(parseTypographyProjectionCommand({ command: 'setTypographyProjection', text: 'yes' })).toBeNull()
  })
})
