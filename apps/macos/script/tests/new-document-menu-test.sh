#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MENU_FILE="$ROOT_DIR/Sources/MarkLeaf/Support/NativeMenuBuilder.swift"
SIDEBAR_FILE="$ROOT_DIR/Sources/MarkLeaf/Views/SidebarView.swift"
SESSION_FILE="$ROOT_DIR/Sources/MarkLeaf/Services/EditorSession.swift"
SHORTCUT_FILE="$ROOT_DIR/Sources/MarkLeaf/Support/ShortcutSettings.swift"

grep -Fq 'popup(L10n.t("新建"), newMenu)' "$MENU_FILE"
grep -Fq 'commandItem(L10n.t("Markdown 文件"), "new", key: "n")' "$MENU_FILE"
grep -Fq 'commandItem(L10n.t("文本文件"), "newPlainText", key: "n", mask: [.command, .option])' "$MENU_FILE"
grep -Fq 'popupItem(L10n.t("新建文件"), newFileMenu(in: targetDirectory))' "$SIDEBAR_FILE"
grep -Fq 'createWorkspaceFile(at directory: URL, kind: NewDocumentKind = .markdown)' "$SESSION_FILE"
grep -Fq 'ShortcutEntry(command: "newPlainText"' "$SHORTCUT_FILE"

echo "PASS"
