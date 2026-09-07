#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/EditorMenuPolicy.swift"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
CONTEXT="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

require_text "$POLICY" '"duplicateParagraph", "deleteParagraph"'
require_text "$MENU" 'L10n.t("重复该段"), "duplicateParagraph"'
require_text "$MENU" 'L10n.t("删除该段"), "deleteParagraph"'
require_text "$MENU" 'case "duplicateParagraph": execute("duplicateParagraph")'
require_text "$CONTEXT" 'L10n.t("重复该段"), "duplicateParagraph"'
require_text "$CONTEXT" 'L10n.t("删除该段"), "deleteParagraph"'
echo PASS
