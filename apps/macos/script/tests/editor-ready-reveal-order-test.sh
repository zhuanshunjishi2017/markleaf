#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
CONTAINER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWebContainerView.swift"

# 主题底色必须在揭示 WebView 之前同步生效，否则深色主题会在 ready 后闪白。
ready_line="$(grep -n 'case "ready":' "$SESSION" | head -1 | cut -d: -f1)"
reveal_line="$(awk -v ready="$ready_line" 'NR > ready && /revealEditorAfterThemeApplied\(\)/ { print NR; exit }' "$SESSION")"
background_line="$(awk -v ready="$ready_line" 'NR > ready && /applyScrollbarAppearance\(dark: currentThemeIsDark\)/ { print NR; exit }' "$SESSION")"

if [ -z "$reveal_line" ] || [ -z "$background_line" ] || [ "$background_line" -ge "$reveal_line" ]; then
  echo "FAIL: theme background must be applied synchronously before revealEditor" >&2
  exit 1
fi

if ! grep -Fq 'private func revealEditorAfterThemeApplied()' "$SESSION"; then
  echo "FAIL: reveal must be deferred until theme CSS lands (helper missing)" >&2
  exit 1
fi

if ! sed -n '/private func revealEditorAfterThemeApplied/,/func applyPreferences/p' "$SESSION" \
    | grep -Fq 'evaluateJavaScript'; then
  echo "FAIL: deferred reveal must wait for the pending style payload to evaluate" >&2
  exit 1
fi

if ! awk '/init\(session: EditorSession\)/, /loadEditor\(\)/' "$CONTAINER" | grep -Fq 'underPageBackgroundColor'; then
  echo "FAIL: webview must have a theme fallback background before first paint" >&2
  exit 1
fi

echo "PASS"
