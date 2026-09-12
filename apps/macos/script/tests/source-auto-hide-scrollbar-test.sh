#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CSS="$ROOT_DIR/../../packages/editor-core/src/styles.css"

for selector in \
  'html.markleaf-auto-hide-scrollbar,' \
  'body.markleaf-auto-hide-scrollbar,' \
  'html.markleaf-auto-hide-scrollbar .cm-scroller {'; do
  if ! grep -Fq "$selector" "$CSS"; then
    echo "FAIL: auto-hide scrollbar styling is missing selector: $selector" >&2
    exit 1
  fi
done

if ! grep -Fq 'scrollbar-color: color-mix(' "$CSS"; then
  echo "FAIL: source .cm-scroller needs standard scrollbar-color to override the macOS opaque fallback" >&2
  exit 1
fi

if ! grep -Fq 'calc(var(--ml-scrollbar-alpha, 0) * 100%)' "$CSS"; then
  echo "FAIL: standard scrollbar-color must follow the JS scrollbar alpha" >&2
  exit 1
fi

if ! grep -A 5 -F 'html.markleaf-auto-hide-scrollbar .cm-scroller {' "$CSS" | grep -Fq 'scrollbar-width: none;'; then
  echo "FAIL: WebKit native source scrollbars must be hidden before using the custom overlay" >&2
  exit 1
fi

if ! grep -Fq '.markleaf-source-scrollbar {' "$CSS" \
   || ! grep -Fq 'width: 8px;' "$CSS" \
   || ! grep -Fq 'right: 0;' "$CSS" \
   || ! grep -Fq 'border-radius: 4px;' "$CSS"; then
  echo "FAIL: source overlay must match the visual mode scrollbar geometry" >&2
  exit 1
fi

echo "PASS"
