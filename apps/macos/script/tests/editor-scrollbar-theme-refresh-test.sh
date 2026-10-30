#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTAINER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWebContainerView.swift"

grep -Fq 'scrollView?.appearance = themedAppearance' "$CONTAINER" || {
  echo "FAIL: WebView scroll view must receive the themed appearance" >&2
  exit 1
}
grep -Fq 'scrollView?.verticalScroller?.appearance = themedAppearance' "$CONTAINER" || {
  echo "FAIL: WebView vertical scroller must receive the themed appearance" >&2
  exit 1
}
grep -Fq 'scrollView?.verticalScroller?.needsDisplay = true' "$CONTAINER" || {
  echo "FAIL: WebView vertical scroller must be invalidated after theme changes" >&2
  exit 1
}

echo PASS
