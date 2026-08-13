# Sidebar Search Two-Row Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 macOS 侧边栏顶部改为跨语言一致的两行工具栏，使 Workspace/Outline、文件夹按钮和搜索框在中文、英文、日文下都保持可读且不互相挤压。

**Architecture:** 保留 `SidebarView` 现有搜索服务、结果视图、标签切换和本地化逻辑，仅把 header 从单个横向 stack 拆成“导航行 + 搜索行”的纵向 stack。导航行由分段控件、弹性间隔和固定宽度的文件夹按钮组成；搜索行由可横向填充的 `NSSearchField` 组成，主体容器继续约束在 header 下方。

**Tech Stack:** Swift 5、AppKit、Auto Layout、XCTest、Swift Package Manager。

## Global Constraints

- 所有语言共用同一布局结构、顺序、间距和控件尺寸。
- 第一行：`NSSegmentedControl` 左侧，打开文件夹按钮固定在右侧。
- 第二行：`NSSearchField` 横向填满可用宽度。
- 保持工作区按文件名/内容搜索、大纲按标题过滤、即时搜索、清空和激活结果行为不变。
- 工作区/大纲切换只更新 placeholder，不切换回单行布局。
- 不通过截断、缩写或语言专用宽度解决英文/日文标签拥挤。
- 只修改 `SidebarView` 的 header 组成和 Auto Layout；不改变搜索服务、树视图或业务逻辑。
- 必须验证中文、英文、日文，以及默认宽度和窄侧边栏下的布局。

---

### Task 1: 建立两行 header 的布局结构

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift:70-115`（header 控件配置和 stack 组成）
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift:150-174`（header 与主体的约束）

**Interfaces:**
- Consumes: 现有 `tabControl`、`headerOpenFolderButton`、`searchField` 及其 action/placeholder 配置。
- Produces: 仍由 `SidebarView` 对外提供现有控件和搜索行为；新增的内部 stack 不改变公开 API。

- [ ] **Step 1: 写布局回归测试**

在 `macos/Tests/MarkLeafTests/SidebarViewTests.swift` 增加测试，挂载 230pt 宽的 sidebar 后执行 `layoutSubtreeIfNeeded()`，断言：

```swift
let navigationRow = try XCTUnwrap(sidebar.tabControl.superview as? NSStackView)
let header = try XCTUnwrap(navigationRow.superview as? NSStackView)
let searchRow = try XCTUnwrap(sidebar.searchFieldForTesting.superview as? NSStackView)

XCTAssertEqual(header.orientation, .vertical)
XCTAssertEqual(header.arrangedSubviews.count, 2)
XCTAssertEqual(navigationRow.orientation, .horizontal)
XCTAssertEqual(searchRow.orientation, .horizontal)
XCTAssertEqual(sidebar.searchFieldForTesting.frame.width, 218, accuracy: 1)
XCTAssertGreaterThanOrEqual(sidebar.tabControl.frame.maxX, sidebar.tabControl.frame.minX)
XCTAssertEqual(sidebar.headerOpenFolderButton.frame.width, 32, accuracy: 0.5)
```

测试需要一个仅测试使用的 `internal` 只读访问器（例如 `searchFieldForTesting`），并在 `SidebarView.swift` 以 `#if DEBUG` 或测试可见的 internal 方式暴露，不改变生产行为。

- [ ] **Step 2: 运行新增测试确认当前单行实现失败**

Run: `swift test --package-path macos --filter SidebarViewTests.testSidebarHeaderUsesTwoRows`

Expected: FAIL，因为当前 header 是横向单行 stack，且搜索框未独占第二行。

- [ ] **Step 3: 实现两行 stack**

在 `SidebarView.init` 中：

1. 保留 `searchField.controlSize = .small`、24pt 高度约束和现有 action/accessibility 设置。
2. 创建导航行：

```swift
let navigationRow = NSStackView(views: [tabControl, NSView(), headerOpenFolderButton])
navigationRow.orientation = .horizontal
navigationRow.spacing = 6
navigationRow.alignment = .centerY
```

3. 创建搜索行并让搜索框填满宽度：

```swift
let searchRow = NSStackView(views: [searchField])
searchRow.orientation = .horizontal
searchRow.alignment = .width
```

4. 创建 header：

```swift
let header = NSStackView(views: [navigationRow, searchRow])
header.orientation = .vertical
header.spacing = 6
header.alignment = .width
header.translatesAutoresizingMaskIntoConstraints = false
```

5. 保留 header 左右 6pt 边距；将 `containerView.topAnchor` 继续约束为 `header.bottomAnchor + 4`，让树/结果视图自动下移到第二行之后。不要在 `showTab`、`searchChanged` 或搜索服务中增加布局分支。

- [ ] **Step 4: 运行布局测试确认通过**

Run: `swift test --package-path macos --filter SidebarViewTests`

Expected: PASS；英文标签完整显示，搜索行横向填满 header 可用宽度，文件夹按钮宽度仍为 32pt。

- [ ] **Step 5: 提交布局实现**

```bash
git add macos/Sources/MarkLeaf/Views/SidebarView.swift macos/Tests/MarkLeafTests/SidebarViewTests.swift
git commit -m "feat(macos): use two-row sidebar search header"
```

### Task 2: 验证多语言与窄宽度行为

**Files:**
- Modify: `macos/Tests/MarkLeafTests/SidebarViewTests.swift`（仅在 Task 1 测试暴露缺陷时补充断言）

**Interfaces:**
- Consumes: Task 1 生成的两行 header 与既有 `L10n.translate` 测试辅助。
- Produces: 跨语言布局回归证据，不改变应用运行时接口。

- [ ] **Step 1: 增加中文、英文、日文布局断言**

为 `en`、`zh`、`ja` 分别创建 SidebarView 和 230pt、200pt 宽窗口，断言：

```swift
XCTAssertEqual(header.orientation, .vertical)
XCTAssertEqual(header.arrangedSubviews.count, 2)
XCTAssertTrue(sidebar.searchFieldForTesting.placeholderString?.isEmpty == false)
XCTAssertGreaterThanOrEqual(sidebar.searchFieldForTesting.frame.width, 188)
XCTAssertGreaterThanOrEqual(sidebar.tabControl.frame.width, 0)
```

同时断言英文 placeholder 为 `Search Workspace`，切换大纲后为 `Search Outline`；日文 placeholder 使用现有本地化文本。

- [ ] **Step 2: 运行定向测试**

Run: `swift test --package-path macos --filter SidebarViewTests`

Expected: PASS；三种语言均使用相同的两行结构，搜索框不再与导航标签争夺同一行空间。

- [ ] **Step 3: 检查静态差异**

Run: `git diff --check`

Expected: 无空白错误或冲突标记。

- [ ] **Step 4: 提交多语言回归覆盖**

```bash
git add macos/Tests/MarkLeafTests/SidebarViewTests.swift
git commit -m "test(macos): cover multilingual sidebar header layout"
```

### Task 3: 构建并进行最终验证

**Files:**
- No source changes expected.

**Interfaces:**
- Consumes: Task 1 和 Task 2 的源代码、测试与既有搜索逻辑。
- Produces: 可构建的 macOS MarkLeaf 1.1.7 二进制和验证结果。

- [ ] **Step 1: 构建 macOS target**

Run: `swift build --package-path macos`

Expected: 成功完成；允许既有弃用警告，但不得出现编译错误。

- [ ] **Step 2: 运行侧边栏回归测试**

Run: `swift test --package-path macos --filter SidebarViewTests`

Expected: 所有 SidebarViewTests PASS。

- [ ] **Step 3: 运行既有前端回归测试**

Run: `cd src/EditorWeb && pnpm vitest run`

Expected: 现有编辑器测试全部 PASS；本次布局不应改变 Web 编辑器行为。

- [ ] **Step 4: 复核最终工作树**

Run: `git status --short && git log -3 --oneline`

Expected: 仅包含本计划对应的已提交变更，且最新提交信息清晰可追溯。

## Self-review checklist

- 设计规格中的两行结构、跨语言一致性、搜索行为不变、窄宽度和验证要求均由 Task 1–3 覆盖。
- 计划没有引入搜索服务或本地化业务逻辑改动。
- 测试访问器只用于观察布局，不参与生产运行时行为。
- 若 Swift XCTest target 因本机 CommandLineTools 缺少 XCTest 模块而无法启动，需记录为环境阻塞，并至少提供 `swift build` 与可执行的静态检查结果，不得宣称测试通过。
