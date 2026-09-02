#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PREFS_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/PreferencesWindowController.swift"
FONT_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/FontSettingsWindowController.swift"
EXPORT_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/ExportWindowController.swift"
SESSION_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
FOOTNOTE_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession+Footnote.swift"
FORMATTER_FILE="$ROOT_DIR/Sources/MarkLeaf/Support/BoundedIntegerFormatter.swift"

require() {
  grep -Fq "$2" "$1" || { echo "FAIL: $3" >&2; exit 1; }
}
refuse() {
  if grep -Fq "$2" "$1"; then echo "FAIL: $3" >&2; exit 1; fi
}

# 数值字段统一使用「输入即拒绝」格式化器 + 确认按钮实时禁用（与导出自定义边距一致）。
require "$FORMATTER_FILE" 'BoundedIntegerFormatter' 'bounded integer formatter must exist'
require "$PREFS_FILE" 'BoundedTextFieldMonitor(' 'preferences numeric fields must use the text monitor (no reformatting)'
require "$PREFS_FILE" 'field: lineHeightField, fractionDigits: 2' 'line height must keep two fraction digits and never round'
require "$PREFS_FILE" 'upperBound: Double(AppSettings.snapshotIntervalRange.upperBound)' 'preferences ranges must reuse AppSettings'
refuse "$PREFS_FILE" '.formatter =' 'preferences must not reformat numeric fields via NumberFormatter'
require "$PREFS_FILE" 'refreshApplyButton()' 'preferences must refresh the apply button while typing'
require "$PREFS_FILE" 'func controlTextDidChange' 'preferences must validate on every keystroke'
require "$PREFS_FILE" 'invalidNumericFieldLabel() == nil' 'apply must stay disabled while any numeric field is invalid'
require "$FONT_FILE" 'BoundedTextFieldMonitor(' 'font size must use the text monitor (no reformatting)'
refuse "$FONT_FILE" '.formatter =' 'font settings must not reformat the size field'
require "$FONT_FILE" 'refreshOKButton()' 'font settings must refresh OK while typing'
require "$FONT_FILE" 'AppSettings.sourceFontSizeRange' 'font size must reuse the AppSettings range'
require "$EXPORT_FILE" 'BoundedTextFieldMonitor(field: $0, fractionDigits: 1, upperBound: 100' 'custom margin fields must keep the text monitor'
refuse "$EXPORT_FILE" '.formatter =' 'custom margin fields must not reformat via NumberFormatter'
require "$EXPORT_FILE" 'okButton?.isEnabled = currentMarginFields.allSatisfy' 'custom margin OK must stay disabled while invalid'
require "$SESSION_FILE" 'isValidMathNumberTag' 'math number tags must be validated'
require "$SESSION_FILE" 'object: numberField,' 'math number field must participate in live validation'
require "$FOOTNOTE_FILE" 'bindAlertInputValidation(field: field, button: okButton)' 'footnote reset must disable OK while the label is empty'

# 遗留的死代码与旧实现不得回流。
refuse "$EXPORT_FILE" 'MarginSettingsDialog' 'the legacy standalone margin dialog must not return'
refuse "$EXPORT_FILE" 'marginOKButton' 'margin OK must stay local to the sheet'
refuse "$EXPORT_FILE" 'marginTextFieldOriginals' 'unused margin revert state must not return'
refuse "$PREFS_FILE" 'formatter = NumberFormatter' 'preferences must not use raw NumberFormatter'

echo PASS
