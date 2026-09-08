#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PREFS="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
SETTINGS_TEST="$ROOT_DIR/script/tests/AppSettingsCodeHighlightTest.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

require_text "$SETTINGS_TEST" 'markdownCodeFence == "tilde"'
WINDOW="$ROOT_DIR/Sources/MarkLeaf/Views/MarkdownBehaviorSettingsWindowController.swift"
MODEL="$ROOT_DIR/Sources/MarkLeaf/Services/MarkdownBehaviorSettings.swift"
require_text "$WINDOW" 'final class MarkdownBehaviorSettingsWindowController'
require_text "$MODEL" 'settings.escapeMarkdownLiteralSymbols = escapeMarkdownLiteralSymbols'
require_text "$MODEL" 'settings.exitBlockOnEmptyEnter = exitBlockOnEmptyEnter'
require_text "$MODEL" 'settings.markdownBulletMarker = markdownBulletMarker'
require_text "$PREFS" 'L10n.t("Markdown 行为")'
require_text "$SESSION" 'private func applyMarkdownEditingSettings()'
require_text "$SESSION" 'setMarkdownEditingSettings'

echo PASS
