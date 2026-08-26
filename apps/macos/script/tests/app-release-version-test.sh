#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "$ROOT_DIR/script/release_version.sh"

actual="$(resolve_markleaf_version)"
if [[ "$actual" != "1.4.0" ]]; then
  echo "FAIL: expected default product version 1.4.0, got $actual" >&2
  exit 1
fi

actual="$(MARKLEAF_VERSION=9.8.7 resolve_markleaf_version)"
if [[ "$actual" != "9.8.7" ]]; then
  echo "FAIL: expected MARKLEAF_VERSION override 9.8.7, got $actual" >&2
  exit 1
fi

echo "PASS"
