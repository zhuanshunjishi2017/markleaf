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

# The detached outline is window state. Reading it from the active tab session
# makes the menu unchecked after switching to a newly created tab.
toggle_block="$(sed -n '/case "toggleDetachedOutline":/,/case "treeView":/p' "$MENU")"
printf '%s' "$toggle_block" | grep -Fq 'menuItem.state = viewStateSession?.outlineDetached == true ? .on : .off' \
  || fail "toggleDetachedOutline must read window-level outline state"
printf '%s' "$toggle_block" | grep -Fq 's?.outlineDetached' \
  && fail "toggleDetachedOutline must not read active-tab outline state" || true

# Preserve the original rule: while the outline is shown on the right, the left
# sidebar cannot switch to its duplicate outline tab.
outline_tab_block="$(sed -n '/case "outlineTab":/,/case "toggleDetachedOutline":/p' "$MENU")"
printf '%s' "$outline_tab_block" | grep -Fq 'viewStateSession?.outlineDetached != true' \
  || fail "outlineTab must stay disabled while the detached outline is enabled"

echo "PASS"
