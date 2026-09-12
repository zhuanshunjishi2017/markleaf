import type { HostCapabilities, ImageResourceResolver } from '@markleaf/editor-core'

export function resolveNativeCapabilities(hostPlatform?: 'macOS'): HostCapabilities & { imageCaptureMode: 'scroll' | 'fullSurface' } {
  const macOS = hostPlatform === 'macOS'
  return {
    usesThemedVisualSelection: macOS,
    primaryActivationModifier: macOS ? 'meta' : 'ctrl',
    installsFrontendWheelHandler: !macOS,
    imageCaptureMode: macOS ? 'scroll' : 'fullSurface',
  }
}

export const nativeImageResources: ImageResourceResolver = {
  resolve(path) {
    if (/^(https?:|data:|blob:)/i.test(path)) return path
    let decoded = path
    try { decoded = decodeURIComponent(path) } catch { /* A literal percent is part of the path. */ }
    return `https://assets.local/image?path=${encodeURIComponent(decoded)}`
  },
  originalPath(value) {
    let url: URL
    try { url = new URL(value) } catch { return null }
    return url.origin === 'https://assets.local' && url.pathname === '/image'
      ? url.searchParams.get('path') : null
  },
}
