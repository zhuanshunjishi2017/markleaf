#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODEL="$ROOT_DIR/Sources/MarkLeaf/Services/MarkdownBehaviorSettings.swift"
if [[ ! -f "$MODEL" ]]; then
  echo "FAIL: missing MarkdownBehaviorSettings.swift" >&2
  exit 1
fi
DEVELOPER_DIR="${DEVELOPER_DIR:-/Applications/Xcode-beta.app/Contents/Developer}" \
CLANG_MODULE_CACHE_PATH="${TMPDIR:-/tmp}/markleaf-markdown-model-clang" \
SWIFT_MODULECACHE_PATH="${TMPDIR:-/tmp}/markleaf-markdown-model-swift" \
swift build --disable-sandbox --package-path "$ROOT_DIR"
