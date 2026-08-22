#!/usr/bin/env bash

resolve_markleaf_build_number() {
  local repo_dir="$1"
  local build_number="${MARKLEAF_BUILD:-}"

  if [[ -z "$build_number" ]]; then
    build_number="$(git -C "$repo_dir" rev-list --count HEAD 2>/dev/null)" || return 1
  fi
  if [[ ! "$build_number" =~ ^[0-9]+$ ]]; then
    echo "MarkLeaf build number must contain digits only: $build_number" >&2
    return 1
  fi
  printf '%s\n' "$build_number"
}
