#!/usr/bin/env bash

# Build the local MarkLeaf distribution bundle without stopping a running app.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MACOS_DIR="$(cd "$HERE/../.." && pwd)"
REPO_DIR="$(cd "$MACOS_DIR/.." && pwd)"
APP_NAME="MarkLeaf"
APP_VERSION="${MARKLEAF_VERSION:-1.1.7}"
DMG_VOLUME_NAME="${MARKLEAF_DMG_VOLUME_NAME:-$APP_NAME $APP_VERSION Installer $(date +%s)}"
ARCH="arm64"
OUTPUT_DIR="${1:-$MACOS_DIR/dist/release}"
BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-release.XXXXXX")"
APP_STAGE="$BUILD_ROOT/$APP_NAME.app"
DMG_STAGE_DIR="$(mktemp -d /private/tmp/markleaf-dmg-stage.XXXXXX)"
DMG_MOUNT_DIR="$(mktemp -d /private/tmp/markleaf-dmg-mount.XXXXXX)"
DMG_RW="$BUILD_ROOT/$APP_NAME-$APP_VERSION-macos-$ARCH-rw.dmg"
DMG_ATTACHED=0

APP_ZIP="$OUTPUT_DIR/$APP_NAME-$APP_VERSION-macos-$ARCH.zip"
APP_DMG="$OUTPUT_DIR/$APP_NAME-$APP_VERSION-macos-$ARCH.dmg"
DSYM_ZIP="$OUTPUT_DIR/$APP_NAME-$APP_VERSION-macos-$ARCH.dSYM.zip"
CHECKSUMS="$OUTPUT_DIR/SHA256SUMS.txt"

cleanup() {
    if [ "$DMG_ATTACHED" = 1 ]; then
        hdiutil detach "$DMG_MOUNT_DIR" >/dev/null 2>&1 || true
    fi
    rmdir "$DMG_MOUNT_DIR" >/dev/null 2>&1 || rm -rf "$DMG_MOUNT_DIR"
    rm -rf "$DMG_STAGE_DIR"
    rm -rf "$BUILD_ROOT"
}
trap cleanup EXIT

mkdir -p "$OUTPUT_DIR"
rm -f "$APP_ZIP" "$APP_DMG" "$DSYM_ZIP" "$CHECKSUMS"

echo "[package] preparing EditorWeb and runtime resources"
"$MACOS_DIR/script/prepare_resources.sh"

echo "[package] building $APP_NAME $APP_VERSION ($ARCH)"
swift build --package-path "$MACOS_DIR" -c release -Xswiftc -g
BUILD_BIN="$(swift build --package-path "$MACOS_DIR" -c release --show-bin-path)/$APP_NAME"

echo '[package] assembling application bundle'
mkdir -p "$APP_STAGE/Contents/MacOS" "$APP_STAGE/Contents/Resources"
cp "$BUILD_BIN" "$APP_STAGE/Contents/MacOS/$APP_NAME"
chmod +x "$APP_STAGE/Contents/MacOS/$APP_NAME"
cat > "$APP_STAGE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>$APP_NAME</string>
  <key>CFBundleIdentifier</key><string>com.markleaf.app</string>
  <key>CFBundleName</key><string>$APP_NAME</string>
  <key>CFBundleDisplayName</key><string>MarkLeaf</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$APP_VERSION</string>
  <key>CFBundleVersion</key><string>$APP_VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Markdown Document</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>CFBundleTypeIconFile</key><string>FileIcon</string>
      <key>LSItemContentTypes</key>
      <array><string>net.daringfireball.markdown</string><string>public.plain-text</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST

ditto "$MACOS_DIR/Resources/EditorWeb" "$APP_STAGE/Contents/Resources/EditorWeb"
ditto "$MACOS_DIR/Resources/Styles" "$APP_STAGE/Contents/Resources/Styles"
if [ -d "$MACOS_DIR/Changelog" ]; then
    ditto "$MACOS_DIR/Changelog" "$APP_STAGE/Contents/Resources/Changelog"
fi
for icon in AppIcon.icns FileIcon.icns; do
    if [ -f "$MACOS_DIR/Resources/$icon" ]; then
        cp "$MACOS_DIR/Resources/$icon" "$APP_STAGE/Contents/Resources/$icon"
    fi
done

xattr -cr "$APP_STAGE"
codesign --force --deep --sign - "$APP_STAGE" >/dev/null
codesign --verify --deep --strict "$APP_STAGE"

echo '[package] generating dSYM and ZIP'
dsymutil "$BUILD_BIN" -o "$BUILD_ROOT/$APP_NAME.app.dSYM"
ditto -c -k --sequesterRsrc --keepParent "$APP_STAGE" "$APP_ZIP"
ditto -c -k --keepParent "$BUILD_ROOT/$APP_NAME.app.dSYM" "$DSYM_ZIP"

echo '[package] creating branded DMG'
ditto "$APP_STAGE" "$DMG_STAGE_DIR/$APP_NAME.app"
ln -s /Applications "$DMG_STAGE_DIR/Applications"
bash "$HERE/markleaf-dmg-layout.sh" prepare "$DMG_STAGE_DIR"
xattr -cr "$DMG_STAGE_DIR/.background"
hdiutil create -volname "$DMG_VOLUME_NAME" -srcfolder "$DMG_STAGE_DIR" -ov -format UDRW "$DMG_RW" >/dev/null
hdiutil attach -readwrite -noverify -noautoopen -mountpoint "$DMG_MOUNT_DIR" "$DMG_RW" >/dev/null
DMG_ATTACHED=1
sleep 5
bash "$HERE/markleaf-dmg-layout.sh" apply "$DMG_MOUNT_DIR"
hdiutil detach "$DMG_MOUNT_DIR" >/dev/null
DMG_ATTACHED=0
hdiutil convert "$DMG_RW" -format UDZO -ov -o "$APP_DMG" >/dev/null
hdiutil verify "$APP_DMG" >/dev/null

echo '[package] writing SHA-256 checksums'
(
    cd "$OUTPUT_DIR"
    shasum -a 256 "$(basename "$APP_DMG")" "$(basename "$APP_ZIP")" "$(basename "$DSYM_ZIP")" > "$(basename "$CHECKSUMS")"
)

echo "[package] completed: $OUTPUT_DIR"
ls -lh "$APP_DMG" "$APP_ZIP" "$DSYM_ZIP" "$CHECKSUMS"
