import type { HostCapabilities, ImageResourceResolver } from '@markleaf/editor-core'

export function resolveNativeCapabilities(hostPlatform?: 'macOS' | 'Windows'): HostCapabilities & { imageCaptureMode: 'scroll' | 'fullSurface' } {
  const macOS = hostPlatform === 'macOS'
  return {
    // 统一选区模型：所有原生宿主使用主题化托管选区（::highlight API，
    // Chromium 与 WebKit 均支持），单一绘制源消灭引擎交替。原生引擎仅
    // 保留给 VS Code webview 等无 ::highlight 能力的环境。
    usesThemedVisualSelection: true,
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
