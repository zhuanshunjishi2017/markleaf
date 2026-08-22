#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MATH_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
FOOTNOTE_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+Footnote.swift"

grep -q 'latexField.bezelStyle = .roundedBezel' "$MATH_FILE"
grep -q 'numberField.bezelStyle = .roundedBezel' "$MATH_FILE"
grep -q 'let numberGrid = NSGridView' "$MATH_FILE"
grep -q 'labelField.bezelStyle = .roundedBezel' "$FOOTNOTE_FILE"
grep -q 'scroll.borderType = .noBorder' "$FOOTNOTE_FILE"
grep -q 'scroll.layer?.cornerRadius = 6' "$FOOTNOTE_FILE"

echo PASS
