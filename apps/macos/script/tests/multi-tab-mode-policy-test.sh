#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-multi-tab-policy-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT

SETTINGS="$ROOT_DIR/Sources/MarkLeaf/Services/AppSettings.swift"
PREFS="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"

require() {
  grep -Fq "$2" "$1" || { echo "FAIL: $3" >&2; exit 1; }
}
require_text() {
  grep -Fq "$2" <<<"$1" || { echo "FAIL: $3" >&2; exit 1; }
}

method_body() {
  awk -v name="$2" '
    $0 ~ "private func " name "\\(\\)" { found=1; next }
    found && $0 == "    }" { exit }
    found { print }
  ' "$1"
}

require "$SETTINGS" 'var multiTabEnabled = true' 'multi-tab must default to enabled'
require "$SETTINGS" 'forKey: .multiTabEnabled' 'legacy settings must decode multi-tab with a safe default'
require "$PREFS" 'private let multiTabCheck' 'preferences must expose the multi-tab switch'
require "$PREFS" 'settings.multiTabEnabled = multiTabCheck.state == .on' 'preferences must persist the multi-tab switch'
require "$PREFS" 'editorCentersMultiTabCheckbox(for: displayLanguage)' 'the multi-tab checkbox must use independent centering'
require "$PREFS" 'editorCentersBlockHandleCheckbox(for: displayLanguage)' 'the block-handle checkbox must use independent centering'
require "$PREFS" 'if centersMultiTab { editorCenteredCheckboxes.insert(multiTabCheck) }' 'the multi-tab checkbox must be registered for independent centering'
require "$PREFS" 'if centersBlockHandle { editorCenteredCheckboxes.insert(blockHandleCheck) }' 'the block-handle checkbox must be registered for independent centering'
PREFS_FILE_BODY=$(method_body "$PREFS" filePage)
PREFS_EDITOR_BODY=$(method_body "$PREFS" editorPage)
require_text "$PREFS_EDITOR_BODY" '.header(L10n.t("文档与标签"))' 'the editor page must contain a documents-and-tabs group'
require_text "$PREFS_EDITOR_BODY" '.field("", multiTabCheck)' 'the multi-tab switch must belong to the editor page group'
if grep -Fq '.field("", multiTabCheck)' <<<"$PREFS_FILE_BODY"; then
  echo 'FAIL: the multi-tab switch must not remain on the file page' >&2
  exit 1
fi
require "$WINDOW" 'func applyMultiTabMode(animated: Bool)' 'editor windows must apply the multi-tab mode'
require "$WINDOW" 'applyWindowTitle()' 'single-document windows must update the filename title'
require "$WINDOW" 'L10n.t("最简模式已开启")' 'minimal mode status must use the renamed state'
require "$WINDOW" 'L10n.t("最简模式已关闭")' 'minimal mode exit status must use the renamed state'

cp "$ROOT_DIR/script/tests/MultiTabModePolicyTest.swift" "$BUILD_DIR/main.swift"
swiftc -module-cache-path "$BUILD_DIR/module-cache" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/ExternalFileOpenMode.swift" \
  "$ROOT_DIR/Sources/MarkLeaf/Services/MultiTabModePolicy.swift" \
  "$BUILD_DIR/main.swift" \
  -o "$BUILD_DIR/multi-tab-mode-policy-test"
"$BUILD_DIR/multi-tab-mode-policy-test"
