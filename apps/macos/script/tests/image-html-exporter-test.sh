#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-image-export-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
# Compile the real option declarations without the unrelated accessory UI.
python3 - "$ROOT_DIR" "$BUILD_DIR" <<'PY'
import pathlib, sys
root, out = map(pathlib.Path, sys.argv[1:])
services = root / 'Sources/MarkLeaf/Services'
options = (services / 'ExportAccessory.swift').read_text().split('/// 保存面板附属视图')[0]
paper = (services / 'PDFGenerator.swift').read_text().split('/// 将编辑器导出的 HTML')[0]
(out / 'Options.swift').write_text(options + '\n' + paper)
(out / 'L10n.swift').write_text('enum L10n { static func t(_ s: String) -> String { s } }')
PY
cp "$ROOT_DIR/script/tests/ImageHTMLExporterTest.swift" "$BUILD_DIR/main.swift"
SOURCES=("$ROOT_DIR/Sources/MarkLeaf/Services/ImageHTMLExporter.swift")
if [[ -f "$ROOT_DIR/Sources/MarkLeaf/Services/ImageExportPolicy.swift" ]]; then
  SOURCES+=("$ROOT_DIR/Sources/MarkLeaf/Services/ImageExportPolicy.swift")
fi
swiftc -module-cache-path "$BUILD_DIR/module-cache" "${SOURCES[@]}" \
  "$BUILD_DIR/Options.swift" "$BUILD_DIR/L10n.swift" "$BUILD_DIR/main.swift" -o "$BUILD_DIR/test"
"$BUILD_DIR/test" "$BUILD_DIR"
