#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-closed-tab-restore-test.XXXXXX")"
trap 'rm -rf "$BUILD_DIR"' EXIT
swift build --package-path "$ROOT_DIR" --build-tests
python3 - "$ROOT_DIR" "$BUILD_DIR" <<'PY'
import os, pathlib, subprocess, sys
root, out = map(pathlib.Path, sys.argv[1:])
lists = list((root / '.build').rglob('*testable*.LinkFileList'))
object_dir = root / '.build/out/Intermediates.noindex/MarkLeaf.build/Debug/MarkLeaf-p.build/Objects-normal/arm64'
if lists:
    linkfile = max(lists, key=lambda p: p.stat().st_mtime)
    objects = [x.strip().strip('"') for x in linkfile.read_text().splitlines()
               if not x.strip().strip('"').endswith('/main.o')]
    module = root / '.build/out/Products/Debug'
elif object_dir.is_dir():
    # Ignore stale numbered copies left by prior Xcode builds; link only
    # objects corresponding to the current target's actual Swift sources.
    names = {p.stem + '.o' for p in (root / 'Sources/MarkLeaf').rglob('*.swift')}
    objects = [str(p) for p in object_dir.glob('*.o') if p.name in names and p.name != 'main.o']
    module = object_dir
else:
    module = next((root / '.build').glob('*/debug/Modules'))
    objects = [str(p) for p in (module.parent / 'MarkLeaf.build').glob('*.swift.o') if p.name != 'main.swift.o']
result = subprocess.run(['swiftc', '-I', str(module), '-module-cache-path', str(out / 'module-cache'),
    str(root / 'script/tests' / os.environ.get('MARKLEAF_BEHAVIOR_TEST_SOURCE', 'ClosedTabRestoreBehaviorTest.swift')), *objects, '-o', str(out / 'test')])
sys.exit(result.returncode)
PY
MARKLEAF_APP_SUPPORT_DIR="$BUILD_DIR/settings" "$BUILD_DIR/test"
rg -q 'rebuildSuspendedTab\(closingTab\)' "$ROOT_DIR/Sources/MarkLeaf/Views/EditorWindowController.swift"
