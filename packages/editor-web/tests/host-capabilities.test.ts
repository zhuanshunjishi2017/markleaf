import { describe, expect, it } from 'vitest'
import {
  hasPrimaryActivationModifier,
  resolveHostCapabilities,
} from '../src/host-capabilities'

describe('host capabilities', () => {
  it('uses themed selection and Command on macOS', () => {
    expect(resolveHostCapabilities('macOS')).toEqual({
      usesThemedVisualSelection: true,
      primaryActivationModifier: 'meta',
      installsFrontendWheelHandler: false,
    })
  })

  it('defaults an unknown host to the safe Windows behavior', () => {
    expect(resolveHostCapabilities(undefined)).toEqual({
      usesThemedVisualSelection: false,
      primaryActivationModifier: 'ctrl',
      installsFrontendWheelHandler: true,
    })
  })

  it('does not treat Control-click as primary activation on macOS', () => {
    const capabilities = resolveHostCapabilities('macOS')

    expect(hasPrimaryActivationModifier(
      { metaKey: false, ctrlKey: true },
      capabilities,
    )).toBe(false)
    expect(hasPrimaryActivationModifier(
      { metaKey: true, ctrlKey: false },
      capabilities,
    )).toBe(true)
  })
})
