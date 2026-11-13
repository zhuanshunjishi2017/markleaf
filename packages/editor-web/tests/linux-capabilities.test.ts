import { afterEach, describe, expect, it, vi } from 'vitest'
import type { EditorMessage, HostMessage } from '../src/protocol'
import { nativeImageResources, resolveNativeCapabilities } from '../src/native-capabilities'

function stubHost(platform: 'Linux' | 'macOS' | 'Windows'): void {
  window.chrome = {
    webview: {
      hostPlatform: platform,
      postMessage(_message: EditorMessage) {},
      addEventListener(_type: 'message', _listener: (event: MessageEvent<HostMessage>) => void) {},
    },
  }
}

describe('linux host capabilities', () => {
  afterEach(() => {
    delete (window as { chrome?: unknown }).chrome
    vi.restoreAllMocks()
    vi.resetModules()
  })

  it('resolves the desktop linux capability set', () => {
    const capabilities = resolveNativeCapabilities('Linux')
    expect(capabilities.usesThemedVisualSelection).toBe(true)
    expect(capabilities.primaryActivationModifier).toBe('ctrl')
    expect(capabilities.installsFrontendWheelHandler).toBe(true)
    expect(capabilities.imageCaptureMode).toBe('fullSurface')
  })

  it('routes image resources through the markleaf-asset scheme', () => {
    stubHost('Linux')

    const relative = nativeImageResources.resolve('./images/pic.png')
    expect(relative).toBe('markleaf-asset://local/image?path=.%2Fimages%2Fpic.png')
    expect(nativeImageResources.originalPath?.(relative)).toBe('./images/pic.png')

    const absolute = nativeImageResources.resolve('/home/user/docs/pic.png')
    expect(absolute).toBe('markleaf-asset://local/image?path=%2Fhome%2Fuser%2Fdocs%2Fpic.png')
    expect(nativeImageResources.originalPath?.(absolute)).toBe('/home/user/docs/pic.png')
  })

  it('keeps remote and data urls untouched on linux', () => {
    stubHost('Linux')

    expect(nativeImageResources.resolve('https://example.com/a.png')).toBe('https://example.com/a.png')
    expect(nativeImageResources.resolve('data:image/png;base64,AAA')).toBe('data:image/png;base64,AAA')
  })

  it('returns null for non-asset urls and non-image endpoints', () => {
    expect(nativeImageResources.originalPath?.('https://assets.local/image?path=x.png')).toBe('x.png')
    expect(nativeImageResources.originalPath?.('markleaf-asset://local/other?path=x.png')).toBeNull()
    expect(nativeImageResources.originalPath?.('markleaf-asset://local/image')).toBeNull()
    expect(nativeImageResources.originalPath?.('not a url')).toBeNull()
  })

  it('keeps other hosts on their existing asset origins', () => {
    stubHost('macOS')
    expect(nativeImageResources.resolve('a.png')).toBe('https://assets.local/image?path=a.png')

    stubHost('Windows')
    expect(nativeImageResources.resolve('a.png')).toBe('https://assets.example/image?path=a.png')
  })
})
