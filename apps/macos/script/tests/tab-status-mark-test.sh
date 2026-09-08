#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
TAB="$ROOT_DIR/Sources/MarkLeaf/Views/TabBarController.swift"
SYNC="$ROOT_DIR/Sources/MarkLeaf/Services/TabStateSync.swift"

grep -Fq 'hasPendingExternalChange' "$SESSION"
grep -Fq 'hasExternalChange' "$TAB"
grep -Fq 'hasPendingExternalChange' "$SYNC"
grep -Fq 'L10n.t("只读")' "$TAB"
grep -Fq 'L10n.t("外部")' "$TAB"

echo PASS
