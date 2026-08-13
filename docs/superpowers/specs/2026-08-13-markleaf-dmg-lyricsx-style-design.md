# MarkLeaf DMG LyricsX 风格安装画布设计

## 目标

为 MarkLeaf 的 macOS 本地分发 DMG 增加清晰、品牌化的 Finder 安装画布，深度参考 LyricsX 的发布实现，让用户打开 DMG 后立即理解“将 MarkLeaf 拖到 Applications”这一安装动作，同时保留标准拖拽安装结构和缺少 Finder 自动化能力时的安全降级。

## 参考基线

采用 LyricsX 实际 DMG 的视觉和技术基线，而不是只复用抽象布局：

- 背景画布逻辑尺寸为 `640×400`，构建 Retina 资源时使用 `1280×800` 并保留高 DPI 元数据。
- Finder 窗口隐藏工具栏、状态栏和路径栏，窗口 bounds 使用 `{100, 100, 740, 500}`。
- 图标视图关闭自动排列，图标尺寸为 `112`，文字尺寸为 `13`。
- `MarkLeaf.app` 位于 `{165, 220}`，`Applications` 位于 `{475, 220}`。
- 背景图放在 DMG 根目录的隐藏 `.background/MarkLeaf-dmg-background.png`。
- Finder 配置失败时只记录警告，继续输出可正常拖拽安装的标准 DMG。

## 视觉设计

### 顶部品牌区

背景顶部约 `104px` 为白色品牌区，下方用 `1px` 的浅灰分隔线与主体区分开。左侧放 MarkLeaf 应用图标，右侧排列：

- 主标题：`MarkLeaf`，接近 LyricsX 的粗体系统字体层级。
- 副标题：`MARKDOWN WITHOUT DISTRACTION`，全大写、较宽字距、灰蓝色，承担品牌说明而不是安装指令。

顶部左侧使用低对比度的蓝紫渐变 wash，不能干扰标题和图标。MarkLeaf 图标优先复用应用现有 `AppIcon.icns` 对应的原始资源；背景图中只绘制品牌头部，不把应用图标重复绘制到 Finder 覆盖区域。

### 主体安装区

主体使用近白色背景（参考 LyricsX 的 `#F8FAFD`），不使用高饱和大面积渐变。Finder 叠加两个真实项目：左侧 `MarkLeaf.app`，右侧 `Applications`。

两者之间绘制细线箭头和 `drag to` 文案：

- 文案位于画布中心略上方，使用系统字体中等字重。
- 箭头为细灰蓝色横线和线框箭头头部，不使用粗重图标。
- 底部居中显示 `Drag MarkLeaf into Applications to install`，使用更浅的灰蓝色小字号。

该层级对应 LyricsX 的“中心动作提示 + 底部说明”，但所有品牌文字和应用名称均为 MarkLeaf 专属，不复制 LyricsX 的 Logo、品牌名或原文案。

## 资源与打包结构

新增 DMG 专用资源放在 macOS release 脚本附近，不混入应用运行时资源：

- `macos/script/release/dmg-assets/MarkLeaf-dmg-background.svg`：可审查的矢量源文件。
- `macos/script/release/dmg-assets/MarkLeaf-dmg-background.png`：实际放入 DMG 的 PNG，逻辑画布对应 `640×400`，Retina 输出为 `1280×800`。
- `macos/script/release/markleaf-dmg-layout.sh`：负责 `prepare`（复制隐藏背景目录）和 `apply`（在挂载卷上设置 Finder 窗口布局）。

DMG 根目录仍然只包含：

- `MarkLeaf.app`
- 指向 `/Applications` 的 `Applications` 符号链接

`.background` 目录必须隐藏，不得成为用户需要处理的第三个安装项目。

## 打包流程

本地和未来 CI 共用同一套可复用流程：

1. 构建并签名 MarkLeaf `.app`。
2. 创建临时 DMG staging 目录，复制应用和 `/Applications` 符号链接。
3. 调用 `markleaf-dmg-layout.sh prepare` 放入背景资源。
4. 创建可读写 DMG 并挂载。
5. 调用 `markleaf-dmg-layout.sh apply` 设置 Finder 画布；如果 `osascript` 不可用或执行失败，保留标准拖拽 DMG 并继续。
6. 卸载可读写 DMG，转换为压缩 UDZO DMG。
7. 对最终 DMG 执行 `hdiutil verify`，只读挂载检查内容和符号链接。

ZIP、dSYM ZIP 和 SHA-256 文件沿用现有本地分发产物命名和验证约定；本次只改变 DMG 内部视觉和 Finder 布局，不改变应用版本、Bundle ID、签名策略或 ZIP 内容契约。

## 错误处理与兼容性

- 背景资源缺失是打包错误，应在 `prepare` 阶段明确失败，避免生成没有预期品牌背景的“半完成”品牌 DMG。
- Finder 自动化工具缺失、权限不足或 AppleScript 执行失败不是打包致命错误；脚本必须输出警告并保留标准 DMG。
- 应用、`Applications` 符号链接和 DMG 文件系统出现错误时必须失败，不能静默产出不可安装包。
- 不要求 Apple 公证；本地 ad-hoc 签名包必须在交付说明中明确这一点。
- 本次目标架构为 Apple Silicon `arm64`；Universal 或 Intel 产物另行规划。

## 验收标准

自动化验收必须覆盖：

1. 背景资源存在、尺寸为 `1280×800`（或在未启用 Retina 生成时明确为 `640×400`），并被复制到隐藏 `.background` 目录。
2. staging 保留 `MarkLeaf.app` 和指向 `/Applications` 的符号链接。
3. Finder AppleScript 包含窗口 bounds、图标尺寸、文本尺寸、背景路径和两个图标坐标。
4. Finder 配置失败后脚本返回成功，且 staging 内容没有被删除或替换。
5. 最终 DMG 通过 `hdiutil verify`，只读挂载后包含正确应用和符号链接。
6. ZIP、dSYM ZIP 和 SHA-256 验证继续通过。
7. 实际 Finder 窗口视觉检查确认：顶部品牌区清晰、箭头居中、应用和 Applications 位置与背景提示一致。

## 范围边界

- 包含 DMG 背景、Finder 布局脚本、本地打包流程和相关测试。
- 不修改 MarkLeaf 应用内 UI、编辑器功能、应用图标源文件或版本号。
- 不自动创建或覆盖 GitHub Release；本地验收完成后再单独决定发布流程。
- 不引入安装器、联网步骤或额外根目录文件。
