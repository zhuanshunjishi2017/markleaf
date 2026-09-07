#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPORT="$ROOT_DIR/Sources/MarkLeaf/Views/ExportWindowController.swift"

require() {
  grep -Fq "$2" "$1" || { echo "FAIL: $3" >&2; exit 1; }
}

require "$EXPORT" 'private var imageQualityRow: NSView?' 'image quality row must be retained for visibility'
require "$EXPORT" 'private var imageQualityRowHeight: NSLayoutConstraint?' \
  'image quality row must have an animatable height constraint'
require "$EXPORT" 'func updateImageControls(animated: Bool = true)' \
  'image controls must have a dedicated visibility updater'
require "$EXPORT" 'setImageQualityRowVisible(imageJPGButton.state == .on' \
  'PNG must hide the JPEG quality slider and JPG must show it'
require "$EXPORT" 'height.animator().constant = visible ? Self.fieldRowHeight : 0' \
  'the JPEG quality row must animate open/closed like the PDF header/footer'
require "$EXPORT" 'NSAnimationContext.runAnimationGroup' \
  'the JPEG quality transition must be animated'
require "$EXPORT" 'NSStackView(views: [imageSettingsHeader, imageSettingsStack])' \
  'the image settings header must sit above its fields'
require "$EXPORT" 'imageSizeSummary.alignment = .center' \
  'the image export summary must be centered under the image controls'
require "$EXPORT" 'window.minSize = NSSize(width: 960, height: 680)' \
  'the export window must enforce a usable minimum size'
require "$EXPORT" 'width: 1160, height: 800' 'the export window must open wider'

echo "PASS"
