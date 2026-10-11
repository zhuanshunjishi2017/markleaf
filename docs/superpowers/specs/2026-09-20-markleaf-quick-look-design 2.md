# MarkLeaf Quick Look 设计

**日期：** 2026-09-20  
**状态：** 待实现  
**适用平台：** macOS 13+

## 目标

为 MarkLeaf 增加一个独立的 Quick Look 预览扩展，使 Finder、桌面和文件选择器可以直接预览 `.md`、`.markdown` 与 `.txt` 文件，而无需启动完整 MarkLeaf 编辑器。

预览应当轻量、只读、离线，并在常见 Markdown 文档上提供与 MarkLeaf 编辑区一致的基本阅读层次。当前文件消息上限保持为 **32 MiB**，不改写为 100 MB 或其他更高上限。

## 非目标

- 不把完整 `editor-core`、ProseMirror、CodeMirror、Mermaid 或编辑器资源打入扩展。
- 不在 Quick Look 中提供编辑、查找、复制格式化内容、导出、公式编辑、任务勾选或链接跳转。
- 不访问网络资源，不执行 Markdown 中的脚本、HTML 脚本或外部网页。
- 不改变主 App 的文件关联、编辑器渲染和 Windows 端实现。

## 用户体验

- Finder 选中支持的文件后按空格，显示只读预览。
- `.txt` 按纯文本显示；`.md` / `.markdown` 显示轻量 Markdown 排版。
- 默认使用系统的浅色/深色偏好，CSS 通过 `prefers-color-scheme` 响应系统主题。
- 文档中的相对图片可以显示；图片仅允许来自当前文档所在目录及其子目录。
- 不支持的文件类型、无法读取的文件、超过 32 MiB 的文件显示明确的预览失败提示，不崩溃、不启动主 App。

## 架构

### 扩展包

新增 `MarkLeafQuickLook.appex`，入口实现 `QLPreviewingController`。扩展接收 Quick Look 提供的文件 URL，读取安全范围内的本地内容，然后把生成的静态 HTML 加载到 `WKWebView`。

扩展只依赖系统框架：`QuickLookUI`、`WebKit`、`UniformTypeIdentifiers`、`Foundation`。轻量 Markdown 解析与 HTML 转义放在扩展自己的 Swift 模块中，不引用主 App 的编辑器内核。

### 预览管线

```text
Quick Look file URL
        |
        v
安全范围检查 + 文件大小检查 + UTF-8/编码读取
        |
        v
QuickLookMarkdownRenderer
        |
        v
静态 HTML + 内置 CSS + 本地图片映射
        |
        v
WKWebView（禁用脚本、网络和外部导航）
```

### 支持的 Markdown 子集

第一阶段支持：

- ATX 标题（`#` 至 `######`）；
- 段落与软换行；
- 无序列表、有序列表、任务列表的静态显示；
- 引用；
- fenced code block，保留代码文本和语言标签；
- 简单表格；
- 粗体、斜体、删除线、行内代码、链接文本；
- 行内图片与独立图片；
- 行内 `$...$` 与块级 `$$...$$` 以安全的公式占位文本显示，不加载 KaTeX。

原始 HTML 仅允许安全的文本级标签；`script`、`style`、`iframe`、`object`、`embed`、事件属性和 `javascript:` URL 一律转义或丢弃。

## 文件与资源安全

### 文件读取

- 只接受 `.md`、`.markdown`、`.txt` 及系统 UTI 对应的纯文本类型。
- 使用 Quick Look 提供的 URL；需要时调用 security-scoped resource 访问，并在完成后停止访问。
- 文件大小超过 32 MiB 时在读取前拒绝，避免扩展占用过多内存。
- 优先 UTF-8；无法解码时使用现有 macOS 编码策略的安全降级，不因单个非法字节崩溃。

### 图片

- 只解析相对路径图片。
- 通过标准化 URL 检查目标必须位于文档目录或其子目录，拒绝 `..` 穿越、绝对路径、网络协议和自定义协议。
- HTML 使用 `file://` 本地资源并限制 `WKWebView` 的 read access 根目录为文档目录。
- 图片加载失败时保留替代文本，不影响正文显示。

### WebView 隔离

- 禁用 JavaScript。
- 不允许任意网络请求和新窗口导航。
- 注入严格 CSP，只允许内联 CSS、`file:` 图片和文档自身资源。
- 不把主 App 的用户数据、设置或编辑会话暴露给扩展。

## 构建与打包

当前 macOS 工程通过 SwiftPM 和脚本手工组装 `.app`，因此 Quick Look 扩展也采用显式构建步骤：

1. 新增独立 Quick Look 源码目录和构建入口；
2. 生成 `.appex/Contents/MacOS/MarkLeafQuickLook`、`Info.plist` 与资源目录；
3. 将扩展复制到 `MarkLeaf.app/Contents/PlugIns/`；
4. 对扩展和主 App 一起执行 ad-hoc 签名与 `codesign --verify --deep --strict`；
5. release 打包脚本与本地 `build_and_run.sh` 使用同一扩展装配逻辑，避免本地构建和发布产物结构不同。

扩展 Info.plist 必须声明：

- `CFBundlePackageType= XPC!`；
- `NSExtensionPointIdentifier = com.apple.quicklook.preview`；
- `NSExtensionPrincipalClass` 指向预览控制器；
- 支持 `net.daringfireball.markdown`、`public.plain-text` 及 `.markdown` 对应类型。

## 错误处理

- 读取失败、编码失败、超限、类型不支持分别映射为可读的静态错误页面。
- 解析器遇到未知 Markdown 结构时保留原始文本，不抛出致命异常。
- WebView 加载错误只显示预览失败状态，不回退启动主 App。
- 任何单个图片、表格行或代码块解析失败都不能影响同一文件其他内容的显示。

## 测试与验收

### 纯逻辑测试

- 标题、段落、列表、任务列表、引用、代码块、表格和行内格式的 HTML 输出；
- HTML 转义和危险标签/协议过滤；
- 相对图片路径规范化、目录穿越拒绝和替代文本；
- 32 MiB 边界：小于等于限制可读，超过限制被拒绝；
- UTF-8 正常文本与非法字节降级；
- 公式占位文本不会执行脚本或加载外部资源。

### Bundle 契约测试

- `.appex` 目录结构完整；
- Quick Look 扩展点和主类声明正确；
- 主 App 包含扩展且签名验证通过；
- 主 App 的原有文档类型声明保持不变。

### 真实验收

使用 `qlmanage -p` 和 Finder 空格预览验证：

- `.md`、`.markdown`、`.txt` 三类文件；
- 中英文、CJK、代码块、列表、表格和相对图片；
- 浅色/深色系统主题切换；
- 文件被删除、无权限、超 32 MiB、图片路径越界等失败场景；
- Quick Look 关闭后主 App 可正常启动，且不会残留 MarkLeaf 进程。

## 发布边界

Quick Look 首版只在 macOS 产物中提供。Windows、VS Code 和排版引擎实验分支不受影响。实现完成后先进行本地 `.appex` 验收，再决定是否进入 1.7.7 或后续版本，不能默认带入当前 1.7.6 热更新。
