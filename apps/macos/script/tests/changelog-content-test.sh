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
  assert_contains "$file" "1.2.6"
  assert_contains "$file" "1.3.0"
  assert_contains "$file" "1.3.2"
done

# 1.3.2 由简体中文人工定稿后同步到其他语言；只校验当前版本的核心语义，
# 避免旧版本的人工精简或重写让发布检查产生误报。
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "新建文件默认编码"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "汉字优先字型"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "合并为「查找与替换」"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "LaTeX 公式"

assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "新檔案預設編碼"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "漢字優先字型"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "合併為「尋找與取代」"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hant.md" "LaTeX 公式"

assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "default encoding for new files"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Preferred Han Glyphs"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Merge Find and Replace into one Find & Replace entry"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "rendered LaTeX formula"

assert_contains "$ROOT_DIR/Changelog/changelog.ja.md" "新規ファイルの既定エンコーディング"
assert_contains "$ROOT_DIR/Changelog/changelog.ja.md" "漢字優先字形"
assert_contains "$ROOT_DIR/Changelog/changelog.ja.md" "「検索」と「置換」を「検索と置換」に統合"
assert_contains "$ROOT_DIR/Changelog/changelog.ja.md" "レンダリング済みの LaTeX 数式"

echo "changelog content policy passed"
