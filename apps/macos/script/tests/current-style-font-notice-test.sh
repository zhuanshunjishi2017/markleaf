#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD="$(mktemp -d /tmp/markleaf-font-notice.XXXXXX)"
trap 'rm -rf "$BUILD"' EXIT
[[ -f "$ROOT/Sources/MarkLeaf/Services/CurrentStyleFontNotice.swift" ]] || { echo 'FAIL: current-style missing-font policy is missing'; exit 1; }
cp "$ROOT/script/tests/CurrentStyleFontNoticeTest.swift" "$BUILD/main.swift"
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH="$BUILD/cache" xcrun swiftc "$ROOT/Sources/MarkLeaf/Services/OptionalFontCatalog.swift" "$ROOT/Sources/MarkLeaf/Services/OptionalFontInstaller.swift" "$ROOT/Sources/MarkLeaf/Services/CurrentStyleFontNotice.swift" "$BUILD/main.swift" -o "$BUILD/test"
"$BUILD/test"
