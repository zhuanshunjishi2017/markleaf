#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD="$(mktemp -d /tmp/markleaf-theme-model.XXXXXX)"
trap 'rm -rf "$BUILD"' EXIT
[[ -f "$ROOT/Sources/MarkLeaf/Services/ThemeSettingsModel.swift" ]] || { echo 'FAIL: unified theme selection model is missing'; exit 1; }
cp "$ROOT/script/tests/ThemeSettingsModelTest.swift" "$BUILD/main.swift"
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH="$BUILD/cache" xcrun swiftc "$ROOT/Sources/MarkLeaf/Services/ThemeIDNormalizer.swift" "$ROOT/Sources/MarkLeaf/Services/StyleManager.swift" "$ROOT/Sources/MarkLeaf/Services/ThemeSettingsModel.swift" "$BUILD/main.swift" -o "$BUILD/test"
"$BUILD/test"
