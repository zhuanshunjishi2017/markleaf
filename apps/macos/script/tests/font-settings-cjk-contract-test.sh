#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FONT_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/FontSettingsWindowController.swift"
PREFS_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"

grep -q 'private(set) var cjkLanguageTag: CJKLanguageTag' "$FONT_FILE"
grep -q 'L10n.t("汉字优先字型")' "$FONT_FILE"
if grep -q '\.field(L10n.t("汉字优先字型"), cjkLanguagePopup)' "$PREFS_FILE"; then
  echo 'FAIL: CJK glyph preference should not remain on the main Preferences page' >&2
  exit 1
fi

echo PASS
