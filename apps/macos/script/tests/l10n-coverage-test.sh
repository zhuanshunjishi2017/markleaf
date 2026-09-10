#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
python3 - "$ROOT_DIR" <<'PY'
from pathlib import Path
import re
import sys

root = Path(sys.argv[1]) / "Sources/MarkLeaf"
source = (root / "Services/L10n.swift").read_text(encoding="utf-8")

# Values that are intentionally identical in every language.
SAME_IN_ALL_LANGUAGES = {"50%", "75%", "90%", "100%", "Mermaid"}


def table(name: str) -> dict[str, str]:
    match = re.search(r"private static let %s: \[String: String\] = \[" % name, source)
    if match is None:
        raise SystemExit(f"FAIL: missing L10n table {name}")
    start = match.end()
    following = re.search(r"\n    private static let ", source[start:])
    body = source[start:start + following.start()] if following else source[start:]
    return dict(re.findall(r'"((?:[^"\\]|\\.)*)":\s*"((?:[^"\\]|\\.)*)"', body))


tables = {
    "zh-Hant": table("zhHantTable"),
    "en": table("englishTable"),
    "ja": table("japaneseTable"),
}

keys: set[str] = set()
for path in root.rglob("*.swift"):
    if path.name == "L10n.swift":
        continue
    text = path.read_text(encoding="utf-8")
    keys |= set(re.findall(r'L10n\.(?:t|f|translate)\("((?:[^"\\]|\\.)*)"', text))

failures = []
for language, translations in tables.items():
    missing = sorted(k for k in keys if k not in translations and k not in SAME_IN_ALL_LANGUAGES)
    if missing:
        failures.append(f"{language} missing {len(missing)} translation(s): " + ", ".join(missing))

if failures:
    raise SystemExit("FAIL: " + "\nFAIL: ".join(failures))

print(f"localization coverage passed ({len(keys)} keys, 3 languages)")
PY
