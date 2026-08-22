#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_REPO="$(mktemp -d "${TMPDIR:-/tmp}/markleaf-build-metadata-test.XXXXXX")"
trap 'rm -rf "$TEST_REPO"' EXIT

source "$ROOT_DIR/script/build_metadata.sh"

git -C "$TEST_REPO" init -q
git -C "$TEST_REPO" -c user.name=MarkLeaf -c user.email=tests@markleaf.local \
  commit --allow-empty -q -m first
git -C "$TEST_REPO" -c user.name=MarkLeaf -c user.email=tests@markleaf.local \
  commit --allow-empty -q -m second

actual="$(resolve_markleaf_build_number "$TEST_REPO")"
if [[ "$actual" != "2" ]]; then
  echo "FAIL: expected Git commit-count build 2, got $actual" >&2
  exit 1
fi

actual="$(MARKLEAF_BUILD=987 resolve_markleaf_build_number "$TEST_REPO")"
if [[ "$actual" != "987" ]]; then
  echo "FAIL: expected MARKLEAF_BUILD override 987, got $actual" >&2
  exit 1
fi

if MARKLEAF_BUILD=release resolve_markleaf_build_number "$TEST_REPO" >/dev/null 2>&1; then
  echo "FAIL: non-numeric build override should be rejected" >&2
  exit 1
fi

echo "PASS"
