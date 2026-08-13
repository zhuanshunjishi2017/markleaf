# Sidebar Search Two-Row Layout Design

## Goal

让 MarkLeaf 的 macOS 侧边栏在中文、英文、日文模式下使用完全一致的顶部布局，避免英文 `Workspace`、`Outline` 和搜索提示文字在单行空间内互相挤压或被截断。

## Approved direction

采用两行工具栏布局（方案 B）：

```text
┌──────────────────────────────────────┐
│  Workspace / Outline       [Folder]  │
│  [ Search Workspace                 ]│
└──────────────────────────────────────┘
```

第一行只承载侧边栏导航和工作区操作，第二行承载搜索输入。两行在所有语言中保持相同的结构、顺序、间距和控件尺寸。

## Layout behavior

- 第一行：`NSSegmentedControl` 位于左侧，打开文件夹按钮固定在右侧。
- 第二行：`NSSearchField` 横向填满可用宽度，左右边距与第一行一致。
- 搜索框保持当前功能：工作区按文件名/内容搜索，大纲按标题过滤。
- 工作区和大纲切换只更新搜索框 placeholder，不切换回单行布局。
- 搜索结果、无结果状态和搜索取消行为保持不变。
- 侧边栏整体顶部高度增加一个搜索行的高度；主体树/列表从第二行下方开始。
- 控件高度、圆角、垂直对齐和间距使用固定值，避免受系统语言文本宽度影响。

## Localization

所有语言共用同一布局约束。placeholder 继续使用现有本地化：

- 工作区：`搜索` / `Search Workspace` / `ワークスペースを検索`
- 大纲：`搜索大纲` / `Search Outline` / `アウトラインを検索`

分段按钮使用完整本地化名称，不允许通过截断、缩写或语言专用宽度解决拥挤问题。

## Implementation boundary

- 只调整 `SidebarView` 的 header 组成和 Auto Layout 约束。
- 不改变搜索服务、搜索结果激活、工作区树、大纲过滤或语言表的业务逻辑。
- 保留现有搜索框的可访问性标签、即时搜索和清空行为。

## Verification

- 在中文、英文、日文模式下检查顶部两行的结构一致性。
- 检查英文 `Workspace`、`Outline`、`Search Workspace` 和 `Search Outline` 不被截断。
- 检查窄侧边栏下搜索框可以缩小，但第一行标签仍保持完整。
- 运行 Swift 构建和现有侧边栏/本地化测试。
