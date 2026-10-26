#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PREFS="$ROOT/Sources/MarkLeaf/Views/PreferencesWindowController.swift"
L10N="$ROOT/Sources/MarkLeaf/Services/L10n.swift"

grep -Fq 'L10n.t("可视化设置…")' "$PREFS"

python3 - "$L10N" <<'PY'
from pathlib import Path
import sys

l10n = Path(sys.argv[1]).read_text()
for key in ("可视化设置", "可视化设置…", "自适应两端对齐", "允许调整表格列宽"):
    count = l10n.count(f'"{key}":')
    assert count == 3, f"{key}: expected 3 translations, found {count}"

assert '"可视化设置…": "表示設定…"' in l10n
assert '"可视化设置…": "視覺化設定…"' in l10n
assert '"可视化设置…": "Visual Settings…"' in l10n
PY

echo 'Visual settings localization passed'
