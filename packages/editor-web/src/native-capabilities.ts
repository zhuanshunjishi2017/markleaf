import type { HostCapabilities, ImageResourceResolver } from '@markleaf/editor-core'

export type NativeHostPlatform = 'macOS' | 'Windows' | 'Android' | 'Linux'

export function resolveNativeCapabilities(hostPlatform?: NativeHostPlatform): HostCapabilities & { imageCaptureMode: 'scroll' | 'fullSurface' } {
  const macOS = hostPlatform === 'macOS'
  const android = hostPlatform === 'Android'
  return {
    // 统一选区模型：所有原生宿主使用主题化托管选区（::highlight API，
    // Chromium 与 WebKit 均支持），单一绘制源消灭引擎交替。原生引擎仅
    // 保留给 VS Code webview 等无 ::highlight 能力的环境。
    usesThemedVisualSelection: true,
    primaryActivationModifier: macOS ? 'meta' : 'ctrl',
    // Android 触屏无滚轮：wheel 缩放处理仅装在桌面宿主。
    // Linux 桌面由现有表达式自然推导：主修饰键 ctrl、安装 wheel 处理、
    // 全文档截图（WebKitGTK FULL_DOCUMENT 快照），无需专属分支。
    installsFrontendWheelHandler: !macOS && !android,
    imageCaptureMode: macOS ? 'scroll' : 'fullSurface',
  }
}

function assetOrigin(): string {
  switch (window.chrome?.webview?.hostPlatform) {
    case 'Windows':
      return 'https://assets.example'
    // Linux 宿主通过 WebKitGTK 注册的 markleaf-asset:// 自定义 scheme
    // 提供文档图片；非特殊 scheme 的 URL 需要携带 host 才能稳定解析。
    case 'Linux':
      return 'markleaf-asset://local'
    default:
      return 'https://assets.local'
  }
}

export const nativeImageResources: ImageResourceResolver = {
  resolve(path) {
    if (/^(https?:|data:|blob:)/i.test(path)) return path
    let decoded = path
    try { decoded = decodeURIComponent(path) } catch { /* A literal percent is part of the path. */ }
    return `${assetOrigin()}/image?path=${encodeURIComponent(decoded)}`
  },
  originalPath(value) {
    let url: URL
    try { url = new URL(value) } catch { return null }
    // 非特殊 scheme（markleaf-asset:）的 url.origin 是字符串 "null"，
    // 必须按 protocol 判等，不能沿用 https origin 比较。
    if (url.protocol === 'markleaf-asset:') {
      return url.pathname === '/image' ? url.searchParams.get('path') : null
    }
    return (url.origin === 'https://assets.local' || url.origin === 'https://assets.example')
      && url.pathname === '/image'
      ? url.searchParams.get('path') : null
  },
}
