#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
PROTOCOL="$ROOT_DIR/../../packages/editor-web/src/protocol.ts"
MAIN="$ROOT_DIR/../../packages/editor-web/src/main.ts"

if ! grep -Fq "| 'stylesApplied'" "$PROTOCOL"; then
  echo "FAIL: the editor protocol must acknowledge applied styles" >&2
  exit 1
fi

styles_line="$(grep -n "case 'applyStyles':" "$MAIN" | head -1 | cut -d: -f1)"
ack_line="$(awk -v styles="$styles_line" 'NR > styles && /send\(.stylesApplied.\)/ { print NR; exit }' "$MAIN")"
sync_line="$(awk -v styles="$styles_line" 'NR > styles && /syncThemeModeClass\(\)/ { print NR; exit }' "$MAIN")"

if [ -z "$ack_line" ] || [ -z "$sync_line" ] || [ "$ack_line" -lt "$sync_line" ]; then
  echo "FAIL: style acknowledgment must follow synchronous theme class update" >&2
  exit 1
fi

ready_line="$(grep -n 'case "ready":' "$SESSION" | head -1 | cut -d: -f1)"
waiting_line="$(awk -v ready="$ready_line" 'NR > ready && /isWaitingForStylesAcknowledgement = true/ { print NR; exit }' "$SESSION")"
apply_line="$(awk -v ready="$ready_line" 'NR > ready && /applyStyles\(\)/ { print NR; exit }' "$SESSION")"
ack_handler_line="$(grep -n 'case "stylesApplied":' "$SESSION" | head -1 | cut -d: -f1)"
reveal_line="$(awk -v ack="$ack_handler_line" 'NR > ack && /revealEditorAfterScreenUpdate\(\)/ { print NR; exit }' "$SESSION")"

if [ -z "$waiting_line" ] || [ -z "$apply_line" ] || [ "$waiting_line" -ge "$apply_line" ]; then
  echo "FAIL: ready must wait for the style acknowledgement before revealing" >&2
  exit 1
fi

if [ -z "$ack_handler_line" ] || [ -z "$reveal_line" ]; then
  echo "FAIL: the editor must reveal only after stylesApplied" >&2
  exit 1
fi

restart_state_line="$(grep -n 'isRestartingEditor = true' "$SESSION" | head -1 | cut -d: -f1)"
loaded_line="$(grep -n 'case "documentLoaded":' "$SESSION" | head -1 | cut -d: -f1)"
loaded_reveal_line="$(awk -v loaded="$loaded_line" 'NR > loaded && /revealEditorAfterScreenUpdate\(\)/ { print NR; exit }' "$SESSION")"

if [ -z "$restart_state_line" ] || [ -z "$loaded_reveal_line" ]; then
  echo "FAIL: restart must keep the reload cover until documentLoaded" >&2
  exit 1
fi

ack_block="$(sed -n "${ack_handler_line},${loaded_line}p" "$SESSION")"
if printf '%s\n' "$ack_block" | grep -Fq 'isRestartingEditor'; then
  restart_reveal_line="$(awk -v ack="$ack_handler_line" -v loaded="$loaded_line" 'NR > ack && NR < loaded && /revealEditorAfterScreenUpdate\(\)/ { print NR; exit }' "$SESSION")"
  restart_guard_line="$(awk -v ack="$ack_handler_line" -v loaded="$loaded_line" 'NR > ack && NR < loaded && /if !isRestartingEditor \{/ { print NR; exit }' "$SESSION")"
  if [ -z "$restart_reveal_line" ] || [ -z "$restart_guard_line" ] || [ "$restart_guard_line" -ge "$restart_reveal_line" ]; then
    echo "FAIL: stylesApplied must not reveal while an editor restart is pending" >&2
    exit 1
  fi
fi

if ! grep -Fq 'revealEditorAfterScreenUpdate()' "$SESSION"; then
  echo "FAIL: stylesApplied must request a committed frame before reveal" >&2
  exit 1
fi

restart_line="$(grep -n 'func restartEditor()' "$SESSION" | head -1 | cut -d: -f1)"
prepare_line="$(awk -v restart="$restart_line" 'NR > restart && /prepareForReload\(\)/ { print NR; exit }' "$SESSION")"
reload_line="$(awk -v restart="$restart_line" 'NR > restart && /webView\?\.reload\(\)/ { print NR; exit }' "$SESSION")"

if [ -z "$prepare_line" ] || [ -z "$reload_line" ] || [ "$prepare_line" -ge "$reload_line" ]; then
  echo "FAIL: editor reload must hide the visible webview before navigation" >&2
  exit 1
fi

CONTAINER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWebContainerView.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
window_init_line="$(grep -n 'init(session: EditorSession)' "$WINDOW" | tail -1 | cut -d: -f1)"
build_line="$(awk -v init="$window_init_line" 'NR > init && /private func buildContent\(\)/ { print NR; exit }' "$WINDOW")"
window_theme_line="$(awk -v init="$window_init_line" -v build="$build_line" 'NR > init && NR < build && /window.backgroundColor = themeBackground/ { print NR; exit }' "$WINDOW")"

if [ -z "$window_theme_line" ]; then
  echo "FAIL: the native window must use the selected theme background before content appears" >&2
  exit 1
fi

if ! grep -Fq 'afterScreenUpdates = true' "$CONTAINER" \
   || ! grep -Fq 'takeSnapshot' "$CONTAINER"; then
  echo "FAIL: reveal must wait for WebKit to commit the themed frame" >&2
  exit 1
fi

snapshot_line="$(grep -n 'func revealEditorAfterScreenUpdate()' "$CONTAINER" | head -1 | cut -d: -f1)"
show_line="$(awk -v snapshot="$snapshot_line" 'NR > snapshot && /webView.isHidden = false/ { print NR; exit }' "$CONTAINER")"

if [ -z "$show_line" ]; then
  echo "FAIL: the themed frame must be captured while the webview is visible under its cover" >&2
  exit 1
fi

if ! grep -Fq 'ThemeFrameReadinessPolicy' "$CONTAINER"; then
  echo "FAIL: snapshot pixels must be validated before removing the themed cover" >&2
  exit 1
fi

system_line="$(grep -n 'private func applySystemAppearance' "$SESSION" | head -1 | cut -d: -f1)"
if ! grep -Fq 'window.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)' "$SESSION" \
   || ! grep -Fq 'window.backgroundColor = self.themeBackgroundColor ?? .windowBackgroundColor' "$SESSION"; then
  echo "FAIL: applying a theme must update the hosting window appearance and background" >&2
  exit 1
fi

if ! grep -Fq 'func prepareForReload()' "$CONTAINER"; then
  echo "FAIL: reload must prepare a themed host cover before hiding the webview" >&2
  exit 1
fi

init_line="$(grep -n 'init(session: EditorSession)' "$CONTAINER" | head -1 | cut -d: -f1)"
load_line="$(awk -v init="$init_line" 'NR > init && /private func loadEditor\(\)/ { print NR; exit }' "$CONTAINER")"
prepare_init_line="$(awk -v init="$init_line" -v load="$load_line" 'NR > init && NR < load && /prepareForReload\(\)/ { print NR; exit }' "$CONTAINER")"

if [ -z "$prepare_init_line" ]; then
  echo "FAIL: every new editor webview must start behind a themed cover" >&2
  exit 1
fi

snapshot_func_line="$(grep -n 'func revealEditorAfterScreenUpdate()' "$CONTAINER" | head -1 | cut -d: -f1)"
snapshot_call_line="$(awk -v snapshot="$snapshot_func_line" 'NR > snapshot && /takeSnapshot\(/ { print NR; exit }' "$CONTAINER")"
early_reveal_line="$(awk -v snapshot="$snapshot_func_line" -v call="$snapshot_call_line" 'NR > snapshot && NR < call && /revealEditor\(\)/ { print NR; exit }' "$CONTAINER")"

if [ -n "$early_reveal_line" ]; then
  echo "FAIL: WebView must stay behind the cover until the themed frame callback" >&2
  exit 1
fi

if ! grep -Fq 'reloadCoverView' "$CONTAINER" \
   || ! grep -Fq 'positioned: .above, relativeTo: webView' "$CONTAINER" \
   || ! grep -Fq 'reloadCoverView?.removeFromSuperview()' "$CONTAINER"; then
  echo "FAIL: reload must use an opaque cover above the webview until the themed frame commits" >&2
  exit 1
fi

if sed -n '/private func revealEditorAfterThemeApplied/,/private func applyPreferences/p' "$SESSION" \
    | grep -Fq 'evaluateJavaScript("1")'; then
  echo "FAIL: an empty script must not gate dark-mode first paint" >&2
  exit 1
fi

echo "PASS"
