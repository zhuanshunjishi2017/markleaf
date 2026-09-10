#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLES="$ROOT_DIR/Samples"

require_contains() {
  grep -Fq "$2" "$1" || { echo "FAIL: $1 missing '$2'" >&2; exit 1; }
}

for file in alert-examples.md yaml-front-matter-basic.md yaml-front-matter-advanced.md; do
  test -s "$SAMPLES/$file" || { echo "FAIL: missing $file" >&2; exit 1; }
done

for alert in NOTE TIP IMPORTANT WARNING CAUTION; do
  require_contains "$SAMPLES/alert-examples.md" "> [!$alert]"
done
require_contains "$SAMPLES/yaml-front-matter-basic.md" 'tags:'
require_contains "$SAMPLES/yaml-front-matter-advanced.md" 'reviewers:'
require_contains "$SAMPLES/yaml-front-matter-advanced.md" 'custom:'

echo "PASS"
