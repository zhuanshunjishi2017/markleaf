# MarkLeaf

[English](./docs/README.en.md) | [日本語](./docs/README.ja.md) | [繁體中文](./docs/README.zh-TW.md)

这是一个原生轻量化 Markdown 可视化编辑器，追求简洁的界面与排版，提供专注于思考、阅读与写作的空间。

项目最初由 [fcz](https://github.com/zhuanshunjishi2017) 发起并制作，初版仅支持 Windows 平台，后由 [Na Bian](https://github.com/Na-Bian) 提供了 macOS 版本的支持。**当前，Windows 版本与 macOS 版本共同更新。**

## 应用截图

![screenshot-light](./docs/assets/screenshot-light.png)

## 功能介绍

### 丰富的排版样式与配色方案

#### **排版样式**

应用内置丰富的排版样式，例如：

- **网页**：适合屏幕阅读和日常编辑，是大多数编辑器较为主流的 Markdown 渲染风格，追求效率和清晰的体验。**(上图中左上窗口所用排版)**
- **印刷品**：采用印刷品常用的衬线字体与黑体排版，段落两端对齐，首行缩进，标题居中，页面留白宽裕，模拟现代书籍排版效果。适合长文写作与阅读。
- **LaTeX**：采用 CMU 字体和类似于 LaTeX 的 document 文档的排版，引用与提示框采用 tcolorbox 风格，尽可能贴近 LaTeX渲染的风格。**（上图中中部窗口所用排版）**
- **铅字印刷**：采用特里王老师制作的汇文、朝华系列字体以及京华老宋体，在印刷品布局基础上营造更为复古的样式。**（上图中右上窗口所用排版）**

> [!NOTE]
> 部分主题可能需要用到特定的字体以获得更佳体验，您可以前往以下页面，或直接从 [Release](https://github.com/zhuanshunjishi2017/markleaf/releases) 中下载相关字体包并将其安装到计算机上。
> 
> - [Computer Modern 系列字体](https://www.fontsquirrel.com/fonts/computer-modern)（LaTeX 默认排版字体）
> - [汇文、朝华系列字体以及京华老宋体](https://huozi.cool/) （铅字印刷排版，由特里王制作的免费字体）
> - [霞鹜文楷](https://github.com/lxgw/LxgwWenKai) （由 Lxgw 制作的优秀开源开源中文字体）

#### 配色方案

应用支持**多种颜色主题**，包含深色与浅色，<strong>实现了 Win32 菜单对深色模式的支持。</strong>以下是部分预置的颜色主题效果。

> [!TIP]
> 由于配色方案与渲染主题**都是 CSS 样式**，故您可以**完全自定义**颜色主题和排版样式，之后，我们也会推出相关的主题编辑器可供编辑。

### Markdown 语法支持

基于 **Tiptap/ProseMirror** 编辑器内核，支持完整的 CommonMark 和 GitHub Flavored Markdown 语法。

**另外还支持：**

- LaTeX 数学公式（由 KaTeX 渲染）
- Mermaid 图表（将图表渲染为 SVG）
- 脚注的定义引用与跳转
- GitHub 风格警示框，包含备注、提示、警告等，在每种主题下有不同的显示效果。
- <strong>（自定义语法）</strong>图片、表格显示标题。

### 优秀的导出效果

当前可导出为 PDF/HTML/长图片，PDF可自定义纸张大小、页边距、页眉页脚等。也支持禁止表格分页等高级设置。印刷品/LaTeX 等主题导出成 PDF 文件后非常适合于阅读和打印，也可满足部分学术写作的排版要求。

### 极简但完善的操作逻辑与功能

- **工作区管理**：支持打开文件夹作为工作区，按树视图或列表视图查看文件，按名称/内容搜索文档。
- **多窗口与多标签页**：支持打开多个窗口实例，也可将文档在新窗口中打开。另外，应用支持在同一个窗口中打开多个标签页，每个标签页独自管理其文档内容。
- **源码模式**：内置 CodeMirror 6 源码编辑模式，可在可视化编辑和 Markdown 源码之间即时切换。
- **不合规 Markdown 标记自动转换**：针对中文 Markdown 文本常见的**暴露字面星号**的问题，应用能够检测不符合 CommonMark 规范的星号标记并转化为 HTML 标签。
- **菜单与快捷键**：所有的段落与格式操作均可通过上下文菜单与段落格式按钮完成。应用还具有完备的快捷键自定义系统。
- **LaTeX 公式输入辅助**：无需记忆 LaTeX 源码，涵盖大部分数学符号，通过点击即可输入复杂的 LaTeX 公式。
- **专注阅读与写作**：提供专注模式、打字机模式、极简模式，也可进入全屏幕编辑。
- **中西文排版友好**：可在首选项中选择首选的汉字字形规范（简体中文/繁体中文/日文/韩文），同时，**应用会在中文与西文之间自动添加间距，无需手动插入空格。**

## 平台支持


| 平台      | 所用技术                             | 代码目录                    |
| ------- | -------------------------------- | ----------------------- |
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS   | Swift + AppKit + WKWebView       | `apps/macos`            |


两个平台共享同一套编辑器前端与样式，应用则用平台原生方法实现。

## 项目结构

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows 原生应用（C# WinForms）
│   │   ├── MarkLeaf/             #   主程序（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setup 安装器
│   └── macos/                    # macOS 原生应用（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   主程序
│       ├── Changelog/            #   产品更新日志（四语言）
│       └── script/               #   构建 / 发布脚本
├── packages/
│   ├── editor-web/               # 共享编辑器前端（Tiptap/ProseMirror + CodeMirror 6）
│   └── styles/                   # 共享排版 / 主题样式（打印风格，两平台共用）
├── MarkLeaf.slnx                 # Windows 解决方案
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共享应用图标
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 技术架构

```text
apps/windows（C# WinForms）        apps/macos（Swift AppKit）
  主窗口 / 菜单 / 工作区 / 导出       主窗口 / 菜单 / 工作区 / 导出
        │                                  │
        ├── packages/editor-web ───────────┤   共享前端（Tiptap + CodeMirror）
        ├── packages/styles ───────────────┤   共享打印样式
        └── WebView2 / WKWebView ──────────┘   native-shim.js 消息桥
```

## 构建与运行

### Web 前端编辑器

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # 产物输出到 packages/editor-web/dist
pnpm --dir packages/editor-web test        # vitest 前端测试
```

### Windows

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# 一次性（构建前端 + 编译 + 打包 .app + 启动）
./apps/macos/script/build_and_run.sh

# 发布打包（.app / ZIP / 品牌 DMG / 校验和）
./apps/macos/script/release/package.sh
```

## 许可证

应用采用 MIT 许可证。见 [LICENSE](./LICENSE)。



&nbsp;