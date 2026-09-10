#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD="$(mktemp -d /tmp/markleaf-code-language.XXXXXX)"
trap 'rm -rf "$BUILD"' EXIT
[[ -f "$ROOT/Sources/MarkLeaf/Services/CodeBlockLanguageCatalog.swift" ]] || {
  echo 'FAIL: code block language catalog is missing'
  exit 1
}
cp "$ROOT/script/tests/CodeBlockLanguageCatalogTest.swift" "$BUILD/main.swift"
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
  CLANG_MODULE_CACHE_PATH="$BUILD/cache" \
  xcrun swiftc \
  "$ROOT/Sources/MarkLeaf/Services/CodeBlockLanguageCatalog.swift" \
  "$BUILD/main.swift" \
  -o "$BUILD/test"
"$BUILD/test"
