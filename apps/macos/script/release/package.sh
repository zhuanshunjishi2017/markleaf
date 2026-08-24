#!/usr/bin/env bash

# Build the local MarkLeaf distribution bundle without stopping a running app.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MACOS_DIR="$(cd "$HERE/../.." && pwd)"
REPO_DIR="$(cd "$MACOS_DIR/.." && pwd)"
APP_NAME="MarkLeaf"
APP_VERSION="${MARKLEAF_VERSION:-1.2.6}"
DMG_VOLUME_NAME="${MARKLEAF_DMG_VOLUME_NAME:-$APP_NAME $APP_VERSION}"
ARCH="arm64"
OUTPUT_DIR="${1:-$MACOS_DIR/dist/release}"
BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-release.XXXXXX")"
APP_STAGE="$BUILD_ROOT/$APP_NAME.app"

APP_ZIP="$OUTPUT_DIR/$APP_NAME-$APP_VERSION-macos-$ARCH.zip"
APP_DMG="$OUTPUT_DIR/$APP_NAME-$APP_VERSION-macos-$ARCH.dmg"
DSYM_ZIP="$OUTPUT_DIR/$APP_NAME-$APP_VERSION-macos-$ARCH.dSYM.zip"
CHECKSUMS="$OUTPUT_DIR/SHA256SUMS.txt"

cleanup() {
    rm -rf "$BUILD_ROOT"
}
trap cleanup EXIT

SWIFT_BUILD_FLAGS=()
if [[ "${MARKLEAF_DISABLE_SWIFT_SANDBOX:-0}" == "1" ]]; then
    SWIFT_BUILD_FLAGS+=(--disable-sandbox)
fi

mkdir -p "$OUTPUT_DIR"
rm -f "$APP_ZIP" "$APP_DMG" "$DSYM_ZIP" "$CHECKSUMS"

echo "[package] preparing EditorWeb and runtime resources"
"$MACOS_DIR/script/prepare_resources.sh"

echo "[package] building $APP_NAME $APP_VERSION ($ARCH)"
swift build "${SWIFT_BUILD_FLAGS[@]}" --package-path "$MACOS_DIR" -c release -Xswiftc -g
BUILD_BIN="$(swift build "${SWIFT_BUILD_FLAGS[@]}" --package-path "$MACOS_DIR" -c release --show-bin-path)/$APP_NAME"

echo '[package] assembling application bundle'
mkdir -p "$APP_STAGE/Contents/MacOS" "$APP_STAGE/Contents/Resources"
cp "$BUILD_BIN" "$APP_STAGE/Contents/MacOS/$APP_NAME"
chmod +x "$APP_STAGE/Contents/MacOS/$APP_NAME"
"$HERE/write-info-plist.sh" "$APP_STAGE/Contents/Info.plist" "$APP_VERSION"

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
bash "$HERE/create-branded-dmg.sh" "$APP_STAGE" "$APP_DMG" "$DMG_VOLUME_NAME"

echo '[package] writing SHA-256 checksums'
(
    cd "$OUTPUT_DIR"
    shasum -a 256 "$(basename "$APP_DMG")" "$(basename "$APP_ZIP")" "$(basename "$DSYM_ZIP")" > "$(basename "$CHECKSUMS")"
)

echo "[package] completed: $OUTPUT_DIR"
ls -lh "$APP_DMG" "$APP_ZIP" "$DSYM_ZIP" "$CHECKSUMS"
