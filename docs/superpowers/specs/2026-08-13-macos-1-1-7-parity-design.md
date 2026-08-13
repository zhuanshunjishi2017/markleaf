# macOS 1.1.7 Windows 4d22f28 Parity Design

## Goal

同步 Windows `4d22f28`（v1.1.6）中的侧栏搜索、段落块句柄菜单、段前/段后插入行和相关样式/本地化调整到 macOS，同时将 macOS 版本提升到 1.1.7，并保留 macOS 当前格式刷与文档生命周期改动。

## Scope

- 共享编辑器：块句柄装饰、块类型标签、块菜单事件、插入段前/段后空段落命令。
- macOS AppKit：`blockMenuRequested` 协议、原生段落菜单、菜单状态、块高亮清理。
- macOS 侧栏：工作区文件名/内容搜索，大纲标题过滤；点击搜索结果打开文档、退出搜索并定位工作区树。
- 样式：待办列表布局和主题强调色、日文印刷字体、黄色纸张主题色。
- 本地化、changelog、版本号 1.1.7。

不包含 Windows 专属 WinForms/Win32/MSI 变更，也不删除或重写 macOS 现有格式刷实现。

## Architecture

前端沿用现有 Tiptap/ProseMirror 与 `window.chrome.webview` shim。块句柄通过 ProseMirror decoration 渲染；点击后发送一个带 CSS 坐标和文档位置的消息。macOS 将 CSS 坐标换算成屏幕坐标，用 `NSMenu` 弹出原生菜单，命令仍通过现有 EditorSession → WKWebView command bridge 执行。

侧栏搜索使用 AppKit 原生输入控件和结果视图。工作区搜索在后台递归扫描，限制 `.md`/`.txt`，支持取消；大纲搜索只过滤已收到的标题模型。搜索结果点击后关闭搜索状态，打开文档并展开/选择树路径。

## Behavior

- 搜索模式只在当前侧栏标签有可搜索内容时启用。
- 清空或取消搜索恢复原工作区/大纲视图。
- 搜索结果点击：退出搜索、打开文档、展开祖先目录、选择目标文件。
- 块句柄只在可编辑文本块的空选区显示；点击后块高亮，菜单关闭后清除高亮。
- “段前插入行/段后插入行”插入空段落，并由编辑器保持焦点。
- 非文档变更的块高亮事务不标记 dirty、不刷新大纲。

## Error Handling

- 搜索遇到不可访问或已删除文件时跳过该项；取消搜索不显示错误。
- 打开搜索结果失败沿用现有文档错误提示。
- 协议消息必须通过现有 schema 校验，坐标和位置必须为非负数。
- 块菜单仅在编辑器已加载时弹出；命令状态沿用当前命令路由。

## Versioning

将 macOS AppVersion、SwiftPM 运行时版本标识、macOS changelog 四语言文档更新为 1.1.7，并记录 Windows 1.1.6 parity 内容。

## Testing

- Vitest：块句柄显示/标签、段前段后插入、协议消息验证、非文档事务不产生 dirty。
- XCTest：搜索结果匹配与排序/取消模型、侧栏搜索状态、块菜单命令列表、版本和本地化关键文案、样式同步关键规则。
- 运行 `pnpm test -- --run`、`swift test`；若 Swift SDK 环境仍阻塞，记录具体工具链错误并完成可执行的静态/前端验证。
