#!/usr/bin/env bash
# MarkLeaf for macOS — 一键 构建 + 打包 + 运行
# 用法:
#   ./script/build_and_run.sh                 # kill + build + run
#   ./script/build_and_run.sh --debug         # 在 lldb 下运行
#   ./script/build_and_run.sh --logs          # 运行并流式查看进程日志
#   ./script/build_and_run.sh --telemetry     # 运行并按 subsystem 过滤统一日志
#   ./script/build_and_run.sh --verify        # 运行并验证进程存在
#   ./script/build_and_run.sh -- --open file.md    # 将额外参数透传给应用
set -euo pipefail

MODE="${1:-run}"
APP_NAME="MarkLeaf"
BUNDLE_ID="com.markleaf.app"
MIN_SYSTEM_VERSION="13.0"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST_DIR="$ROOT_DIR/dist"
APP_BUNDLE="$DIST_DIR/$APP_NAME.app"
APP_CONTENTS="$APP_BUNDLE/Contents"
APP_MACOS="$APP_CONTENTS/MacOS"
APP_BINARY="$APP_MACOS/$APP_NAME"
INFO_PLIST="$APP_CONTENTS/Info.plist"

# 透传参数：`--` 之后的所有参数原样交给应用
EXTRA_ARGS=()
if [[ "${1:-}" == "--" ]]; then
  shift
  MODE="run"
  EXTRA_ARGS=("$@")
fi

# ---- 1. 停止旧实例 ----
pkill -x "$APP_NAME" >/dev/null 2>&1 || true

# ---- 2. 准备资源 + 构建 ----
"$ROOT_DIR/script/prepare_resources.sh"
swift build --package-path "$ROOT_DIR"

BUILD_BINARY="$(swift build --package-path "$ROOT_DIR" --show-bin-path)/$APP_NAME"

# ---- 3. 打包 .app（SwiftPM GUI 应用必须走 bundle，直接跑裸二进制会丢 Dock 图标/激活） ----
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_MACOS"
cp "$BUILD_BINARY" "$APP_BINARY"
chmod +x "$APP_BINARY"

mkdir -p "$APP_CONTENTS/Resources"
cp -R "$ROOT_DIR/Resources/EditorWeb" "$APP_CONTENTS/Resources/EditorWeb"
cp -R "$ROOT_DIR/Resources/Styles" "$APP_CONTENTS/Resources/Styles"
if [ -f "$ROOT_DIR/Resources/AppIcon.icns" ]; then
  cp "$ROOT_DIR/Resources/AppIcon.icns" "$APP_CONTENTS/Resources/AppIcon.icns"
fi
if [ -f "$ROOT_DIR/Resources/FileIcon.icns" ]; then
  cp "$ROOT_DIR/Resources/FileIcon.icns" "$APP_CONTENTS/Resources/FileIcon.icns"
fi

cat > "$INFO_PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>MarkLeaf</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.1.3</string>
  <key>CFBundleVersion</key>
  <string>1.1.3</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MIN_SYSTEM_VERSION</string>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key>
      <string>Markdown 文档</string>
      <key>CFBundleTypeRole</key>
      <string>Editor</string>
      <key>LSHandlerRank</key>
      <string>Alternate</string>
      <key>CFBundleTypeIconFile</key>
      <string>FileIcon</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>net.daringfireball.markdown</string>
        <string>public.plain-text</string>
      </array>
    </dict>
    <dict>
      <key>CFBundleTypeName</key>
      <string>纯文本</string>
      <key>CFBundleTypeRole</key>
      <string>Editor</string>
      <key>LSHandlerRank</key>
      <string>Alternate</string>
      <key>CFBundleTypeIconFile</key>
      <string>FileIcon</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>public.plain-text</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
PLIST

# ---- 4. 临时签名（自用无需 Apple 开发者账号；ad-hoc 签名避免 Gatekeeper 拦截） ----
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP_BUNDLE" >/dev/null 2>&1 &&     echo "[build] 已 ad-hoc 签名 ($(codesign -dv --verbose=2 "$APP_BUNDLE" 2>&1 | grep -o 'Signature=.*' | head -1))" ||     echo "[build] 签名跳过（非致命）"
else
  echo "[build] codesign 不可用，跳过签名"
fi

echo "[build] $APP_BUNDLE 打包完成"

open_app() {
  /usr/bin/open -n "$APP_BUNDLE" --args "${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}"
}

case "$MODE" in
  run)
    open_app
    echo "[run] 已启动 $APP_NAME"
    ;;
  --debug|debug)
    lldb -- "$APP_BINARY" "${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}"
    ;;
  --logs|logs)
    open_app
    /usr/bin/log stream --info --style compact --predicate "process == \"$APP_NAME\""
    ;;
  --telemetry|telemetry)
    open_app
    /usr/bin/log stream --info --style compact --predicate "subsystem == \"$BUNDLE_ID\""
    ;;
  --verify|verify)
    open_app
    sleep 1
    if pgrep -x "$APP_NAME" >/dev/null; then
      echo "[verify] 进程存在: $(pgrep -x "$APP_NAME" | head -1)"
    else
      echo "[verify] 失败：进程未运行" >&2
      exit 1
    fi
    ;;
  *)
    echo "用法: $0 [run|--debug|--logs|--telemetry|--verify] [-- args...]" >&2
    exit 2
    ;;
esac
