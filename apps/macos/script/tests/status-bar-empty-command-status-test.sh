#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CONTROLLER="$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"

grep -Fq 'lastDocumentIndependentStatus' "$CONTROLLER"
grep -Fq 'statusLabel.stringValue = lastDocumentIndependentStatus' "$CONTROLLER"
grep -Fq 'statusLabel.isHidden = lastDocumentIndependentStatus.isEmpty' "$CONTROLLER"
grep -Fq 'lastDocumentIndependentStatus = statusLabel.stringValue' "$CONTROLLER"
echo PASS
