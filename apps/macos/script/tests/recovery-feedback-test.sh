#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EDITOR_SESSION="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
WINDOW_MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"
WINDOW_CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
TAB_BAR="$ROOT_DIR/Sources/MarkLeaf/Views/TabBarController.swift"
SCHEDULER="$ROOT_DIR/Sources/MarkLeaf/Services/SessionWriteScheduler.swift"
RECOVERY="$ROOT_DIR/Sources/MarkLeaf/Services/RecoveryService.swift"
L10N="$ROOT_DIR/Sources/MarkLeaf/Services/L10n.swift"
grep -Fq 'onRecoveryWriteFailure' "$EDITOR_SESSION"
grep -Fq 'onRecoveryWriteSuccess' "$EDITOR_SESSION"
grep -Fq 'markRecoveryUnavailable' "$WINDOW_MANAGER"
grep -Fq 'onWriteFailure' "$SCHEDULER"
grep -Fq 'recoveryUnavailable ?' "$TAB_BAR"
grep -Fq '恢复保护暂时不可用' "$L10N"
grep -Fq -- '-> Bool' "$RECOVERY"
grep -Fq 'onRecoveryWriteFailure' "$WINDOW_CONTROLLER"
printf 'PASS recovery feedback contracts\n'
