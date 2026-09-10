#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PREFS="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/MarkdownBehaviorSettingsWindowController.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$2" "$1" || fail "$1 missing: $2"; }
reject() { grep -Fq "$2" "$1" && fail "$1 unexpectedly contains: $2" || true; }

require "$WINDOW" 'final class MarkdownBehaviorSettingsWindowController'
require "$WINDOW" 'MarkdownBehaviorSettingsModel'
require "$WINDOW" 'L10n.t("Markdown 行为")'
require "$WINDOW" 'private let checkboxStack = NSStackView()'
require "$WINDOW" 'checkboxStack.alignment = .leading'
require "$WINDOW" 'checkboxStack.centerXAnchor.constraint(equalTo: checkboxArea.centerXAnchor)'
require "$WINDOW" 'form.centerXAnchor.constraint(equalTo: formArea.centerXAnchor)'
require "$WINDOW" 'max(fitting.width, 360)'
require "$WINDOW" 'window?.setContentSize(contentSize)'
reject "$WINDOW" 'NSTextField(labelWithString: "")'
require "$PREFS" 'L10n.t("Markdown 行为…")'
require "$PREFS" '#selector(openMarkdownBehaviorSettings)'
for control in escapeLiteralSymbolsCheck escapeMarkdownLiteralSymbolsCheck exitBlockOnEmptyEnterCheck useShiftEnterHardBreakCheck markdownCodeFencePopup markdownEmphasisMarkerPopup markdownBulletMarkerPopup; do
  reject "$PREFS" "private let $control"
  reject "$PREFS" "settings.$control"
done
echo PASS
