#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-export-window-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
swift build --package-path "$ROOT_DIR" --build-tests
python3 - "$ROOT_DIR" "$BUILD_DIR" <<'PY'
import pathlib, subprocess, sys
root, out = map(pathlib.Path, sys.argv[1:])
# SwiftPM's native and Xcode build backends use different object layouts.
lists = list((root / '.build').rglob('*testable*.LinkFileList'))
if lists:
    linkfile = max(lists, key=lambda p: p.stat().st_mtime)
    objects = [x.strip().strip('"') for x in linkfile.read_text().splitlines()
               if not x.strip().strip('"').endswith('/main.o')]
    module = root / '.build/out/Products/Debug'
elif (root / '.build/out/Intermediates.noindex/MarkLeaf.build/Debug/MarkLeaf-p.build/Objects-normal/arm64').is_dir():
    # Xcode 27's SwiftPM backend emits per-source objects and the swiftmodule
    # beside them under Intermediates.noindex instead of using LinkFileList.
    object_dir = root / '.build/out/Intermediates.noindex/MarkLeaf.build/Debug/MarkLeaf-p.build/Objects-normal/arm64'
    # File Provider may leave numbered copies from older builds beside the
    # canonical objects. Only link objects for the current source filenames.
    names = {p.stem + '.o' for p in (root / 'Sources/MarkLeaf').rglob('*.swift')}
    objects = [str(p) for p in object_dir.glob('*.o') if p.name in names and p.name != 'main.o']
    module = object_dir
else:
    module = next((root / '.build').glob('*/debug/Modules'))
    objects = [str(p) for p in (module.parent / 'MarkLeaf.build').glob('*.swift.o') if p.name != 'main.swift.o']
subprocess.run(['swiftc', '-I', str(module), '-module-cache-path', str(out / 'module-cache'),
    str(root / 'script/tests/ExportWindowBehaviorTest.swift'), *objects, '-o', str(out / 'test')], check=True)
PY
MARKLEAF_APP_SUPPORT_DIR="$BUILD_DIR/settings" "$BUILD_DIR/test"
