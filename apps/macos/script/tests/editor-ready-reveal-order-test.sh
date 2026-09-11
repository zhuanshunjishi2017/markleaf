#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
CONTAINER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWebContainerView.swift"

# 主题底色必须在揭示 WebView 之前同步生效，否则深色主题会在 ready 后闪白。
ready_line="$(grep -n 'case "ready":' "$SESSION" | head -1 | cut -d: -f1)"
waiting_line="$(awk -v ready="$ready_line" 'NR > ready && /isWaitingForStylesAcknowledgement = true/ { print NR; exit }' "$SESSION")"
background_line="$(awk -v ready="$ready_line" 'NR > ready && /applyScrollbarAppearance\(dark: currentThemeIsDark\)/ { print NR; exit }' "$SESSION")"

if [ -z "$waiting_line" ] || [ -z "$background_line" ] || [ "$waiting_line" -ge "$background_line" ]; then
  echo "FAIL: ready must wait for style acknowledgement before first paint" >&2
  exit 1
fi

handle_line="$(awk -v ready="$ready_line" 'NR > ready && /applyBlockHandleVisibility\(/ { print NR; exit }' "$SESSION")"

if [ -z "$handle_line" ] || [ "$handle_line" -lt "$background_line" ]; then
  echo "FAIL: block handle visibility must be prepared before the style acknowledgement" >&2
  exit 1
fi

highlight_line="$(awk -v ready="$ready_line" 'NR > ready && /setCodeHighlightVisible\(/ { print NR; exit }' "$SESSION")"

if [ -z "$highlight_line" ] || [ "$highlight_line" -lt "$background_line" ]; then
  echo "FAIL: code highlight visibility must be prepared before the style acknowledgement" >&2
  exit 1
fi

if grep -Fq 'revealEditorAfterThemeApplied' "$SESSION"; then
  echo "FAIL: reveal must be driven by stylesApplied, not an empty script" >&2
  exit 1
fi

if ! awk '/init\(session: EditorSession\)/, /loadEditor\(\)/' "$CONTAINER" | grep -Fq 'underPageBackgroundColor'; then
  echo "FAIL: webview must have a theme fallback background before first paint" >&2
  exit 1
fi

echo "PASS"
