#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TAB="$ROOT_DIR/Sources/MarkLeaf/Views/TabBarController.swift"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/TabDetachPolicy.swift"
STORE="$ROOT_DIR/Sources/MarkLeaf/Services/TabStore.swift"
PREVIEW="$ROOT_DIR/Sources/MarkLeaf/Views/TabDragPreviewWindow.swift"

require_text() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing: $2" >&2; exit 1; }
}

# 撕下判定以标签栏条带为基准：上下左右拖离条带都能拆窗。
require_text "$TAB" 'var onTearOff: ((DocumentTabID, NSPoint) -> Void)?'
require_text "$TAB" 'stripGlobalRect: dragStripGlobalRect'
require_text "$POLICY" 'static func stripRegion(stripGlobalRect: NSRect'
require_text "$POLICY" 'globalPoint.y < stripGlobalRect.minY - tolerance'

# 拖到别的窗口标签栏上释放：并入那个窗口，而不是新建窗口。
require_text "$TAB" 'var onTransfer: ((DocumentTabID, EditorWindowController, Int) -> Void)?'
require_text "$TAB" 'var stripHitProvider: (NSPoint, TabBarController) -> TabStripHit? = { point, excluded in'
require_text "$TAB" 'AppWindowManager.shared.tabStripHit(globalPoint: point, excluding: excluded)'
require_text "$TAB" 'TabDetachPolicy.outcome('
require_text "$TAB" 'func showDropIndicator(at index: Int)'
require_text "$POLICY" 'static func tearOffOrigin(globalPoint: NSPoint, windowPoint: NSPoint) -> NSPoint'
require_text "$MANAGER" 'func tabStripHit(globalPoint: NSPoint, excluding excluded: TabBarController? = nil) -> TabStripHit?'
require_text "$STORE" 'func insert(_ tab: DocumentTab, at index: Int, activate: Bool = true) -> DocumentTab'
require_text "$CONTROLLER" 'func attachTransferredDocument(_ document: DetachedTabDocument, at index: Int) -> DocumentTabID?'
require_text "$CONTROLLER" 'func transferTab(_ id: DocumentTabID, to target: EditorWindowController, at index: Int)'

# 撕下后的新窗口贴着光标出现，并沿用来源窗口的光标相对位置。
require_text "$CONTROLLER" 'func tearOffTab(_ id: DocumentTabID, windowOrigin: NSPoint)'
require_text "$MANAGER" 'at windowOrigin: NSPoint? = nil'
require_text "$CONTROLLER" 'func placeWindow(origin: NSPoint)'

# 光标离开来源窗口后由独立浮层跟随鼠标。
require_text "$TAB" 'TabDragPreviewWindow(image: image)'
require_text "$PREVIEW" 'final class TabDragPreviewWindow: NSWindow'
require_text "$CONTROLLER" 'DetachedTabDocument('
require_text "$CONTROLLER" 'removeDetachedTab(id)'
require_text "$SESSION" 'func openInitialDocument('
require_text "$SESSION" 'pendingInitialDetachedDocument'
require_text "$MANAGER" 'detachedDocument: DetachedTabDocument,'
echo PASS
