#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"

assert_contains() {
  local file="$1"
  local needle="$2"
  if ! grep -Fq -- "$needle" "$file"; then
    echo "missing changelog entry: $file: $needle" >&2
    exit 1
  fi
}

for file in "$ROOT_DIR"/Changelog/changelog.{zh-Hans,zh-Hant,en,ja}.md; do
  assert_contains "$file" "1.2.5"
done

assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "源码模式支持撤销、重做"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "检测不符合 CommonMark 规则"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "导出设置持久化"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "PDF 页眉页脚"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "柠檬海盐"

assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Source mode supports undo and redo"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "unsafe CommonMark emphasis boundaries"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Persist export settings"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "PDF headers and footers"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Saltlemon"

echo "changelog content policy passed"
