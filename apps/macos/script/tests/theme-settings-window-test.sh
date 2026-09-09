#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD="$(mktemp -d /tmp/markleaf-theme-window.XXXXXX)"
trap 'rm -rf "$BUILD"' EXIT
[[ -f "$ROOT/Sources/MarkLeaf/Views/ThemeSettingsWindowController.swift" ]] || { echo 'FAIL: native unified theme window is missing'; exit 1; }
cp "$ROOT/script/tests/ThemeSettingsWindowTest.swift" "$BUILD/main.swift"
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH="$BUILD/cache" xcrun swiftc "$ROOT/Sources/MarkLeaf/Services/L10n.swift" "$ROOT/Sources/MarkLeaf/Services/ThemeIDNormalizer.swift" "$ROOT/Sources/MarkLeaf/Services/StyleManager.swift" "$ROOT/Sources/MarkLeaf/Services/ThemeSettingsModel.swift" "$ROOT/Sources/MarkLeaf/Views/ThemeSettingsWindowController.swift" "$BUILD/main.swift" -o "$BUILD/test"
"$BUILD/test"
