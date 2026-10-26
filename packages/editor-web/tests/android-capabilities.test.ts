import { afterEach, describe, expect, it, vi } from 'vitest'
import { resolveNativeCapabilities } from '../src/native-capabilities'

describe('android host capabilities', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  it('resolves themed selection with ctrl modifier for Android', () => {
    const capabilities = resolveNativeCapabilities('Android')
    expect(capabilities.usesThemedVisualSelection).toBe(true)
    expect(capabilities.primaryActivationModifier).toBe('ctrl')
    expect(capabilities.installsFrontendWheelHandler).toBe(false)
    expect(capabilities.imageCaptureMode).toBe('fullSurface')
  })

  it('keeps desktop platforms unchanged', () => {
    expect(resolveNativeCapabilities('macOS').primaryActivationModifier).toBe('meta')
    expect(resolveNativeCapabilities('Windows').primaryActivationModifier).toBe('ctrl')
    expect(resolveNativeCapabilities().installsFrontendWheelHandler).toBe(true)
  })
})
