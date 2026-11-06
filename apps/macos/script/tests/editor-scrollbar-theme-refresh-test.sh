#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTAINER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWebContainerView.swift"

grep -Fq 'configureWebKitScrollView(' "$CONTAINER" || {
  echo "FAIL: WebView scroll view refresh must go through a shared helper" >&2
  exit 1
}
grep -Fq 'scrollView.appearance = appearance' "$CONTAINER" || {
  echo "FAIL: WebView scroll view must receive the themed appearance" >&2
  exit 1
}
grep -Fq 'scrollView.scrollerStyle = .overlay' "$CONTAINER" || {
  echo "FAIL: WebView scroll view must keep compact overlay scrollers" >&2
  exit 1
}
grep -Fq 'autoHide: Bool' "$CONTAINER" || {
  echo "FAIL: WebView scroll view must honor the auto-hide preference" >&2
  exit 1
}
grep -Fq 'applyThemeAppearance(' "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift" || {
  echo "FAIL: editor session must refresh the WebView scroll view" >&2
  exit 1
}
grep -Fq 'autoHide: SettingsService.shared.settings.autoHideScrollbars' "$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift" || {
  echo "FAIL: editor session must forward the auto-hide preference" >&2
  exit 1
}

echo PASS
