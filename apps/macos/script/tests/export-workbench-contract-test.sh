#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/ExportWindowController.swift"

require() {
  grep -Fq "$2" "$1" || { echo "FAIL: $3" >&2; exit 1; }
}
refuse() {
  if grep -Fq "$2" "$1"; then echo "FAIL: $3" >&2; exit 1; fi
}

require "$CONTROLLER" 'ExportFormatSelector.make()' 'export formats must use a native in-content segmented selector'
require "$CONTROLLER" 'formatHost,' 'the compact format selector must be the first options row'
require "$CONTROLLER" 'formatSelector.centerXAnchor.constraint(equalTo: formatHost.centerXAnchor)' 'the compact format selector must be centered in the left column'
require "$CONTROLLER" 'formatChanged(_ sender: NSSegmentedControl)' 'format changes must follow the native selector'
require "$CONTROLLER" 'labeled(L10n.t("纸张设置"), paperPopup)' 'paper settings must live in the shared label column'
require "$CONTROLLER" 'labeled(L10n.t("方向"), directionPopup)' 'orientation must live in the shared label column'
require "$CONTROLLER" 'directionPopup' 'orientation must be a popup, not a checkbox'
require "$CONTROLLER" 'directionPopup.addItems(withTitles: [L10n.t("纵向"), L10n.t("横向")])' 'orientation popup must expose portrait and landscape'
require "$CONTROLLER" 'L10n.t("方向")' 'orientation needs a visible label'
require "$CONTROLLER" 'marginSummaryLabel' 'margins must show a value preview'
require "$CONTROLLER" 'updateMarginSummary()' 'margin preview must refresh after preset or custom changes'
require "$CONTROLLER" 'marginRowStack.alignment = .top' 'margin label must align with its first control'
require "$CONTROLLER" 'NSGridView(views: [[topCell, bottomCell], [leftCell, rightCell]])' 'custom margin dialog must use two rows and two columns'
require "$CONTROLLER" 'BoundedTextFieldMonitor(field: $0, fractionDigits: 1, upperBound: 100' 'custom margin fields must reject invalid characters and out-of-range values without reformatting'
require "$CONTROLLER" 'alert.beginSheetModal(for: window)' 'custom margin settings must present as a sheet'
require "$CONTROLLER" 'previewContainer.bottomAnchor.constraint(equalTo: root.bottomAnchor)' 'the right preview must reach the bottom edge'
require "$CONTROLLER" 'pageCountLabel.trailingAnchor.constraint(equalTo: previewContainer.trailingAnchor, constant: -12)' 'the page count label must not touch the window edge'
require "$CONTROLLER" 'let leftStack = NSStackView(views: [optionsStack, buttonRow])' 'bottom action buttons must live in the left column'
refuse "$CONTROLLER" 'ExportFormatToolbar.make()' 'format selection must not use a window toolbar tab controller'
refuse "$CONTROLLER" 'formatToolbarHost' 'format selection must not use a window toolbar host'
refuse "$CONTROLLER" 'selectedTabViewItemIndex' 'format selection must not use a window toolbar tab controller'
refuse "$CONTROLLER" 'landscapeCheck.state == .on' 'orientation must not be a checkbox'
refuse "$CONTROLLER" 'directionLabel' 'orientation label must not be nested inside the control column'
require "$CONTROLLER" 'updateHeaderFooterFieldState(animated: false)' 'format switching must not animate header or footer rows'
require "$CONTROLLER" 'let headerVisible = isPDF && selectedHeaderFooterPreset' 'header fields must be PDF-only'
require "$CONTROLLER" 'PDFView()' 'PDF preview must use PDFKit'
require "$CONTROLLER" 'htmlPreviewView' 'HTML preview must keep WebKit'
require "$CONTROLLER" 'binding: ExportBinding' 'export windows must bind to a tab'
require "$CONTROLLER" 'ExportSessionLease' 'export windows must retain the source editor lease'
require "$CONTROLLER" 'keepTablesCheck' 'table pagination must have a control'
require "$CONTROLLER" 'keepHeadingsCheck' 'heading pagination must have a control'
require "$CONTROLLER" 'L10n.t("尽量保持表格不跨页")' 'table pagination copy must be advisory'
require "$CONTROLLER" 'L10n.t("尽量避免标题孤悬页尾")' 'heading pagination copy must be advisory'
require "$CONTROLLER" 'pageBehaviorRowStack.alignment = .top' 'page behavior label must align with its first checkbox'
refuse "$CONTROLLER" '不允许表格分居两页' 'old table pagination copy must not remain'
refuse "$CONTROLLER" '不允许标题处于页面最底部' 'old heading pagination copy must not remain'
require "$CONTROLLER" 'pageBehaviorRow?.isHidden = !isPDF' 'page behavior must be PDF-only'
require "$CONTROLLER" 'schedulePreview()' 'export settings must auto-refresh'
refuse "$CONTROLLER" '更新预览' 'manual preview refresh must not exist'

echo "PASS"
