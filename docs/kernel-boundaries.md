# 共享内核与产品边界

`packages/editor-core` 是 Markdown 语法、编辑行为和文档规则的唯一实现。仓库统一构建内核，各产品加载同一组 JavaScript/CSS 产物。产品负责系统接口和控件呈现：AppKit、WinForms、VS Code 的窗口、菜单外观、液态玻璃等效果不进入内核；Markdown 渲染、组件交互和文档内容解释不在产品层重写。

## 一次构建，多个宿主加载

```text
packages/editor-core
    └─ corepack pnpm build:kernel
       ├─ dist/renderer/               渲染、编辑、组件交互和共享 CSS
       │   ├─ editor-web/kernel/       macOS WKWebView / Windows WebView2
       │   └─ vscode-webview/kernel/   VS Code Webview
       └─ dist/document-kernel.cjs     VS Code 专用无 DOM 文档规则
           └─ VS Code Node.js
```

渲染入口需要 DOM，供 macOS、Windows 和 VS Code Webview 加载。`document-kernel.cjs` 是 VS Code 插件当前采用的文档规则入口；macOS 和 Windows 继续使用各自原生编码、文件读写、搜索、预览和恢复实现。Markdown 自定义词法器和规范化规则目前由 VS Code 文档入口复用。

macOS 和 Windows 不引入 `DocumentCoreRuntime` 适配器或 JavaScript 文档运行时。JavaScript 源产物可按宿主分发；执行引擎和原生系统适配器仍按平台编译。内核不通过网络常驻服务运行，也不要求各产品共享进程内的文档实例。

产品生产构建将 `@markleaf/editor-core` 标记为外部模块，再原样复制 `dist/renderer/`；仅 VS Code 扩展进程复制 `document-kernel.cjs`。产品不重新打包内核源码，不直接持有 Tiptap、ProseMirror、CodeMirror、Mermaid、KaTeX 依赖。包内文件比对用于确认分发结果，不作为运行时版本锁步门禁。

## 文档事实与系统接口

```text
macOS / Windows：文件系统提供实际内容、版本和权限
    → 原生实现读取、解码、检测并执行保存决策
    → 产品控件呈现编辑状态

VS Code：TextDocument 提供实际内容、版本和权限
    → 扩展适配器传递字节、版本、用户操作
    → document kernel 解释内容、产生文档投影和保存决策
    → 扩展执行系统读写，返回成功或原始错误
    → Webview 生成编辑状态，产品控件呈现
```

VS Code 的 `TextDocument` 继续拥有文档内容、撤销记录和保存生命周期。Webview 通过 `WorkspaceEdit`、版本与确认队列同步，内核不另建文件副本争夺权威。macOS 和 Windows 继续由原生实现提供文件读取、监视、锁定、原子替换、编码选择、BOM、换行、保存前冲突判断、保存后脏状态和恢复内容格式。

## 职责表

| 内核模块 | 唯一职责 | 产品层保留 |
| --- | --- | --- |
| `document/markdown-syntax.ts`、`document/projection.ts` | Markdown 扩展词法、纯文本投影、搜索匹配、预览摘要；代码和纯文本的字面内容保持 | 枚举文件、路径与权限、原始字节读取、搜索结果控件 |
| `document/encoding.ts` | 编码目录、BOM/端序、检测顺序、无损判断、换行规则、截断预览边界 | 系统字符编码转换原语 |
| `document/transactions.ts` | 保存冲突决策、保存字节准备、版本对应的脏状态、恢复格式及去重 | 文件锁、原子写入、文件监视、时间和版本事实 |
| `command-state.ts`、`getEditorCommandPresentation` | 命令可用／选中状态与语义上下文 | 命令 ID 映射、菜单样式、窗口和系统剪贴板事实 |
| `document-pointer.ts`、`document-links.ts` | 原子节点点击、右键选区、公式／图表展开、锚点和脚注跳转 | 平台主修饰键、外部链接打开、缺失引用提示 |
| `editor-interactions.ts` | 格式刷、块句柄、IME 与交互生命周期 | 块菜单控件和文案 |
| `outline.ts`、`reading-behavior.ts` | 大纲、标题锚点、活动项、滚动、打字机和滚动条策略 | 大纲控件、可用视口上边距 |
| `typography.ts` | 排版样式依赖与 Mermaid 主题声明解析 | 设置来源、样式选择控件 |
| `image-resources.ts` | 原始 Markdown 路径和显示 URL 的区分、图片刷新 | 路径到系统资源 URL 的转换、文件读取与导入 |
| `export-html.ts`、`build/index.ts` | 导出文档、排版、公式字体、共享产物构建分发 | 打印／截图／保存 API |
| `editor.ts`、`source-editor.ts` | 文档模型、序列化、命令、公式源码框、CodeMirror 源码编辑 | 消息适配和文件生命周期 |

## 统一行为与恢复兼容

- 公式／图表第一次点击选中，再次点击切换源码展开；浮层在视口内定位。源码框的粘贴、选区删除和全选走同一命令入口。
- 空选区的粗体、斜体等行内格式设置后续输入格式；修改现有文字时先选择文字。
- 文内链接和脚注在阅读模式直接点击，在编辑模式使用平台主修饰键点击。
- 打字机模式使用可用视口高度的固定比例，产品仅提供工具栏占用的上边距。大纲与导出共用标题锚点规则。
- 混合换行在用户明确选择换行格式前保持。编码不支持或转换有损时报告错误，不用 UTF-8 替代数据制造成功。
- 新恢复快照用一个 `.recovery.json` 原子保存内容与元数据，允许空文档；修订号用字符串保留 64 位精度。旧 `.md` + `.meta` 恢复记录，以及旧会话清单引用的 `.md` 快照有明确读取路径。窗口会话清单继续属于产品层。

## 构建与验证

Node.js 22.12+，Corepack 固定 pnpm 11.9.0。独立包保留各自锁文件；安装必须显式执行，构建发现依赖未准备好时提示安装。

```bash
corepack pnpm install:editor-web
corepack pnpm install:vscode
corepack pnpm build:products
```

`build:products` 只构建一次内核，然后构建原生 Webview、VS Code Webview 和扩展宿主。只构建单个产品可用 `build:editor-web` 或 `build:vscode`，二者均先调用公共内核构建入口。直接运行适配包的 `build` 只消费已有内核产物，缺失时明确失败。

完成统一构建后可复用产物打包：

```bash
MARKLEAF_USE_BUILT_EDITOR_WEB=1 ./apps/macos/script/release/package.sh artifacts/macos
corepack pnpm --dir apps/vscode package
```

测试按内核行为、产品适配和系统 API 边界整理；原生适配测试加载真实公共产物。此次架构调整按用户要求只做类型检查、编译、打包和静态核对，未运行 Vitest、Swift 测试脚本或 `dotnet test`。

2026-09-11 验收记录：VS Code 导出经用户验证通过；macOS 导出预览的 PDF 大纲越界修复后，用户反馈“已通过验收暂未发现异常”。交付版本保持 macOS 1.7.5（build 525）和 VS Code 0.2.7。该反馈属于本轮手工验收，不等同于自动化测试通过或所有平台、场景均已覆盖。
