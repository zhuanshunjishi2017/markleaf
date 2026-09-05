# MarkLeaf

[简体中文](../README.md) | [English](./README.en.md) | [日本語](./README.ja.md)

這是一個原生輕量化 Markdown 視覺化編輯器，追求簡潔的介面與排版，提供專注於思考、閱讀與寫作的空間。

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

當前可匯出為 PDF/HTML/長圖片，PDF可自訂紙張大小、邊界、頁首頁尾等。也支援禁止表格分頁等進階設定。印刷品/LaTeX 等主題匯出成 PDF 文件後非常適合於閱讀和列印，也可滿足部分學術寫作的排版要求。

### 極簡但完善的操作邏輯與功能

- **工作區管理**：支援開啟資料夾作為工作區，按樹狀檢視或清單檢視檢視檔案，依名稱/內容搜尋文件。
- **多視窗與多標籤頁**：支援開啟多個視窗實例，也可將文件在新視窗中開啟。另外，應用支援在同一個視窗中開啟多個標籤頁，每個標籤頁獨自管理其文件內容。
- **原始碼模式**：內建 CodeMirror 6 原始碼編輯模式，可在視覺化編輯和 Markdown 原始碼之間即時切換。
- **不合規 Markdown 標記自動轉換**：針對中文 Markdown 文字常見的**暴露字面星號**問題，應用能夠偵測不符合 CommonMark 規範的星號標記並轉化為 HTML 標籤。
- **選單與快速鍵**：所有的段落與格式操作均可透過上下文選單與段落格式按鈕完成。應用還具有完備的快速鍵自訂系統。
- **LaTeX 公式輸入輔助**：無需記憶 LaTeX 原始碼，涵蓋大部分數學符號，透過點擊即可輸入複雜的 LaTeX 公式。
- **專注閱讀與寫作**：提供專注模式、打字機模式、極簡模式，也可進入全螢幕編輯。
- **中西文排版友好**：可在偏好設定中選擇偏好的漢字字形規範（簡體中文/繁體中文/日文/韓文），同時，**應用會在中西文之間自動增加間距，無需手動插入空格。**

## 平台支援


| 平台      | 所用技術                             | 程式碼目錄                   |
| ------- | -------------------------------- | ----------------------- |
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS   | Swift + AppKit + WKWebView       | `apps/macos`            |


兩個平台共享同一套編輯器前端與樣式，應用則用平台原生方法實現。

## 專案結構

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows 原生應用（C# WinForms）
│   │   ├── MarkLeaf/             #   主程式（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setup 安裝器
│   └── macos/                    # macOS 原生應用（Swift AppKit + WKWebView）
│       ├── Sources/MarkLeaf/     #   主程式
│       ├── Changelog/            #   產品更新日誌（四語言）
│       └── script/               #   建置 / 發布腳本
├── packages/
│   ├── editor-web/               # 共享編輯器前端（Tiptap/ProseMirror + CodeMirror 6）
│   └── styles/                   # 共享排版 / 主題樣式（列印樣式，兩平台共用）
├── MarkLeaf.slnx                 # Windows 解決方案
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共享應用程式圖示
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 技術架構

```text
apps/windows（C# WinForms）        apps/macos（Swift AppKit）
  主視窗 / 選單 / 工作區 / 匯出       主視窗 / 選單 / 工作區 / 匯出
        │                                  │
        ├── packages/editor-web ───────────┤   共享前端（Tiptap + CodeMirror）
        ├── packages/styles ───────────────┤   共享列印樣式
        └── WebView2 / WKWebView ──────────┘   native-shim.js 訊息橋
```

## 建置與執行

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

應用採用 MIT 授權條款。見 [LICENSE](./LICENSE)。