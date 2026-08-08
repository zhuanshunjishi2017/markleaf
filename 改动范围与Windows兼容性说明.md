# 改动范围与 Windows 兼容性说明

> 更新日期：2026-08-07

## 一句话总结

**本仓库近期的所有修改均以 macOS 移植版为目标并只在 macOS 上完成了构建与验证。**
共享前端（`src/EditorWeb` 与 `src/MarkLeaf/Resources/Styles`）的改动会**同时影响 Windows 版**，
但 **Windows 端没有重新构建、没有回归测试**，可能仍然存在 bug（包括这些共享改动引入的回归，
以及 Windows 原生代码自身的历史遗留问题）。

---

## 1. 改动范围

### 1.1 macOS 移植版（`macos/` 目录，全新）

这是本次工作的主体：基于 AppKit + WKWebView 的 macOS 原生应用，对应 Windows 版功能做了移植与大量修复，主要包括：

- 菜单栏（文件/编辑/段落/格式/视图/外观/帮助，对齐 Windows 结构）
- 首选项（5 个标签页：文件/编辑器/外观/通用/图片，LyricsX 式样式）
- 工作区 / 大纲侧边栏（毛玻璃、source-list、FSEvents 自动刷新、递归文档列表）
- 导出（PDF 按纸张分页、HTML）、剪贴板、快捷键窗口、原生“关于”面板
- 恢复未保存的文档（快照 + 恢复对话框，窗口居中）
- 源码模式选区主题化、行号栏主题化、缩放（⌘+滚轮/触控板捏合）
- 文件关联（绑定默认打开程序）、用户主题目录（`~/Library/Application Support/MarkLeaf/Themes`）
- 大量 UI 细节（占位文案、按钮标红、文案统一等）

### 1.2 共享前端（`src/EditorWeb` + `src/MarkLeaf/Resources/Styles`，6 个提交）

这些改动两边共用，**Windows 端未验证**：

| 提交 | 内容 |
| --- | --- |
| `a95782d` | 源码模式行号栏/编辑器背景跟随主题（`.cm-gutters` 等） |
| `86fa359` | WYSIWYG 选区主题化（ProseMirror 装饰替代 contenteditable `::selection`） |
| `9e5a9b0` | 在线图片显示（`toVirtualImageUrl` 跳过 http/https，CSP 允许 https 图片） |
| `5e12321` | 提升/降低标题级别时对列表/引用的处理（先移出块结构再设标题） |
| `663f2f6` | 源码模式选区改用真实 DOM 装饰 span（替代 CodeMirror `drawSelection` 图层） |
| `c478f8b` | 源码模式缩进宽度接入前端（`indentUnit`/`tabSize`），新增 `setSourceSelection` 命令 |

### 1.3 Windows 原生端（`src/MarkLeaf` 下的 `*.cs`）

**未做任何修改、未构建、未测试。**

---

## 2. Windows 版潜在风险点（建议回归）

以下共享改动在 macOS（WebKit）上已修复并验证，但在 Windows（WebView2 / Chromium）上**未经测试**，
可能存在行为差异或回归，建议 Windows 端单独构建（`build-markleaf.bat`）后重点回归：

1. **源码模式选区**（`663f2f6`）
   - macOS 上 WKWebView 忽略 contenteditable `::selection`，且 `drawSelection()` 的绝对定位图层坐标不稳，
     因此改成了真实 DOM 装饰 span（`.ml-source-selection`）+ 隐藏原生选区/光标。
   - Chromium/WebView2 对 `::selection` 和 `drawSelection` 的支持与 WebKit 不同，**选区渲染行为需要重新验证**。

2. **WYSIWYG 选区主题化**（`86fa359`）
   - 用 ProseMirror 装饰绘制主题化选中背景。Chromium 上 `::selection` 是生效的，
     装饰方案可能与原生 `::selection` 叠加，需要确认无双重高亮或颜色冲突。

3. **源码模式行号栏 / 背景主题化**（`a95782d`）
   - 依赖主题 CSS 变量（`--bg-primary` 等），两端同源，风险较低，但仍需确认。

4. **源码模式缩进宽度 / 新命令**（`c478f8b`）
   - 前端新增 `setSourceIndent`、`setSourceSelection` 命令处理。**Windows 宿主（`EditorHostController`）未下发这些命令**，
     缩进宽度设置可能不生效（前端默认 2 空格兜底，不影响基本使用）。

5. **在线图片**（`9e5a9b0`）与**标题升降**（`5e12321`）
   - 纯前端逻辑，两端应一致，但未在 Windows 上回归。

6. **滚动条样式**
   - macOS 端在 `StyleManager` 中注入的是 WebKit 的 `scrollbar-color`；Windows 端使用 Chromium 的
     `::-webkit-scrollbar`，两套样式并不互通，需要各自维护。

7. **Windows 原生代码**（`*.cs`）本身未改动，可能存在与本次改动无关的**历史遗留 bug**，
   需要 Windows 端自行构建与排查。

---

## 2.5 本轮新增的共享前端改动（macOS 侧实现，Windows 需回归）

在并入上游 `efd817e` 之后，macOS 侧又对共享前端做了两处改动，**Windows 也会受影响**：

1. **源码模式语法高亮改为主题感知**
   - 上游用 `defaultHighlightStyle`（style-mod 生成非主题硬编码色，深色主题下看不清，且 `.tok-*` CSS 不生效）。
   - macOS 侧改为 class-based `HighlightStyle`（`@lezer/highlight` 的 `tags`），产出 `.tok-*` 类，颜色由 `styles.css` 的主题变量控制。
   - **影响**：前端新增 `@lezer/highlight` 直接依赖（需 `pnpm install`）；`.tok-*` CSS 从 4 条扩展为 16 条。
   - Windows 回归点：源码模式语法高亮颜色、深色主题可读性。

2. **导出配色方案（`colorSchemeCss`）**
   - macOS 导出对话框新增「配色方案」下拉，把所选主题 CSS 作为 `colorSchemeCss` 传给前端 `generateExportHtml`（该参数上游已实现）。
   - **影响**：Windows 的 `ExportDialog` 已实现同样功能，无需改动；仅确认前端 `generateExportHtml` 注入顺序（baseCss → colorSchemeCss → 排版样式）一致。

---

## 3. 建议

- Windows 端：拉取本分支后执行 `build-markleaf.bat`，按第 2 节清单逐项回归（重点：源码模式选区、WYSIWYG 选区、源码模式行号栏与缩进）。
- 共享前端的修复对两端都有益，但**发布前请先在 Windows 上完整验证一遍**。
- 若 Windows 端发现由共享改动引入的问题，建议在共享前端做兼容处理（例如按运行环境选择选区方案），
  而不是只在 Windows 端打补丁，避免两端行为再次分叉。
