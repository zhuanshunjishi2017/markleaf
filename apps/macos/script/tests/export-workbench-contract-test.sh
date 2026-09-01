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

require "$CONTROLLER" 'NSSegmentedControl' 'export formats must use a native segmented control'
require "$CONTROLLER" 'PDFView()' 'PDF preview must use PDFKit'
require "$CONTROLLER" 'htmlPreviewView' 'HTML preview must keep WebKit'
require "$CONTROLLER" 'binding: ExportBinding' 'export windows must bind to a tab'
require "$CONTROLLER" 'ExportSessionLease' 'export windows must retain the source editor lease'
require "$CONTROLLER" 'keepTablesCheck' 'table pagination must have a control'
require "$CONTROLLER" 'keepHeadingsCheck' 'heading pagination must have a control'
require "$CONTROLLER" 'pageBehaviorRow?.isHidden = !isPDF' 'page behavior must be PDF-only'
require "$CONTROLLER" 'schedulePreview()' 'export settings must auto-refresh'
refuse "$CONTROLLER" '更新预览' 'manual preview refresh must not exist'

echo "PASS"
