#!/usr/bin/env bash
# AppImage 打包(实验性;见 spec §11):ubuntu-24.04 容器内 linuxdeploy
# 捆绑 GTK/Adw/WebKitGTK 运行时栈。产物 MarkLeaf-x86_64.AppImage。
# 前置:宿主已跑 prepare_resources.sh 与 cargo build --release。
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ARCH="${ARCH:-x86_64}"
OUT="${1:-$REPO_DIR/artifacts/linux}"
APPDIR="$OUT/AppDir"

mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/scalable/apps" "$APPDIR/usr/share/metainfo" "$APPDIR/share/markleaf"

install -m755 "$REPO_DIR/apps/linux/target/release/markleaf" "$APPDIR/usr/bin/markleaf"
install -m644 "$REPO_DIR/apps/linux/markleaf/data/com.markleaf.MarkLeaf.desktop" "$APPDIR/usr/share/applications/"
install -m644 "$REPO_DIR/apps/linux/markleaf/data/com.markleaf.MarkLeaf.svg" "$APPDIR/usr/share/icons/hicolor/scalable/apps/"
install -m644 "$REPO_DIR/apps/linux/markleaf/data/com.markleaf.MarkLeaf.appdata.xml" "$APPDIR/usr/share/metainfo/"
cp -R "$REPO_DIR/apps/linux/markleaf/resources/editor" "$APPDIR/share/markleaf/editor"
install -m644 "$REPO_DIR/apps/linux/markleaf/data/com.markleaf.MarkLeaf.desktop" "$APPDIR/"

echo "[appimage] 运行 linuxdeploy 捆绑运行时(需在 Linux 容器内执行):"
echo "  linuxdeploy --appdir $APPDIR --output appimage --output-file MarkLeaf-$ARCH.AppImage"
echo "[appimage] 实际打包命令由 CI(release-linux.yml)或用户在 Linux 环境执行;"
echo "[appimage] macOS 开发机仅准备 AppDir 结构。"
