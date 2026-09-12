#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"

check_state_command() {
  local command="$1"
  if ! awk -v command="$command" '
    $0 ~ "case \"" command "\":" { inrule = 1; next }
    inrule && /return/ { seen_return = 1 }
    inrule && /^[[:space:]]*case / { exit 1 }
    END { exit !seen_return }
  ' "$MENU"; then
    echo "FAIL: $command must explicitly return its menu validation result" >&2
    exit 1
  fi
}

for command in \
  toggleDetachedOutline \
  toggleFollowSystemTheme; do
  check_state_command "$command"
done

echo "PASS"
