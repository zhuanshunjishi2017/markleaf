import { describe, expect, it } from 'vitest'
import { shouldInstallFrontendWheelHandler } from '../src/wheel-routing'

describe('wheel event routing', () => {
  it('keeps the blocking frontend handler for hosts that rely on the web bridge', () => {
    expect(shouldInstallFrontendWheelHandler(undefined)).toBe(true)
  })

  it('leaves macOS wheel input to the native WKWebView host', () => {
    expect(shouldInstallFrontendWheelHandler('macOS')).toBe(false)
  })
})
