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

release_section() {
  local file="$1"
  local version="$2"
  awk -v version="$version" '
    $0 ~ "^## " version "([[:space:]]|$)" { in_release = 1; next }
    in_release && /^## / { exit }
    in_release { print }
  ' "$file"
}

assert_release_contains() {
  local file="$1"
  local version="$2"
  local needle="$3"
  if ! release_section "$file" "$version" | grep -Fq -- "$needle"; then
    echo "missing changelog entry in $version: $file: $needle" >&2
    exit 1
  fi
}

assert_release_not_contains() {
  local file="$1"
  local version="$2"
  local needle="$3"
  if release_section "$file" "$version" | grep -Fq -- "$needle"; then
    echo "unexpected changelog entry in $version: $file: $needle" >&2
    exit 1
  fi
}

for file in "$ROOT_DIR"/Changelog/changelog.{zh-Hans,zh-Hant,en,ja}.md; do
  assert_contains "$file" "1.4.0"
  assert_contains "$file" "1.2.5"
  assert_contains "$file" "1.2.6"
  assert_contains "$file" "1.3.0"
  assert_contains "$file" "1.3.1"
  assert_contains "$file" "1.3.2"
done

# 1.4.0 内容：新增/更改/修复在四种语言中保持一致，且不混入旧版本的编码条目。
assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "1.4.0" "检查更新"
assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "1.4.0" "触控板捏合缩放"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "1.4.0" "新建文件默认编码"

assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "1.4.0" "檢查更新"
assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "1.4.0" "觸控板捏合縮放"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "1.4.0" "新檔案預設編碼"

assert_release_contains "$ROOT_DIR/Changelog/changelog.en.md" "1.4.0" "Check for Updates"
assert_release_contains "$ROOT_DIR/Changelog/changelog.en.md" "1.4.0" "pinch-to-zoom"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.en.md" "1.4.0" "default encoding for new files"

assert_release_contains "$ROOT_DIR/Changelog/changelog.ja.md" "1.4.0" "アップデートの確認"
assert_release_contains "$ROOT_DIR/Changelog/changelog.ja.md" "1.4.0" "ピンチズーム"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.ja.md" "1.4.0" "新規ファイルの既定エンコーディング"

# 1.3.2 只包含本次发布新增的修复；1.3.1 的内容必须保留在独立版本段落。
assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "1.3.2" "Mermaid"
assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "1.3.2" "应用格式刷时偶尔失效"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "1.3.2" "新建文件默认编码"

assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "1.3.2" "Mermaid"
assert_release_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "1.3.2" "套用格式刷時偶爾失效"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "1.3.2" "新檔案預設編碼"

assert_release_contains "$ROOT_DIR/Changelog/changelog.en.md" "1.3.2" "Mermaid"
assert_release_contains "$ROOT_DIR/Changelog/changelog.en.md" "1.3.2" "Format Painter occasionally failing to apply"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.en.md" "1.3.2" "default encoding for new files"

assert_release_contains "$ROOT_DIR/Changelog/changelog.ja.md" "1.3.2" "Mermaid"
assert_release_contains "$ROOT_DIR/Changelog/changelog.ja.md" "1.3.2" "書式のコピーがまれに適用されない"
assert_release_not_contains "$ROOT_DIR/Changelog/changelog.ja.md" "1.3.2" "新規ファイルの既定エンコーディング"

echo "changelog content policy passed"
