# MarkLeaf

[简体中文](../README.md) | [English](./README.en.md) | [日本語](./README.ja.md)

MarkLeaf 是輕量化 Markdown 視覺化編輯器，提供 Windows/macOS 原生應用程式和 VS Code 擴充功能，追求簡潔的介面與排版，為思考、閱讀與寫作提供專注的空間。

專案最初由 [fcz](https://github.com/zhuanshunjishi2017) 發起並製作，初版僅支援 Windows 平台，後由 [Na Bian](https://github.com/Na-Bian) 提供了 macOS 版本的支援。**當前，Windows 版本與 macOS 版本共同更新。**

## 應用截圖

![screenshot-light](./assets/screenshot-light.png)

## 功能介紹

### 豐富的排版樣式與配色方案

#### **排版樣式**

應用內建豐富的排版樣式，例如：

- **網頁**：適合螢幕閱讀和日常編輯，是多數編輯器較為主流的 Markdown 渲染風格，追求效率和清晰的體驗。**(上圖中左上視窗所用排版)**
- **印刷品**：採用印刷品常用的襯線字體與黑體排版，段落兩端對齊，首行縮排，標題置中，頁面留白寬裕，模擬現代書籍排版效果。適合長文寫作與閱讀。
- **LaTeX**：採用 CMU 字體和類似於 LaTeX 的 document 文件的排版，引用與提示框採用 tcolorbox 風格，盡可能貼近 LaTeX 渲染的風格。**（上圖中部視窗所用排版）**
- **鉛字印刷**：採用特里王老師製作的匯文、朝華系列字體以及京華老宋體，在印刷品佈局基礎上營造更為復古的樣式。**（上圖右上視窗所用排版）**

> [!NOTE]
> 部分主題可能需要用到特定的字體以獲得更佳體驗，您可以前往以下頁面，或直接從 [Release](https://github.com/zhuanshunjishi2017/markleaf/releases) 中下載相關字體包並將其安裝到電腦上。
>
> - [Computer Modern 系列字體](https://www.fontsquirrel.com/fonts/computer-modern)（LaTeX 預設排版字體）
> - [匯文、朝華系列字體以及京華老宋體](https://huozi.cool/) （鉛字印刷排版，由特里王製作的免費字體）
> - [霞鶩文楷](https://github.com/lxgw/LxgwWenKai) （由 Lxgw 製作的優秀開源開源中文字體）

#### 配色方案

應用支援**多種顏色主題**，包含深色與淺色，<strong>實現了 Win32 選單對深色模式的支援。</strong>以下是部分預置的顏色主題效果。

> [!TIP]
> 由於配色方案與渲染主題**都是 CSS 樣式**，故您可以**完全自訂**顏色主題和排版樣式，之後，我們也會推出相關的主題編輯器可供編輯。

### Markdown 語法支援

基於 **Tiptap/ProseMirror** 編輯器核心，支援完整的 CommonMark 和 GitHub Flavored Markdown 語法。

**另外還支援：**

- LaTeX 數學公式（由 KaTeX 渲染）
- Mermaid 圖表（將圖表渲染為 SVG）
- 註腳的定義參照與跳轉
- GitHub 風格警示框，包含備註、提示、警告等，在每種主題下有不同的顯示效果。
- <strong>（自訂語法）</strong>圖片、表格顯示標題。

### 優異的匯出效果

Windows 原生版支援 PDF、HTML、PNG/JPG 長圖和列印；macOS 原生版支援 PDF、HTML、PNG/JPG 長圖和系統列印。VS Code 擴充功能現已接入 PDF、獨立 HTML、PNG/JPG 長圖、預覽和瀏覽器列印，預設採用 MarkLeaf 極簡排版。PDF 可設定紙張、方向、邊界、頁首頁尾和頁碼；長圖可依高度連續分片。除了 HTML 檔案匯出，擴充功能使用 `puppeteer-core` 呼叫已安裝的 Chrome/Edge，不附帶或下載瀏覽器。

### 極簡但完善的操作邏輯與功能

下面的工作區、多視窗和內建原始碼模式介紹以原生應用程式為主。VS Code 擴充功能使用 VS Code 的檔案總管、視窗、分頁和原始碼編輯器，其功能入口見下方擴充功能說明。

- **工作區管理**：支援開啟資料夾作為工作區，按樹狀檢視或清單檢視檢視檔案，依名稱/內容搜尋文件。檔案變更自動重新整理時保留資料夾展開、選取項目與捲動位置。目前僅列出 `.md`、`.txt` 文字檔與資料夾，不讀取 PDF、圖片或壓縮檔內容。
- **多視窗與多標籤頁**：支援開啟多個視窗實例，也可將文件在新視窗中開啟。另外，應用支援在同一個視窗中開啟多個標籤頁，每個標籤頁獨自管理其文件內容。
- **原始碼模式**：內建 CodeMirror 6 原始碼編輯模式，可在視覺化編輯和 Markdown 原始碼之間即時切換。
- **不合規 Markdown 標記自動轉換**：針對中文 Markdown 文字常見的**暴露字面星號**問題，應用能夠偵測不符合 CommonMark 規範的星號標記並轉化為 HTML 標籤。
- **選單與快速鍵**：所有的段落與格式操作均可透過上下文選單與段落格式按鈕完成。應用還具有完備的快速鍵自訂系統。
- **LaTeX 公式輸入輔助**：無需記憶 LaTeX 原始碼，涵蓋大部分數學符號，透過點擊即可輸入複雜的 LaTeX 公式。
- **專注閱讀與寫作**：提供專注模式、打字機模式、極簡模式，也可進入全螢幕編輯。
- **中西文排版友好**：可在偏好設定中選擇偏好的漢字字形規範（簡體中文/繁體中文/日文/韓文），同時，**應用會在中西文之間自動增加間距，無需手動插入空格。**

## 平台支援

| 平台 | 所用技術 | 程式碼目錄 |
| --- | --- | --- |
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS | Swift + AppKit + WKWebView | `apps/macos` |
| VS Code 擴充功能 | TypeScript + CustomTextEditorProvider + Webview | `apps/vscode` |

三個宿主共享編輯核心與排版樣式。VS Code 擴充功能使用現有的 VS Code 執行環境，不引入獨立 Electron 相依套件或桌面殼層。支援閱讀與視覺化編輯、格式刷、表格、註腳、公式與 Mermaid、圖片貼上和拖放、尋找取代、大綱及排版偏好。儲存、復原重做、分頁和 Markdown 原始碼由 VS Code 管理，支援原始碼切換及並排。

VS Code 擴充功能 0.2.7 提供 36 項設定和 67 項可設定快速鍵的格式操作；公式與圖表原始碼展開在對應內容下方，隨文件捲動。快速鍵設定僅作用於 VS Code 中的 MarkLeaf，不改變原生應用程式的鍵位。專案與擴充功能說明均提供簡體中文、英文、日文和繁體中文；擴充功能介面尚未全部在地化，詳細入口與範圍見 [擴充功能說明](../apps/vscode/docs/README.zh-TW.md) 和 [功能對應說明（簡體中文）](../apps/vscode/docs/feature-parity.md)。

三個產品的複製與貼上以 Windows 邏輯為準：「複製 HTML」取得原始碼文字；視覺編輯中的一般文字貼上和「貼上純文字」均解析 Markdown，原始碼編輯保留字面文字。貼上提示區分成功、格式轉換、純文字降級及失敗，並保留降級原因。

正文渲染同樣預設使用 `minimal`（網頁·極簡），保留字型層級、留白和表格細節；已儲存的使用者或工作區排版選擇優先。

在 VS Code 1.120+ 中，Markdown Git 比較預設使用原生原始碼 diff，顯示新增／刪除醒目提示；一般檔案仍預設使用 MarkLeaf。

## 專案結構

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows 原生應用（C# WinForms）
│   │   ├── MarkLeaf/             #   主程式（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setup 安裝器
│   ├── vscode/                   # VS Code Markdown 閱讀與編輯擴充功能（TypeScript）
│   └── macos/                    # macOS 原生應用（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   主程式
│       ├── Changelog/            #   產品更新日誌（四語言）
│       └── script/               #   建置 / 發布腳本
├── packages/
│   ├── editor-web/               # 共享編輯器前端（Tiptap/ProseMirror + CodeMirror 6）
│   └── styles/                   # 共享排版 / 主題樣式（三個宿主共用）
├── MarkLeaf.slnx                 # Windows 解決方案
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共享應用程式圖示
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 技術架構

```text
packages/editor-web（共享編輯核心）+ packages/styles（共享排版）
├── apps/windows → WinForms + WebView2 → 原生訊息橋
├── apps/macos   → AppKit + WKWebView  → 原生訊息橋
└── apps/vscode  → VS Code Webview    → TextDocument / WorkspaceEdit

Windows/macOS：main.ts，內建 CodeMirror 6 原始碼模式
VS Code：vscode.ts，使用 VS Code 原生 Markdown 原始碼編輯器
```

## 建置與執行

### VS Code 擴充功能

從儲存庫根目錄執行，Node.js 22.12+，使用專案指定的 pnpm：

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir apps/vscode install --frozen-lockfile
pnpm package:vscode                # artifacts/markleaf-vscode-0.2.7.vsix
```

在 VS Code 中使用 **Install from VSIX…** 安裝產生的擴充套件。新開啟的 `.md`、`.markdown` 檔案預設進入 MarkLeaf；既有原始碼分頁使用 **Reopen Editor With… → MarkLeaf**，既有預設關聯使用 **Configure default editor for…** 調整。**Ctrl+Shift+V**（macOS 為 **Cmd+Shift+V**）在原始碼與渲染檢視間切換。

升級後先儲存文件，再執行 **Developer: Reload Window**。若設定項目缺失或提示 `markleaf.shortcuts` 未註冊，需要重新載入整個視窗。透過 **视图 → 快捷键…**（檢視 → 快速鍵）錄入格式鍵位。閱讀不會回寫；視覺化編輯可能正規化 Markdown 格式，詳見 [擴充功能使用與保真範圍](../apps/vscode/docs/README.zh-TW.md)。

### Web 前端編輯器

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # 產出輸出到 packages/editor-web/dist
pnpm --dir packages/editor-web test        # vitest 前端測試
```

### Windows

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# 一次性（建置前端 + 編譯 + 打包 .app + 啟動）
./apps/macos/script/build_and_run.sh

# 發布打包（.app / ZIP / 品牌 DMG / 校驗和）
./apps/macos/script/release/package.sh
```

## 授權條款

應用採用 MIT 授權條款。見 [LICENSE](../LICENSE)。
