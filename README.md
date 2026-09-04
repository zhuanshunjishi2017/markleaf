# MarkLeaf

[English](./docs/README.en.md) | [日本語](./docs/README.ja.md) | [繁體中文](./docs/README.zh-TW.md)

这是一个原生轻量化 Markdown 可视化编辑器，追求简洁的界面与排版，提供专注于思考、阅读与写作的空间。

项目最初由 [fcz](https://github.com/zhuanshunjishi2017) 发起并制作，初版仅支持 Windows 平台，后由 [Na Bian](https://github.com/Na-Bian) 提供了 macOS 版本的支持。当前，**Windows 版本与 macOS 版本同步更新。**

## 应用截图

![screenshot-light](./docs/assets/screenshot-light.png)

## 功能介绍

### 丰富的排版样式与配色方案

#### **排版样式**

应用内置丰富的排版样式，例如：

- **网页**：适合屏幕阅读和日常编辑，是大多数编辑器较为主流的 Markdown 渲染风格，追求效率和清晰的体验。
- **印刷品**：采用印刷品常用的衬线字体与黑体排版，段落两端对齐，首行缩进，标题居中，页面留白宽裕，模拟现代书籍排版效果。适合长文写作与阅读。
- **LaTeX**：采用 CMU 字体和类似于 LaTeX 的 document 文档的排版，引用与提示框贴近 tcolorbox 风格，尽可能贴近 LaTeX渲染的风格，适合于论文的写作。
- **铅字印刷**：采用特里王老师制作的汇文、朝华系列字体以及京华老宋体，在印刷品布局基础上营造更为复古的样式。

#### 配色方案

应用支持**多种颜色主题**，包含深色与浅色，每种颜色主题都有独特的风格。

由于配色方案与渲染主题都是 CSS 样式，故可以**完全自定义**颜色主题和排版样式。所有风格均支持导出为 **PDF/HTML/图片**，可自定义纸张大小、页边距和页眉页脚。

### Markdown 语法支持

基于 **Tiptap/ProseMirror** 编辑器内核，支持完整的 CommonMark 和 GitHub Flavored Markdown 语法。

**另外还支持**

- LaTeX 数学公式（由 KaTeX 提供渲染支持）
- Mermaid 图表（将图表渲染为 SVG）
- 脚注的定义引用与跳转
- GitHub 风格警示框，包含备注、提示、警告等，在每种主题下有不同的显示效果。
- <strong>（自定义语法）</strong>图片、表格显示标题。

### 极简但完善的操作逻辑

- **工作区管理**：支持打开文件夹作为工作区，按树视图或列表视图查看文件，按名称/内容搜索文档。
- **多窗口与多标签页**：支持打开多个窗口实例，也可将文档在新窗口中打开。另外，应用支持在同一个窗口中打开多个标签页，每个标签页独自管理其文档内容。
- **源码模式**：内置 CodeMirror 6 源码编辑模式，可在可视化编辑和 Markdown 源码之间即时切换。
- **菜单与快捷键**：所有的段落与格式操作均可通过上下文菜单与段落格式按钮完成。应用还具有完备的快捷键自定义系统。
- **专注阅读与写作**：提供专注模式、打字机模式、极简模式，也可进入全屏幕编辑。

## 平台支持


| 平台      | 技术栈                              | 代码目录                    |
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

本应用采用 MIT 许可证。

## 其他说明

### 字体

部分主题可能需要用到特定的字体，您可以前往以下页面下载。

- [Computer Modern 系列字体](https://www.fontsquirrel.com/fonts/computer-modern)（LaTeX 默认排版字体）
- [汇文、朝华系列字体以及京华老宋体](https://huozi.cool/) （适用于铅字印刷排版，由特里王制作）
- [霞鹜文楷](https://github.com/lxgw/LxgwWenKai) （适用于手记排版，由 Lxgw 制作的优秀开源字体）

