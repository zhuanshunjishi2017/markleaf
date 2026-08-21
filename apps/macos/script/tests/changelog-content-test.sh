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
  assert_contains "$file" "1.3.1"
done

assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "源码模式支持撤销、重做"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "检测不符合 CommonMark 规则"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "导出设置持久化"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "PDF 页眉页脚"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "柠檬海盐"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "插入注释"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "重设注释编号"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "文件夹图标"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "自动隐藏滚动条"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "查找与替换文本框"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "单击文件后选中高亮"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "选择另一个文件夹时应用崩溃"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "隐藏侧边栏启动后首次显示"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "当前已打开文件切换 LF/CRLF"
assert_contains "$ROOT_DIR/Changelog/changelog.zh-Hans.md" "打开文件或执行保存会意外重新显示侧边栏"

assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Source mode supports undo and redo"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "unsafe CommonMark emphasis boundaries"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Persist export settings"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "PDF headers and footers"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Saltlemon"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Insert Footnote"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Reset Footnote Number"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "folder icon"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Auto-Hide Scrollbars"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "Find and Replace fields"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "single-click selection highlight"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "choosing another folder"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "hidden-sidebar launch"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "convert the current open file"
assert_contains "$ROOT_DIR/Changelog/changelog.en.md" "opening or saving a document unexpectedly revealing"

echo "changelog content policy passed"
