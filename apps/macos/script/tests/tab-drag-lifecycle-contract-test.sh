#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TAB_BAR="$ROOT_DIR/Sources/MarkLeaf/Views/TabBarController.swift"

fail() { echo "FAIL: $*" >&2; exit 1; }
require() { grep -Fq "$1" "$TAB_BAR" || fail "missing: $1"; }
reject() { grep -Fq "$1" "$TAB_BAR" && fail "unexpected: $1" || true; }

reject 'NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDragged, .leftMouseUp])'
require 'NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDragged, .leftMouseUp])'
require 'stack.arrangedSubviews.contains(cell)'
require 'removed.forEach { view in'
require 'viewDidChangeEffectiveAppearance'
require 'performAsCurrentDrawingAppearance'
require 'cellsByTab.removeValue(forKey: id)'
echo PASS
