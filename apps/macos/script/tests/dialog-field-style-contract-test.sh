#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MATH_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
FOOTNOTE_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+Footnote.swift"
STYLE_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/DialogTextFieldStyle.swift"

# 公式编号沿用 Windows 的单输入框对话框（MainForm.Dialogs.SetMathNumber → TextInputDialog），
# 不再有 latexField / NSGridView 双字段布局；字段样式统一交给 DialogTextFieldStyle。
grep -q 'numberField.bezelStyle = .roundedBezel' "$MATH_FILE"
grep -q 'DialogTextFieldStyle.apply(to: numberField)' "$MATH_FILE"
grep -q 'func setMathNumber()' "$MATH_FILE"
grep -q 'latexField' "$MATH_FILE" && { echo "FAIL: math number dialog must stay single-field" >&2; exit 1; }

grep -q 'labelField.bezelStyle = .roundedBezel' "$FOOTNOTE_FILE"
grep -q 'DialogTextFieldStyle.apply(to: labelField)' "$FOOTNOTE_FILE"
grep -q 'scroll.borderType = .noBorder' "$FOOTNOTE_FILE"
grep -q 'scroll.layer?.cornerRadius = 6' "$FOOTNOTE_FILE"

grep -q 'field.bezelStyle = .squareBezel' "$STYLE_FILE"
grep -q 'field.frame.size.height = field.fittingSize.height' "$STYLE_FILE"

echo PASS
