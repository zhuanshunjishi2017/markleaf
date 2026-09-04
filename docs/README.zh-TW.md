# MarkLeaf

[简体中文](../README.md) | [English](./README.en.md) | [日本語](./README.ja.md)

這是一個原生、輕量化的 Markdown 視覺化編輯器，追求簡潔的介面與排版，提供專注於思考、閱讀與寫作的空間。

本專案最初由 [fcz](https://github.com/zhuanshunjishi2017) 發起並製作，初版僅支援 Windows，後由 [Na Bian](https://github.com/Na-Bian) 提供 macOS 版本支援。目前，**Windows 版本與 macOS 版本同步更新**。

## 應用程式截圖

![screenshot-light](./assets/screenshot-light.png)

## 功能介紹

### 豐富的排版樣式與配色方案

#### **排版樣式**

應用程式內建豐富的排版樣式，例如：

- **網頁**：適合螢幕閱讀與日常編輯，是大多數編輯器較為主流的 Markdown 渲染風格，追求效率與清晰的體驗。
- **印刷品**：採用印刷品常用的襯線字體與黑體排版，段落兩端對齊、首行縮排、標題置中、頁面留白寬裕，模擬現代書籍排版效果。適合長文寫作與閱讀。
- **LaTeX**：採用 CMU 字體與類似 LaTeX document 文件的排版，引用與提示框貼近 tcolorbox 風格，盡可能貼近 LaTeX 渲染的風格，適合論文寫作。
- **鉛字印刷**：採用特里王老師製作的匯文、朝華系列字體以及京華老宋體，在印刷品版面基礎上營造更為復古的樣式。

#### 配色方案

應用程式支援**多種顏色主題**，包含深色與淺色，每種顏色主題都有獨特的風格。

由於配色方案與渲染主題都是 CSS 樣式，因此可以完全自訂顏色主題與排版樣式。所有風格均支援匯出為 PDF/HTML/圖片，可自訂紙張大小、頁邊距與頁眉頁腳。

### Markdown 語法支援

基於 **Tiptap/ProseMirror** 編輯器核心，支援完整的 CommonMark 與 GitHub Flavored Markdown 語法。

**另外還支援**

- LaTeX 數學公式（由 KaTeX 提供渲染支援）
- Mermaid 圖表（將圖表渲染為 SVG）
- 腳註的定義、引用與跳轉
- GitHub 風格警示框，包含備註、提示、警告等，在每種主題下有不同的顯示效果
- <strong>（自訂語法）</strong>圖片、表格顯示標題

### 極簡但完善的操作邏輯

- **工作區管理**：支援開啟資料夾作為工作區，按樹狀檢視或清單檢視查看檔案，按名稱或內容搜尋文件。
- **多視窗與多分頁**：支援開啟多個視窗實例，也可將文件在新視窗中開啟。此外，應用程式支援在同一個視窗中開啟多個分頁，每個分頁獨立管理其文件內容。
- **原始碼模式**：內建 CodeMirror 6 原始碼編輯模式，可在視覺化編輯與 Markdown 原始碼之間即時切換。
- **選單與快捷鍵**：所有段落與格式操作均可透過內容選單與段落格式按鈕完成。應用程式還具有完整的快捷鍵自訂系統。
- **專注閱讀與寫作**：提供專注模式、打字機模式、極簡模式，也可進入全螢幕編輯。

## 平台支援


| 平台      | 技術棧                              | 程式碼目錄                   |
| ------- | -------------------------------- | ----------------------- |
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS   | Swift + AppKit + WKWebView       | `apps/macos`            |


兩個平台共用同一套編輯器前端與樣式，應用程式則使用平台原生方法實作。

## 專案結構

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows 原生應用程式（C# WinForms）
│   │   ├── MarkLeaf/             #   主程式（.NET 10 + WebView2）
│   │   └── setup/                #   Inno Setup 安裝程式
│   └── macos/                    # macOS 原生應用程式（Swift AppKit）
│       ├── Sources/MarkLeaf/     #   主程式
│       ├── Changelog/            #   產品更新日誌（四種語言）
│       └── script/               #   建置 / 發佈腳本
├── packages/
│   ├── editor-web/               # 共用編輯器前端（Tiptap/ProseMirror + CodeMirror 6）
│   └── styles/                   # 共用排版 / 主題樣式（兩個平台共用列印樣式）
├── MarkLeaf.slnx                 # Windows 解決方案
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # 共用應用程式圖示
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## 技術架構

```text
apps/windows（C# WinForms）        apps/macos（Swift AppKit）
  主視窗 / 選單 / 工作區 / 匯出       主視窗 / 工作區 / 匯出
        │                                  │
        ├── packages/editor-web ───────────┤   共用前端（Tiptap + CodeMirror）
        ├── packages/styles ───────────────┤   共用列印樣式
        └── WebView2 / WKWebView       ─────┘   native-shim.js 訊息橋接
```

## 建置與執行

### Web 前端編輯器

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # 產物輸出至 packages/editor-web/dist
pnpm --dir packages/editor-web test        # Vitest 前端測試
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

# 發佈打包（.app / ZIP / 品牌 DMG / 校驗和）
./apps/macos/script/release/package.sh
```

## 授權條款

本應用程式採用 MIT 授權條款。

## 其他說明

### 字體

部分主題可能需要使用特定字體，您可以前往以下頁面下載。

- [Computer Modern 系列字體](https://www.fontsquirrel.com/fonts/computer-modern)（LaTeX 預設排版字體）
- [匯文、朝華系列字體以及京華老宋體](https://huozi.cool/)（適用於鉛字印刷排版，由特里王製作）
- [霞鶩文楷](https://github.com/lxgw/LxgwWenKai)（適用於手記排版，由 Lxgw 製作的優秀開源字體）

