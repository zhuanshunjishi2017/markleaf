# MarkLeaf DMG LyricsX Style Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 MarkLeaf macOS 本地分发流程加入深度参考 LyricsX 的品牌化 Finder DMG 画布，并重新生成通过完整验收的本地分发四件套。

**Architecture:** 将 DMG 视觉资源、Finder AppleScript 布局和分发打包流程拆成三个边界清晰的 shell 组件。背景资源由 SVG 源文件生成 Retina PNG；布局 helper 只负责 staging/apply 并在 Finder 自动化失败时安全降级；release package 脚本复用现有前端资源和 Swift 构建流程，产出 app ZIP、DMG、dSYM ZIP 和 SHA-256 文件。

**Tech Stack:** Bash、AppleScript、SVG/PNG、Swift Package Manager、`hdiutil`、`codesign`、`dsymutil`、`ditto`、`shasum`、shell regression tests。

## Global Constraints

- 背景画布逻辑尺寸为 `640×400`，实际 Retina PNG 为 `1280×800`。
- Finder 窗口 bounds 为 `{100, 100, 740, 500}`，隐藏 toolbar/statusbar/pathbar。
- Finder icon view 使用不自动排列、图标尺寸 `112`、文字尺寸 `13`。
- `MarkLeaf.app` 坐标为 `{165, 220}`，`Applications` 坐标为 `{475, 220}`。
- DMG 根目录只包含 `MarkLeaf.app` 和指向 `/Applications` 的 `Applications` 符号链接；`.background` 必须隐藏。
- Finder 自动化不可用或失败时，保留标准拖拽 DMG 并以成功状态结束；应用、符号链接或 DMG 文件系统错误必须失败。
- 目标架构为 Apple Silicon `arm64`；不修改应用版本、Bundle ID、签名策略或 ZIP 内容契约。
- 本次不创建或覆盖 GitHub Release。

---

### Task 1: 创建 LyricsX 风格背景资源

**Files:**
- Create: `macos/script/release/dmg-assets/MarkLeaf-dmg-background.svg`
- Create: `macos/script/release/dmg-assets/MarkLeaf-dmg-background.png`
- Test: `macos/script/release/tests/markleaf-dmg-assets-test.sh`

**Interfaces:**
- Consumes: MarkLeaf 品牌名称和现有 AppIcon 视觉方向。
- Produces: 可审查 SVG 源文件和 `1280×800` PNG，供 Task 2 的 helper 复制到 `.background`。

- [ ] **Step 1: 写资源契约测试并确认失败**

创建 shell 测试，检查 SVG 存在且包含 `MarkLeaf`、`MARKDOWN WITHOUT DISTRACTION`、`drag to`，PNG 存在且 `sips` 读回宽高为 `1280` 和 `800`：

```bash
#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
SVG="$ROOT/dmg-assets/MarkLeaf-dmg-background.svg"
PNG="$ROOT/dmg-assets/MarkLeaf-dmg-background.png"
test -f "$SVG"; test -f "$PNG"
grep -Fq 'MarkLeaf' "$SVG"
grep -Fq 'MARKDOWN WITHOUT DISTRACTION' "$SVG"
grep -Fq 'drag to' "$SVG"
test "$(sips -g pixelWidth "$PNG" | awk '/pixelWidth/ {print $2}')" = 1280
test "$(sips -g pixelHeight "$PNG" | awk '/pixelHeight/ {print $2}')" = 800
echo 'PASS: MarkLeaf DMG assets'
```

Run: `bash macos/script/release/tests/markleaf-dmg-assets-test.sh`

Expected: FAIL because the release asset directory and files do not yet exist.

- [ ] **Step 2: 写 640×400 SVG 设计源**

使用系统字体和 LyricsX 的层级创建 SVG：白色 `104px` 顶部品牌区、低对比度蓝紫 wash、MarkLeaf 标题、副标题、浅灰主体、中心 `drag to` 细箭头和底部 `Drag MarkLeaf into Applications to install`。不在背景中绘制 Finder 负责叠加的两个安装图标。

- [ ] **Step 3: 生成 1280×800 PNG 并保留高 DPI 元数据**

优先使用 Quick Look 对 SVG 进行 2× 渲染，再用 `sips` 写入 `144` DPI；生成命令固定为：

```bash
TMP_DIR="$(mktemp -d /private/tmp/markleaf-dmg-asset.XXXXXX)"
qlmanage -t -s 1280 -o "$TMP_DIR" macos/script/release/dmg-assets/MarkLeaf-dmg-background.svg >/dev/null
sips -z 800 1280 "$TMP_DIR/MarkLeaf-dmg-background.svg.png" --out macos/script/release/dmg-assets/MarkLeaf-dmg-background.png >/dev/null
sips -s dpiHeight 144 -s dpiWidth 144 macos/script/release/dmg-assets/MarkLeaf-dmg-background.png >/dev/null
rm -rf "$TMP_DIR"
```

- [ ] **Step 4: 运行资源测试并检查差异**

Run: `bash macos/script/release/tests/markleaf-dmg-assets-test.sh && git diff --check`

Expected: PASS；PNG 为 `1280×800`，SVG 文案完整，且无空白错误。

- [ ] **Step 5: 提交资源**

```bash
git add macos/script/release/dmg-assets macos/script/release/tests/markleaf-dmg-assets-test.sh
git commit -m "feat(macos): add LyricsX-style DMG background"
```

### Task 2: 实现 Finder DMG 布局 helper

**Files:**
- Create: `macos/script/release/markleaf-dmg-layout.sh`
- Test: `macos/script/release/tests/markleaf-dmg-layout-test.sh`

**Interfaces:**
- Consumes: `prepare <stage-dir>` 和 `apply <mounted-volume>` 两个命令；Task 1 的 PNG。
- Produces: `.background/MarkLeaf-dmg-background.png` staging 目录，以及包含 Finder 坐标的 AppleScript；apply 失败时返回 0 并保留安装内容。

- [ ] **Step 1: 写 helper 回归测试并确认失败**

测试创建临时 staging 目录和 `/Applications` 符号链接，调用 `prepare` 检查隐藏背景目录；再用 fake `osascript` 捕获脚本并返回 23，调用 `apply`，断言命令仍成功、内容未被删除，并检查 bounds、尺寸、背景路径和两个坐标。

Run: `bash macos/script/release/tests/markleaf-dmg-layout-test.sh`

Expected: FAIL because helper 尚不存在。

- [ ] **Step 2: 实现 `prepare`**

脚本解析自身目录，复制 `dmg-assets/MarkLeaf-dmg-background.png` 到 `<stage>/.background/`，执行 `chflags hidden`（失败可忽略），背景缺失或 stage 不是目录时以非零状态退出。

- [ ] **Step 3: 实现 `apply`**

使用 `OSASCRIPT_BIN` 环境变量覆盖默认 `/usr/bin/osascript`，向 Finder 发送以下设置：

```applescript
set bounds of targetWindow to {100, 100, 740, 500}
set arrangement of viewOptions to not arranged
set icon size of viewOptions to 112
set text size of viewOptions to 13
set background picture of viewOptions to file ".background:MarkLeaf-dmg-background.png" of targetDisk
set position of item "MarkLeaf.app" of targetDisk to {165, 220}
set position of item "Applications" of targetDisk to {475, 220}
```

`osascript` 缺失或返回非零时输出 warning 并返回 0；不删除 app 或符号链接。

- [ ] **Step 4: 运行 helper 测试并提交**

Run: `bash macos/script/release/tests/markleaf-dmg-layout-test.sh && git diff --check`

Expected: PASS。

```bash
git add macos/script/release/markleaf-dmg-layout.sh macos/script/release/tests/markleaf-dmg-layout-test.sh
git commit -m "feat(macos): add branded Finder DMG layout helper"
```

### Task 3: 接入本地四件套分发打包

**Files:**
- Create: `macos/script/release/package.sh`
- Test: `macos/script/release/tests/package-script-test.sh`

**Interfaces:**
- Consumes: `package.sh [output-dir]`，默认输出到 `macos/dist/release`；Task 1 背景资源和 Task 2 helper。
- Produces: `MarkLeaf-1.1.7-macos-arm64.dmg`、对应 `.zip`、`.dSYM.zip` 和 `SHA256SUMS.txt`。

- [ ] **Step 1: 写打包脚本契约测试并确认失败**

静态测试检查脚本引用 `prepare_resources.sh`、`swift build -c release`、`dsymutil`、`hdiutil create/attach/convert`、`markleaf-dmg-layout.sh prepare/apply`、`codesign --verify` 和 `shasum -a 256`。

Run: `bash macos/script/release/tests/package-script-test.sh`

Expected: FAIL because package script 尚不存在。

- [ ] **Step 2: 实现 app、ZIP、dSYM 和 DMG 构建流程**

脚本不得调用 `pkill`；先运行 `prepare_resources.sh`，再执行：

```bash
swift build --package-path macos -c release -Xswiftc -g
BUILD_BIN="$(swift build --package-path macos -c release --show-bin-path)/MarkLeaf"
dsymutil "$BUILD_BIN" -o "$BUILD_ROOT/MarkLeaf.app.dSYM"
```

组装 `MarkLeaf.app` 后 ad-hoc 签名并验证；ZIP 使用 `ditto -c -k --sequesterRsrc --keepParent`；DMG 使用可读写镜像挂载、helper apply、卸载后转换为 UDZO；最后生成 SHA-256 文件。

- [ ] **Step 3: 运行打包脚本生成本地分发包**

Run: `bash macos/script/release/package.sh /Users/nabian/Documents/Codex/2026-08-13/codex-threads-019ff68e-6ff1-7fc2-8f6f-2/outputs/MarkLeaf-1.1.7-community.1`

Expected: 输出四个文件，Finder 自动化成功则 DMG 带品牌画布，自动化不可用则输出标准可安装 DMG。

- [ ] **Step 4: 提交打包流程**

```bash
git add macos/script/release/package.sh macos/script/release/tests/package-script-test.sh
git commit -m "feat(macos): add local MarkLeaf distribution packaging"
```

### Task 4: 进行最终产物验收

**Files:**
- No source changes expected.

**Interfaces:**
- Consumes: Task 3 生成的四件套。
- Produces: 本地分发验收证据，不上传 GitHub Release。

- [ ] **Step 1: 运行打包脚本测试和前端回归**

Run: `bash macos/script/release/tests/markleaf-dmg-assets-test.sh && bash macos/script/release/tests/markleaf-dmg-layout-test.sh && bash macos/script/release/tests/package-script-test.sh && cd src/EditorWeb && pnpm vitest run`

Expected: shell tests PASS；EditorWeb 现有测试全部 PASS。

- [ ] **Step 2: 验证最终 ZIP 和 DMG**

Run:

```bash
cd /Users/nabian/Documents/Codex/2026-08-13/codex-threads-019ff68e-6ff1-7fc2-8f6f-2/outputs/MarkLeaf-1.1.7-community.1
shasum -a 256 -c SHA256SUMS.txt
unzip -t MarkLeaf-1.1.7-macos-arm64.zip
hdiutil verify MarkLeaf-1.1.7-macos-arm64.dmg
```

Expected: 校验和、ZIP 和 DMG 均成功。

- [ ] **Step 3: 只读挂载检查安装结构**

挂载最终 DMG，断言根目录包含 `MarkLeaf.app`，`Applications` 是指向 `/Applications` 的符号链接，应用版本为 `1.1.7`，然后卸载镜像。

- [ ] **Step 4: 检查工作树和产物清单**

Run: `git diff --check && git status --short && ls -lh /Users/nabian/Documents/Codex/2026-08-13/codex-threads-019ff68e-6ff1-7fc2-8f6f-2/outputs/MarkLeaf-1.1.7-community.1`

Expected: 源码变更已提交，输出目录包含四个分发文件；不创建或上传 GitHub Release。
