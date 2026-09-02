#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
POLICY="$ROOT_DIR/Sources/MarkLeaf/Services/IncomingFileRoutingPolicy.swift"
ROUTER="$ROOT_DIR/Sources/MarkLeaf/Services/IncomingFileRouter.swift"
SETTINGS="$ROOT_DIR/Sources/MarkLeaf/Services/AppSettings.swift"
MANAGER="$ROOT_DIR/Sources/MarkLeaf/App/AppWindowManager.swift"

require() {
  local file="$1" text="$2" message="$3"
  if ! grep -Fq "$text" "$file"; then
    echo "FAIL: $message" >&2
    exit 1
  fi
}

# 外部文件模式必须包含"当前窗口新标签页"，否则多标签下语义含糊。
require "$SETTINGS" 'case newTab' 'external file modes must include opening in a new tab'
require "$SETTINGS" '在当前窗口的新标签页中打开' 'the new-tab mode must be user visible'
require "$SETTINGS" '在当前标签页中打开' 'replace-active mode must say which tab it touches'

# 路由必须支持"活动窗口新标签页"这一动作。
require "$POLICY" 'case newTabInActiveWindow' 'routing must expose an active-window new-tab action'
require "$POLICY" 'case .newTab:' 'policy must route the new-tab mode explicitly'
require "$ROUTER" 'newTabInActiveWindow: (URL) -> Void' 'router must let the caller open a new tab in the active window'

# AppWindowManager 接线：去重覆盖所有窗口的所有标签，激活走 activateTab，新标签走 requestOpenFile。
require "$MANAGER" 'newTabInActiveWindow: { [weak self] url in' 'window manager must wire the new-tab action'
require "$MANAGER" 'windowSession?.requestOpenFile(url)' 'new-tab action must reuse the window tab pipeline'
require "$MANAGER" 'controller.activateTab(tab.tabID, animated: true)' 'duplicate activation must select the existing tab'
require "$MANAGER" 'tabStore.tabs' 'duplicate detection must cover background tabs too'

echo "PASS"
