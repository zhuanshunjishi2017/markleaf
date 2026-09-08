#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
MENU="$ROOT_DIR/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
WINDOWS="$ROOT_DIR/macos/Sources/MarkLeaf/App/AppWindowManager.swift"
CONTROLLER="$ROOT_DIR/macos/Sources/MarkLeaf/Views/OptionalFontsWindowController.swift"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

require_text() {
  local target="$1"
  local text="$2"
  [[ -f "$target" ]] || fail "missing file: $target"
  grep -Fq "$text" "$target" || fail "missing '$text' in $target"
}

require_text "$MENU" 'commandItem(L10n.t("安装可选字体…"), "installOptionalFonts")'
require_text "$MENU" '"installOptionalFonts"'
require_text "$MENU" 'case "installOptionalFonts":'
require_text "$MENU" 'AppWindowManager.shared.showOptionalFonts()'
require_text "$WINDOWS" 'private var optionalFontsController: OptionalFontsWindowController?'
require_text "$WINDOWS" 'func showOptionalFonts()'
require_text "$WINDOWS" 'optionalFontsController = controller'
require_text "$CONTROLLER" 'final class OptionalFontsWindowController'
require_text "$CONTROLLER" 'OptionalFontCatalog.packs'
require_text "$CONTROLLER" 'OptionalFontPresentation.actions'
require_text "$CONTROLLER" 'installer.install('
require_text "$CONTROLLER" 'installer.uninstall('
require_text "$CONTROLLER" '.licenseURL'

echo "PASS"
