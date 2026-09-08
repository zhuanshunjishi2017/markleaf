#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SETTINGS="$ROOT_DIR/Sources/MarkLeaf/Services/AppSettings.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
PREFS="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"
FRONTEND="$ROOT_DIR/packages/editor-web/src/main.ts"

for file in "$SESSION" "$PREFS" "$FRONTEND"; do
  grep -Fq 'ignoreMaxWidth' "$file" || { echo "FAIL: $file missing ignoreMaxWidth" >&2; exit 1; }
done
grep -Fq 'visualIgnoreMaxWidth' "$SETTINGS"
grep -Fq 'visualIgnoreMaxWidth' "$PREFS"
grep -Fq 'markleaf-ignore-max-width' "$FRONTEND"
echo PASS
