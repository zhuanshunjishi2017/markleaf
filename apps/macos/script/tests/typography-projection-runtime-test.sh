#!/usr/bin/env bash
set -euo pipefail
TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bash "$TEST_DIR/editor-web-runtime-test.sh" "${1:-$TEST_DIR/../../Resources/EditorWeb}" typography "${2:-}"
