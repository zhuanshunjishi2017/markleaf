#!/usr/bin/env bash
# 准备 macOS 应用资源：
#   1) 构建 EditorWeb 前端（dist）
#   2) 复制 dist 到 Resources/EditorWeb，注入 native-shim.js（提供 window.chrome.webview 桥）
#   3) 复制 MarkLeaf 样式资源到 Resources/Styles
#   4) 从 App.png 生成 AppIcon.icns（纯 Python 构造，不依赖 iconutil）
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_DIR="$(cd "$ROOT_DIR/../.." && pwd)"
EDITOR_WEB_DIR="$REPO_DIR/packages/editor-web"
RESOURCES_DIR="$ROOT_DIR/Resources"

mkdir -p "$RESOURCES_DIR"

# ---- 1. 构建前端 ----
if [ ! -d "$EDITOR_WEB_DIR/node_modules" ]; then
  echo "[prepare] 安装前端依赖 (pnpm install)..."
  pnpm --dir "$EDITOR_WEB_DIR" install --frozen-lockfile
fi
echo "[prepare] 构建 EditorWeb (pnpm build)..."
pnpm --dir "$EDITOR_WEB_DIR" build

# ---- 2. 复制并注入桥 ----
DIST_DIR="$RESOURCES_DIR/EditorWeb"
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"
cp -R "$EDITOR_WEB_DIR/dist/." "$DIST_DIR/"

cat > "$DIST_DIR/native-shim.js" <<'SHIM_EOF'
// MarkLeaf for macOS: 在 WKWebView 中提供 window.chrome.webview (WebView2) 兼容桥。
// 同时把 assets.local 图片引用重写为 markleaf-asset://（WKWebView 无法注册 https scheme）。
(function () {
  'use strict';
  if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
    return;
  }
  var listeners = [];
  var webview = {
    postMessage: function (message) {
      window.webkit.messageHandlers.markleaf.postMessage(message);
    },
    postMessageWithAdditionalObjects: function (message) {
      // WKScriptMessageHandler 无法序列化 File 对象；MVP 先仅转发消息体。
      window.webkit.messageHandlers.markleaf.postMessage(message);
    },
    addEventListener: function (type, listener) {
      if (type === 'message' && typeof listener === 'function') {
        listeners.push(listener);
      }
    }
  };
  try {
    Object.defineProperty(window, 'chrome', { value: window.chrome || {}, writable: true, configurable: true });
    Object.defineProperty(window.chrome, 'webview', { value: webview, writable: true, configurable: true });
  } catch (e) {
    window.chrome = window.chrome || {};
    window.chrome.webview = webview;
  }
  window.addEventListener('message', function (event) {
    var data = event && event.data;
    if (data && typeof data === 'object' && typeof data.protocolVersion === 'number') {
      for (var i = 0; i < listeners.length; i++) {
        try { listeners[i]({ data: data }); } catch (e) { /* ignore */ }
      }
    }
  });

  // ---- 图片资源重写：https://assets.local/image?path=... → markleaf-asset://image?path=... ----
  function rewriteImages() {
    var images = document.querySelectorAll('img[src^="https://assets.local/image?path="]');
    for (var i = 0; i < images.length; i++) {
      var img = images[i];
      var src = img.getAttribute('src');
      var rewritten = 'markleaf-asset://image?' + src.slice(src.indexOf('path='));
      img.setAttribute('src', rewritten);
    }
  }
  if (window.MutationObserver) {
    var observer = new MutationObserver(function () { rewriteImages(); });
    observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
  }
  setInterval(rewriteImages, 800);
  setInterval(rewriteImages, 800);

  // ---- macOS：缩放滚轮事件统一入口 ----
  // ⌘+滚轮 → source 'wheel'（离散跳档）；Ctrl+滚轮/触控板捏合 → source 'pinch'（连续平滑）。
  // stopImmediatePropagation 阻止前端自带 ctrlKey 处理器重复上报。
  window.addEventListener('wheel', function (event) {
    if (event.metaKey || event.ctrlKey) {
      event.preventDefault();
      event.stopImmediatePropagation();
      webview.postMessage({
        protocolVersion: 1,
        type: 'zoomWheel',
        documentId: 'shim',
        revision: 0,
        payload: {
          deltaY: event.deltaY,
          source: event.metaKey ? 'wheel' : 'pinch',
          clientX: event.clientX,
          clientY: event.clientY
        }
      });
    }
  }, { passive: false });
})();
SHIM_EOF

if ! grep -q 'native-shim.js' "$DIST_DIR/index.html"; then
  python3 - "$DIST_DIR/index.html" <<'PY_EOF'
import sys
path = sys.argv[1]
with open(path, "r", encoding="utf-8") as f:
    html = f.read()
html = html.replace("</head>", '<script src="native-shim.js"></script></head>', 1)
# 允许 markleaf-asset: 图片源（本地图片资源服务）
html = html.replace(
    "img-src 'self' https: https://assets.local data:",
    "img-src 'self' https: https://assets.local markleaf-asset: data:",
    1)
with open(path, "w", encoding="utf-8") as f:
    f.write(html)
print("[prepare] index.html 已注入 native-shim.js 并更新 CSP")
PY_EOF
fi

# ---- 3. 样式资源 ----
STYLES_DIR="$RESOURCES_DIR/Styles"
rm -rf "$STYLES_DIR"
mkdir -p "$STYLES_DIR"
cp -R "$REPO_DIR/packages/styles/." "$STYLES_DIR/"

# ---- 4. 应用图标 / 文件图标 ----
build_icns() {
  local src="$1" out="$2"
  local icon_dir="$ROOT_DIR/.icon-build"
  rm -rf "$icon_dir"
  mkdir -p "$icon_dir"
  for size in 16 32 64 128 256 512 1024; do
    sips -z "$size" "$size" "$src" --out "$icon_dir/icon_$size.png" >/dev/null
  done
  python3 - "$icon_dir" "$out" <<'ICNS_EOF'
import struct, sys, os

ICON_DIR, OUT = sys.argv[1], sys.argv[2]

def png_bytes(path):
    with open(path, "rb") as f:
        return f.read()

entries = [
    ("icp4", 16), ("icp5", 32), ("icp6", 64),
    ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
]
chunks = []
for tag, size in entries:
    p = os.path.join(ICON_DIR, f"icon_{size}.png")
    if os.path.exists(p):
        chunks.append((tag, png_bytes(p)))

total = 8 + sum(8 + len(d) for _, d in chunks)
out = b"icns" + struct.pack(">I", total)
for tag, data in chunks:
    out += tag.encode() + struct.pack(">I", 8 + len(data)) + data
with open(OUT, "wb") as f:
    f.write(out)
print(f"[prepare] {os.path.basename(OUT)} 已生成 ({len(chunks)} 个尺寸)")
ICNS_EOF
  rm -rf "$icon_dir"
}

if [ -f "$ROOT_DIR/appicon.png" ]; then
  build_icns "$ROOT_DIR/appicon.png" "$RESOURCES_DIR/AppIcon.icns"
elif [ -f "$REPO_DIR/appicon.png" ]; then
  build_icns "$REPO_DIR/appicon.png" "$RESOURCES_DIR/AppIcon.icns"
fi

# 文档类型图标（Finder 中 .md/.txt 的图标）
if [ -f "$ROOT_DIR/fileicon.png" ]; then
  build_icns "$ROOT_DIR/fileicon.png" "$RESOURCES_DIR/FileIcon.icns"
elif [ -f "$REPO_DIR/fileicon.png" ]; then
  build_icns "$REPO_DIR/fileicon.png" "$RESOURCES_DIR/FileIcon.icns"
fi

echo "[prepare] 资源准备完成"
