# MarkLeaf for VS Code

[简体中文](../README.md) | [English](./README.en.md) | [日本語](./README.ja.md)

在 VS Code 中閱讀和視覺化編輯 Markdown，重用 MarkLeaf 的 Tiptap/ProseMirror 編輯核心、KaTeX、Mermaid 和排版樣式。擴充功能由 TypeScript 編寫，使用 VS Code 提供的 API 與 Webview，沒有獨立 Electron 相依套件或桌面殼層。

目前版本 **0.2.6** 提供 36 項設定和 67 項可設定快速鍵的格式操作，支援格式刷、表格、註腳、公式與圖表、圖片資源、尋找取代、大綱和閱讀偏好。現已支援 PDF、HTML、PNG/JPG 長圖、預覽和列印。

工具列選單會在點擊外部、按 Escape、切換選單或焦點離開擴充功能時收起。公式與 Mermaid 原始碼面板使用適配明暗主題的不透明背景，始終展開在對應內容下方，隨文件捲動移出視野，不會根據可用空間上下跳轉或固定在視窗底部。公式符號面板會根據可用空間調整版面。

專案與擴充功能 README 均提供四種語言；介面翻譯範圍見下方「排版和偏好」。


## 匯出、預覽與列印

開啟文件後使用工具列「匯出…」或命令選擇區中的 **MarkLeaf: 匯出文件 / PDF / HTML / 圖片 / 列印**。閱讀模式也可匯出。「依上次設定匯出」重用上次成功儲存的選項，並重新選擇輸出位置。

- **PDF**：產生文字可選取的分頁檔案，支援 A4/A5/Letter/Legal、橫向、四邊邊界、純文字頁首頁尾和頁碼；可讓表格、標題與下一區塊盡量同頁，過大的表格仍可能跨頁。
- **HTML**：完整獨立文件，包含排版 CSS、預先算繪的公式和 Mermaid、KaTeX 字型及內嵌圖片；產生檔案不需要瀏覽器。Web 超連結保留原始目的地。
- **PNG/JPG**：設定內容寬度、1–3 倍解析度、單張最大輸出高度與 JPEG 品質。長文件輸出為 `名稱-01.png` 等連續分片；覆寫既有分片前會確認。
- **預覽**：獨立 Chrome/Edge 視窗顯示實際產生的 PDF／圖片；HTML 顯示完整文件。關閉視窗返回，也可在 VS Code 進度通知中取消。
- **列印**：Chrome/Edge 開啟列印對話框，使用者選擇印表機並確認列印。可再調整紙張、邊界和背景圖形；對話框關閉不代表印表機已完成工作。重複頁首頁尾使用現代 Chromium 分頁功能，建議使用目前穩定版瀏覽器。

匯出預設使用 `minimal`（網頁·極簡）與淺色配色，可選九種原有排版和十九種配色。正文繼續跟隨 VS Code 明暗主題；匯出設定獨立，不改寫編輯器偏好。沿用已設定的正文字型與中西文間距；自訂 CSS 檔案與編輯器縮放不套用於匯出。

PDF、圖片、預覽與列印使用已安裝的 **Chrome/Edge**，不附帶或下載瀏覽器。自動尋找失敗時可選擇執行檔，或設定 **MarkLeaf: Export Browser Path**（`markleaf.exportBrowserPath`，機器層級）。macOS 範例：`/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`。不連接既有瀏覽器工作階段，每次使用暫存設定檔，完成或取消後釋放。

匯出先等待編輯同步，再取得 VS Code `TextDocument` 的本次快照，不儲存或修改 Markdown。未同步衝突、圖片讀取或公式／圖表解析失敗會停止並保留原始錯誤。依文件 URI 讀取本機、工作區及 HTTP/HTTPS 圖片並內嵌，單張限 16 MiB；網路圖片須可存取。正文字型使用本機字型，僅內嵌 KaTeX 字型。可取消長工作，已寫出的分片會在結果中列出。

遠端擴充宿主可透過其檔案系統和已安裝瀏覽器匯出檔案。互動預覽與列印限定本機桌面 VS Code；SSH/WSL 請先匯出再於本機開啟。Windows/Linux/遠端及實際印表機仍需在對應環境驗收。


## 安裝和開啟

在本機建置得到 `artifacts/markleaf-vscode-0.2.6.vsix` 後，於 VS Code 擴充功能選單選擇 **Install from VSIX…**，或從儲存庫根目錄執行：

```bash
code --install-extension artifacts/markleaf-vscode-0.2.6.vsix
```

安裝並啟用後，新開啟的 `.md` 或 `.markdown` 檔案預設進入 MarkLeaf 渲染檢視，可直接閱讀與視覺化編輯。已開啟的原始碼分頁可透過 **Reopen Editor With… → MarkLeaf** 或 **Ctrl/Cmd+Shift+V** 切換，也可從檔案總管右鍵選擇 **MarkLeaf: Open Markdown**。

升級 VSIX 後請先儲存文件，再執行 **Developer: Reload Window**，讓目前視窗重新載入擴充功能及其設定宣告。如果設定頁仍只有舊選項，或提示「沒有註冊設定 markleaf.shortcuts」，也請重新載入整個視窗後再錄入鍵位；僅重新啟動擴充功能宿主可能留下舊的設定註冊狀態。

MarkLeaf 使用 VS Code 的預設自訂編輯器宣告。若已為 Markdown 指定其他預設編輯器，或安裝多個預設 Markdown 編輯器，可在 **Reopen Editor With… → Configure default editor for…** 選擇 **MarkLeaf**。要恢復預設原始碼開啟方式時，在同一位置選擇 **Text Editor**；擴充功能遵循既有的 VS Code 編輯器關聯設定。

### Git 差異檢視

在 VS Code 1.120 及以上版本，`.md` 和 `.markdown` 的 Git 工作目錄、暫存區與提交歷史比較，預設使用 VS Code 原生原始碼 diff，顯示行號、新增／刪除醒目提示及差異導覽。一般檔案仍預設使用 MarkLeaf 閱讀與編輯。

擴充功能透過 `workbench.diffEditorAssociations` 提供預設值，不寫入使用者設定；既有使用者或工作區的 diff 關聯優先。若曾為 Markdown 明確指定其他 diff 編輯器，可將該設定中的 `*.md`、`*.markdown` 設為 `default`。升級後先執行 **Developer: Reload Window**，再關閉並重新開啟既有比較分頁。較舊的 VS Code 不支援這項自動關聯，可在比較分頁使用 **Reopen Editor With… → Text Editor**，或升級 VS Code。

## 編輯和閱讀

| 功能群組 | 入口和行為 |
| --- | --- |
| 文字與段落 | 工具列和「格式…」支援粗體、斜體、底線、刪除線、醒目提示、行內程式碼、清除格式、H1–H6、標題升降級、段落前後插入、複製和刪除、清單縮排，以及五種 GitHub 提示框。選單支援依操作名稱搜尋。 |
| 格式刷與段落操作柄 | 選取已有格式的文字，點擊格式刷，再拖曳選取目標文字；套用一次後退出，Escape 取消。段落左側操作柄開啟目前段落的選單，操作柄位於可編輯內容外。 |
| 表格 | 指定行列數插入、新增刪除行列、欄對齊、設定或清除表格標題、刪除表格。選單依游標位置顯示適用操作。 |
| 公式與 Mermaid | 插入行內／獨立公式、公式編號、轉換類型、刪除；雙擊公式與圖表可開啟共享原始碼控制項，保留數學輸入輔助。支援 Mermaid 程式碼渲染、編輯和重新渲染。 |
| 註腳與中繼資料 | 插入註腳、重新命名標籤、回到參照、清除參照、刪除定義；顯示或插入 YAML Front Matter。標籤不能重複占用既有定義或參照。 |
| 程式碼 | 選擇程式碼區塊語言、複製程式碼、退出區塊，支援語法醒目提示開關。 |
| 剪貼簿 | 「编辑」（編輯）提供複製為 Markdown、純文字或 HTML，以及貼上純文字。一般複製同時提供選取文字和 HTML；純文字貼上保留字面的 Markdown/HTML 標記。 |
| 尋找取代 | 在渲染內容中尋找，支援大小寫、完整單字、上一處／下一處、單次和全部取代。閱讀模式可尋找，取代需要編輯模式。 |
| 大綱與閱讀 | 「视图」（檢視）提供 H1–H6 大綱、專注目前段落、打字機捲動、縮放、排版與配色。狀態列顯示字元數、選取字元數、目前區塊及位置，懸停可查看詳細統計。 |

**阅读 / 编辑**（閱讀／編輯）按鈕切換模式。開啟文件、尋找、切換模式或顯示設定不會觸發 Markdown 回寫。閱讀模式保留複製、尋找、連結和大綱導覽，需要改動文件的操作僅在編輯模式可用。

開啟格式選單、圖片選擇器或輸入框後，操作仍套用到開啟時的選取範圍。如果等待期間文件已修改，會提示重新選取。若圖片已經儲存，但原選取範圍失效，提示中會保留資源路徑，方便從新位置插入。

連結在閱讀模式可直接點擊，編輯模式按住 **Ctrl**（macOS 為 **Cmd**）點擊，支援網頁、本機檔案和文件內標題定位。

## 圖片資源

- 工具列「图片」（圖片）可一次選取多張檔案；「编辑 → 图片地址」插入相對路徑或網路 URL。支援 PNG、JPEG、GIF、WebP、SVG、BMP 和 AVIF。
- 直接貼上或拖入圖片時，將圖片儲存到文件旁的 `assets` 目錄，再插入 Markdown 參照。每張圖片最多 16 MiB；未儲存文件以第一個工作區資料夾為基準，沒有工作區時須先儲存文件。
- 檔案選擇預設複製到資源目錄，可用 `markleaf.fileImageHandling` 改為參照原檔案。資源目錄、相對路徑及 `./` 前綴均可設定，檔名會自動避免覆寫既有資源。
- 選取圖片後透過「编辑 → 当前图片操作」取代圖片、編輯標題、旋轉、調整寬度或另存圖片；閱讀模式僅提供另存圖片。
- 圖片顯示使用 VS Code 資源位址，寫回 Markdown 的仍是原始路徑。參照工作區外圖片時，按圖片所在目錄載入資源；遠端檔案透過 VS Code 檔案系統 API 處理。

## 快速鍵與文件同步

| 操作 | Windows / Linux | macOS |
| --- | --- | --- |
| 原始碼／渲染雙向切換 | `Ctrl+Shift+V` | `Cmd+Shift+V` |
| 渲染內容尋找 | `Ctrl+F` | `Cmd+F` |
| 渲染內容取代 | `Ctrl+H` | `Cmd+Alt+F` |
| 貼上純文字 | `Ctrl+Alt+V` | `Cmd+Alt+V` |
| 儲存 | `Ctrl+S` | `Cmd+S` |
| 復原／重做 | `Ctrl+Z` / `Ctrl+Shift+Z` | `Cmd+Z` / `Cmd+Shift+Z` |

尋找列用 Enter / Shift+Enter 定位下一處／上一處，Escape 關閉。Ctrl+滾輪可調整文件縮放。在 VS Code **Keyboard Shortcuts** 搜尋 MarkLeaf，可自訂原始碼切換、尋找、取代和純文字貼上。兩條檢視切換規則分別作用於原始碼與渲染，改鍵時應一起調整。渲染快速鍵僅在 MarkLeaf 取得焦點時生效，純文字貼上不接管尋找框和公式原始碼等輸入框。標題、粗體、斜體等預設沿用共享編輯器既有鍵位，可在 MarkLeaf 快速鍵設定中變更。原始碼切換在 Markdown 編輯情境接管原生預覽快速鍵，原生預覽命令仍可透過命令選擇區使用。

### 格式快速鍵設定

開啟 **视图 → 快捷键…**（檢視 → 快速鍵），或執行命令 **MarkLeaf: Configure Shortcuts / 快捷键设置**，也可從排版、主題與設定入口進入。錄鍵面板可搜尋所有格式操作、錄入、清除或恢復預設；點擊「保存键位」（儲存鍵位）後立即生效，格式選單與工具列提示同步更新。

行內公式、獨立公式及 Mermaid 預設未綁定。為插入公式操作設定鍵位後，在渲染編輯區的游標位置按鍵即可插入，並開啟既有公式原始碼與符號輔助。表格尺寸、註腳等需要額外參數的操作仍使用既有輸入框。

VS Code 設定頁中的 **Markleaf: Shortcuts** 儲存同一份設定，也可直接編輯設定 JSON，例如：

```json
"markleaf.shortcuts": {
  "insertMathInline": "Mod+Alt+M",
  "insertMathBlock": "Mod+Alt+Shift+M",
  "toggleBold": "Mod+Alt+B",
  "toggleHighlight": ""
}
```

- `Mod` 在 macOS 表示 Cmd，在 Windows/Linux 表示 Ctrl；也支援明確指定 `Ctrl`、`Cmd`、`Alt`、`Shift`。英文字母和數字使用鍵盤的實體鍵位，避免 macOS Option 產生符號影響比對。
- 支援單組組合鍵（A–Z、0–9 或 F1–F24），目前不支援連續組合鍵或標點鍵。一般字母／數字須搭配 Ctrl、Cmd 或 Alt，避免占用正常輸入。
- 未設定的操作沿用預設鍵位；空字串取消綁定。變更或取消後，原鍵位在 MarkLeaf 渲染編輯區不再觸發該操作。「使用默认」（使用預設）仍須再點擊儲存，不修改其他操作。
- 與其他 MarkLeaf 操作重複，或使用儲存／復原／原始碼切換等保留鍵位時，面板會說明原因並阻止儲存。手動 JSON 中的無效或重複項目會在面板標明，相關快速鍵停用，不暗中退回舊鍵位。
- 儲存範圍顯示在面板頂部：既有資料夾層級設定優先，其次工作區，否則儲存為使用者設定；不將繼承的其他鍵位複製到目前範圍。修改設定不會回寫 Markdown 文件。
- 自訂格式鍵僅作用於目前 MarkLeaf 渲染編輯區；閱讀模式、尋找框、公式／圖表原始碼輸入框與原生 Markdown 原始碼編輯器不觸發這些操作。輸入法組字及 AltGraph 輸入保持原有行為。

全部 67 項格式操作也註冊為獨立 VS Code 命令，例如 `markleaf.insertMathInline`、`markleaf.insertMathBlock`、`markleaf.setHeading1`。若需連續組合鍵等進階規則，可在 VS Code Keyboard Shortcuts 設定，並將條件限定為 `activeWebviewPanelId == markleaf.editor && markleaf.focus == document`。同一操作應在 MarkLeaf 設定中清除舊鍵，以免保留兩套入口。

MarkLeaf 選單僅顯示 `markleaf.shortcuts` 的有效設定。VS Code 公開擴充 API 不提供解析後的全域鍵位查詢，因此無法同步顯示另外在 `keybindings.json` 設定的覆寫規則，也無法檢查其他擴充功能和系統攔截的全部組合鍵；這類衝突請使用 VS Code Keyboard Shortcuts 檢查。

VS Code 的 `TextDocument` 是文件的唯一事實來源，負責儲存、未儲存標記和編輯歷程。「源码 / 并排」（原始碼／並排）開啟原生 Markdown 編輯器，共用同一份文件。連續輸入依同步批次復原，輸入法組字在確認後同步；底部「已同步到 VS Code」表示編輯已進入文字緩衝區，是否寫入磁碟以 VS Code 未儲存標記為準。

Webview 一次只送出一項編輯，收到版本確認後再送出期間累積的新內容，自身編輯確認不會重建編輯器。如果其他檢視同時修改檔案，過期編輯不會覆寫新版本，未同步內容保留在 MarkLeaf，並提供「将未同步内容打开为草稿」（將未同步內容開啟為草稿）。VS Code 開啟草稿後，MarkLeaf 重新載入檔案目前內容；請在原始碼中合併草稿。

每個檔案允許一個 MarkLeaf 視覺化檢視，可與原生原始碼並排。隱藏檢視保留內容以保護未完成輸入。日常儲存會等待文件同步；VS Code 結束時可能略過儲存參與者，因此關閉視窗前應確認同步完成，衝突內容應先開啟為草稿。

## 排版和偏好

透過「视图」（檢視）的排版與設定入口選擇常用選項，或在 VS Code 設定搜尋 `@ext:markleaf.markleaf`。設定遵循既有的使用者、工作區或工作區資料夾範圍。

| 設定群組 | 主要設定 |
| --- | --- |
| 排版與配色 | `typography` 提供九種原有樣式：sans、serif、print、print-double、latex、retro-print、minimal、magazine、notebook；`colorTheme` 預設跟隨 VS Code，也可選擇十九種內建配色。正文渲染同樣預設使用 `minimal`（網頁·極簡），保留字型層級、留白和表格細節；已儲存的使用者或工作區排版選擇優先。 |
| 字型和寬度 | `fontSize` 預設 16、`fontFamily`、`lineHeight`、`maxWidth` 預設 820、`ignoreMaxWidth`、`zoom`；樣式使用的字型須已安裝於本機。 |
| 程式碼和中西文 | `showCodeHighlight`、`sourceFontFamily` / `sourceFontSize`（公式／圖表原始碼控制項）、`cjkLanguage`、`cjkAutoSpacing`；視覺間距不會插入原始碼空格。 |
| 檢視 | `defaultMode`、`showOutline`、`focusMode`、`typewriterMode`、`showStatusBar`、`showBlockHandle`、`autoHideScrollbars`、`ctrlWheelZoom`。 |
| Markdown 編輯 | `codeFence`、`emphasisMarker`、`bulletMarker`、`exitBlockOnEmptyEnter`、`useShiftEnterHardBreak`、強調轉換與字面符號跳脫。序列化設定在實際編輯後套用。 |
| 圖片 | `imageDirectory`、`fileImageHandling`、`useRelativeImagePaths`、`prefixImagePathsWithDot`。 |
| 格式快速鍵 | `shortcuts`；與「视图 → 快捷键…」錄鍵面板共用設定，只作用於 VS Code 的 MarkLeaf 渲染編輯區。 |
| 自訂 CSS | `customCss` 指向相對於文件或絕對路徑的 CSS 檔案；僅在受信任工作區載入，修改 CSS 後重新載入編輯器。 |

一般 Markdown 原始碼的字型、縮排和快速鍵使用 VS Code 原生設定。檔案／資料夾、最近檔案、分頁、自動儲存、復原、編碼、換行、視窗配置和擴充更新也使用 VS Code 本身的能力。共享公式與圖表控制項依 VS Code 語言選擇既有的簡體中文、繁體中文、英文或日文翻譯；匯出選單、面板、命令和狀態訊息依 VS Code 語言提供簡體中文、繁體中文、英文、日文。既有編輯選單和格式命令標題仍使用中文，其他既有命令保留英文。四語種文件不代表介面已全部在地化。

## 格式和交付範圍

僅開啟和閱讀不會改動來源檔案。實際視覺化編輯經過 Markdown 解析與序列化，可能正規化清單標記、空行、強調或其他格式；保留文件的換行類型與既有結尾換行，不承諾逐位元組保真。編輯範圍包含段落、標題、清單、工作項目、引用、程式碼、表格、圖片、連結、YAML Front Matter、註腳、公式、Mermaid 和提示框。MDX、自訂外掛語法或需保留任意原始 HTML 的文件，請使用原始碼編輯器。

底線相容讀取邊界明確的 `++文字++`：內側首尾不能為空白，外側不能與英文字母、數字、底線或其他加號相連。`C++`、`C++17` 和一般遞增運算子保留為文字。視覺化編輯後的底線儲存為 `<u>…</u>`，避免與字面加號或單字內的選取範圍產生歧義，並保留連續空格及其他行內格式。原生版和擴充功能使用相同規則。

目前交付目標為桌面 VS Code，瀏覽器版沒有擴充入口。遠端 URI 已接入 VS Code 檔案系統與資源 API，但 Windows、Linux、SSH/WSL 及實際剪貼簿、拖放、輸入法與快速鍵互動仍需在對應環境驗收。建置、Vitest 和模擬宿主測試不取代實際安裝後的介面驗收。

逐組對應見 [功能對應說明（簡體中文）](./feature-parity.md)。

## 開發與建置

從儲存庫根目錄執行，Node.js 22.12+，使用專案指定的 pnpm：

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir apps/vscode install --frozen-lockfile
pnpm build:vscode
pnpm test:editor-web
pnpm package:vscode
```

開發時可使用既有 VS Code，無需額外桌面執行環境：

```bash
code --new-window --extensionDevelopmentPath="$PWD/apps/vscode" path/to/document.md
```

`packages/editor-web/src/vscode.ts` 是擴充前端入口，`apps/vscode/src/extension.ts` 是 VS Code 文件適配層。Windows/macOS 入口使用 `main.ts` 和原生宿主協定。擴充建置輸出至 `apps/vscode/dist`，原生前端輸出至 `packages/editor-web/dist`。
