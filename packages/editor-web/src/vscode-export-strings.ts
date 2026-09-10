const en = {
  alertNote: 'Note', alertTip: 'Tip', alertImportant: 'Important', alertWarning: 'Warning', alertCaution: 'Caution',
  export: 'Export…', exportPdf: 'Export PDF…', exportHtml: 'Export HTML…', exportImage: 'Export Image…',
  print: 'Print…', last: 'Export with Last Settings', title: 'Export document',
  intro: 'Minimal typography, complete document. Export settings do not change the editor.',
  format: 'Format', typography: 'Typography', colorTheme: 'Colors', minimal: 'Web · Minimal',
  fontSize: 'Font size (px)', lineHeight: 'Line height', contentWidth: 'Content width (px)',
  paperSize: 'Paper size', landscape: 'Landscape', marginTop: 'Top (mm)', marginRight: 'Right (mm)',
  marginBottom: 'Bottom (mm)', marginLeft: 'Left (mm)', header: 'Header text', footer: 'Footer text',
  pageNumbers: 'Page numbers', keepTablesTogether: 'Keep tables together when they fit',
  keepHeadingsWithNext: 'Keep headings with the next block', imageScale: 'Image scale',
  imageMaxHeight: 'Maximum image height (px)', jpegQuality: 'JPEG quality (1–100)',
  imageHint: 'Long documents split into numbered images at this output height. Width includes 112 px of page padding before scaling.',
  printHint: 'Chrome/Edge opens a print dialog. Paper, margins and background graphics can be adjusted there. Close the dialog to return.',
  pageHint: 'Header and footer are plain text. Oversized tables may span pages. For page numbers, leave at least 10 mm bottom margin.',
  save: 'Export…', preview: 'Preview', cancel: 'Cancel', close: 'Close', defaults: 'Reset',
  preparing: 'Preparing export…', rendering: 'Rendering document…', writing: 'Writing output…',
  previewing: 'Preview is open. Close its window to return.', printing: 'Print dialog is open. Close it to return.',
  completed: 'Export saved', printClosed: 'Print dialog closed; printer completion is not reported.',
  cancelled: 'Export cancelled.', invalid: 'Check the export options and number ranges.',
  busy: 'An export is already running for this document.', unsynced: 'Finish input or recover the unsynchronized draft before exporting.',
  closed: 'The document view closed before export preparation completed.', timeout: 'Export preparation timed out.',
  renderFailed: 'A formula or diagram could not be rendered. Correct it before exporting.',
  missingImage: 'Unable to embed image', unsupportedImage: 'Unsupported image format', largeImage: 'Image exceeds 16 MiB',
  missingBrowser: 'Chrome or Edge was not found. Select an installed browser executable, or install one and retry.',
  chooseBrowser: 'Select Browser', browserPath: 'Path to Chrome/Edge executable (not the .app folder)',
  browserInvalid: 'The configured browser executable does not exist or cannot be executed',
  browserFailed: 'Unable to launch Chrome/Edge. Check the executable and browser version',
  remotePrint: 'Interactive preview and printing require a local desktop VS Code window. Export a file on the remote host, then open it locally.',
  open: 'Open', reveal: 'Show in Folder', overwrite: 'Overwrite', exists: 'These output files already exist',
  partial: 'Files already saved', empty: 'The document contains no exportable content.',
  readyError: 'Fonts or images failed to load', dialogError: 'The print dialog did not become ready',
  installBrowser: 'Install Chrome or Edge, or set MarkLeaf: Export Browser Path.',
}
type Strings = { [K in keyof typeof en]: string }
const zh: Strings = {
  alertNote: '备注', alertTip: '提示', alertImportant: '重要', alertWarning: '警告', alertCaution: '注意',
  export: '导出…', exportPdf: '导出 PDF…', exportHtml: '导出 HTML…', exportImage: '导出图片…',
  print: '打印…', last: '按上次设置导出', title: '导出文档',
  intro: '极简排版，完整文档。导出设置不改变编辑器中的显示。',
  format: '格式', typography: '排版', colorTheme: '配色', minimal: '网页 · 极简',
  fontSize: '字号（px）', lineHeight: '行高', contentWidth: '内容宽度（px）',
  paperSize: '纸张', landscape: '横向', marginTop: '上边距（mm）', marginRight: '右边距（mm）',
  marginBottom: '下边距（mm）', marginLeft: '左边距（mm）', header: '页眉文字', footer: '页脚文字',
  pageNumbers: '页码', keepTablesTogether: '空间允许时保持表格完整', keepHeadingsWithNext: '标题与下一块保持同页',
  imageScale: '图片倍率', imageMaxHeight: '单张最大高度（px）', jpegQuality: 'JPEG 质量（1–100）',
  imageHint: '长文档按此输出高度拆成连续编号图片。缩放前的页面宽度包含额外 112 px 留白。',
  printHint: '在 Chrome/Edge 打印对话框中选择打印机，可调整纸张、边距和背景图形。关闭对话框后返回。',
  pageHint: '页眉页脚使用纯文本。过大的表格仍可能跨页；显示页码时建议下边距至少 10 mm。',
  save: '导出…', preview: '预览', cancel: '取消', close: '关闭', defaults: '恢复默认',
  preparing: '正在准备导出…', rendering: '正在渲染文档…', writing: '正在写入文件…',
  previewing: '预览已打开，关闭预览窗口后返回。', printing: '打印对话框已打开，关闭后返回。',
  completed: '导出已保存', printClosed: '打印对话框已关闭；无法读取打印机的实际完成状态。',
  cancelled: '已取消导出。', invalid: '请检查导出选项和数值范围。', busy: '此文档已有导出正在进行。',
  unsynced: '请先完成输入或恢复未同步草稿，再导出。', closed: '准备导出时文档视图已关闭。', timeout: '准备导出超时。',
  renderFailed: '公式或图表渲染失败，请修正后再导出。', missingImage: '无法嵌入图片', unsupportedImage: '不支持此图片格式', largeImage: '图片超过 16 MiB',
  missingBrowser: '未找到 Chrome 或 Edge。请选择已安装浏览器的可执行文件，或安装后重试。',
  chooseBrowser: '选择浏览器', browserPath: 'Chrome/Edge 可执行文件路径（不是 .app 文件夹）', browserInvalid: '配置的浏览器文件不存在或不可执行',
  browserFailed: '无法启动 Chrome/Edge，请检查可执行文件及浏览器版本',
  remotePrint: '交互预览和打印需要本地桌面 VS Code 窗口。请先在远程宿主导出文件，再到本机打开。',
  open: '打开', reveal: '在文件夹中显示', overwrite: '覆盖', exists: '以下输出文件已经存在', partial: '已保存的文件', empty: '文档没有可导出的内容。',
  readyError: '字体或图片加载失败', dialogError: '打印对话框未能就绪', installBrowser: '请安装 Chrome 或 Edge，或配置 MarkLeaf: Export Browser Path。',
}
const tw: Strings = {
  alertNote: '備註', alertTip: '提示', alertImportant: '重要', alertWarning: '警告', alertCaution: '注意',
  export: '匯出…', exportPdf: '匯出 PDF…', exportHtml: '匯出 HTML…', exportImage: '匯出圖片…', print: '列印…', last: '依上次設定匯出', title: '匯出文件',
  intro: '極簡排版，完整文件。匯出設定不改變編輯器中的顯示。', format: '格式', typography: '排版', colorTheme: '配色', minimal: '網頁 · 極簡',
  fontSize: '字級（px）', lineHeight: '行高', contentWidth: '內容寬度（px）', paperSize: '紙張', landscape: '橫向',
  marginTop: '上邊界（mm）', marginRight: '右邊界（mm）', marginBottom: '下邊界（mm）', marginLeft: '左邊界（mm）', header: '頁首文字', footer: '頁尾文字',
  pageNumbers: '頁碼', keepTablesTogether: '空間允許時保持表格完整', keepHeadingsWithNext: '標題與下一區塊保持同頁', imageScale: '圖片倍率', imageMaxHeight: '單張最大高度（px）', jpegQuality: 'JPEG 品質（1–100）',
  imageHint: '長文件依此輸出高度拆成連續編號圖片。縮放前的頁面寬度包含額外 112 px 留白。',
  printHint: '在 Chrome/Edge 列印對話框中選擇印表機，可調整紙張、邊界和背景圖形。關閉對話框後返回。',
  pageHint: '頁首頁尾使用純文字。過大的表格仍可能跨頁；顯示頁碼時建議下邊界至少 10 mm。',
  save: '匯出…', preview: '預覽', cancel: '取消', close: '關閉', defaults: '還原預設', preparing: '正在準備匯出…', rendering: '正在算繪文件…', writing: '正在寫入檔案…',
  previewing: '預覽已開啟，關閉預覽視窗後返回。', printing: '列印對話框已開啟，關閉後返回。', completed: '匯出已儲存', printClosed: '列印對話框已關閉；無法讀取印表機的實際完成狀態。',
  cancelled: '已取消匯出。', invalid: '請檢查匯出選項與數值範圍。', busy: '此文件已有匯出正在進行。', unsynced: '請先完成輸入或復原未同步草稿，再匯出。',
  closed: '準備匯出時文件檢視已關閉。', timeout: '準備匯出逾時。', renderFailed: '公式或圖表算繪失敗，請修正後再匯出。', missingImage: '無法嵌入圖片', unsupportedImage: '不支援此圖片格式', largeImage: '圖片超過 16 MiB',
  missingBrowser: '找不到 Chrome 或 Edge。請選擇已安裝瀏覽器的執行檔，或安裝後重試。', chooseBrowser: '選擇瀏覽器', browserPath: 'Chrome/Edge 執行檔路徑（不是 .app 資料夾）', browserInvalid: '設定的瀏覽器檔案不存在或無法執行', browserFailed: '無法啟動 Chrome/Edge，請檢查執行檔與瀏覽器版本',
  remotePrint: '互動預覽和列印需要本機桌面 VS Code 視窗。請先在遠端匯出檔案，再於本機開啟。', open: '開啟', reveal: '在資料夾中顯示', overwrite: '覆寫', exists: '以下輸出檔案已存在', partial: '已儲存的檔案', empty: '文件沒有可匯出的內容。',
  readyError: '字型或圖片載入失敗', dialogError: '列印對話框未能就緒', installBrowser: '請安裝 Chrome 或 Edge，或設定 MarkLeaf: Export Browser Path。',
}
const ja: Strings = {
  alertNote: '注記', alertTip: 'ヒント', alertImportant: '重要', alertWarning: '警告', alertCaution: '注意',
  export: 'エクスポート…', exportPdf: 'PDF をエクスポート…', exportHtml: 'HTML をエクスポート…', exportImage: '画像をエクスポート…', print: '印刷…', last: '前回の設定でエクスポート', title: '文書をエクスポート',
  intro: '極簡の組版で文書全体を出力します。エディターの表示設定は変わりません。', format: '形式', typography: '組版', colorTheme: '配色', minimal: 'Web · 極簡',
  fontSize: '文字サイズ（px）', lineHeight: '行の高さ', contentWidth: '本文の幅（px）', paperSize: '用紙', landscape: '横向き', marginTop: '上余白（mm）', marginRight: '右余白（mm）', marginBottom: '下余白（mm）', marginLeft: '左余白（mm）', header: 'ヘッダーの文字', footer: 'フッターの文字',
  pageNumbers: 'ページ番号', keepTablesTogether: '収まる表を同じページに保持', keepHeadingsWithNext: '見出しと次のブロックを同じページに保持', imageScale: '画像の倍率', imageMaxHeight: '画像の最大高さ（px）', jpegQuality: 'JPEG 品質（1–100）',
  imageHint: '長い文書はこの出力高さで連番画像に分割します。拡大前の幅には 112 px の余白が加わります。',
  printHint: 'Chrome/Edge の印刷ダイアログでプリンター、用紙、余白、背景を選べます。閉じると戻ります。', pageHint: 'ヘッダーとフッターはプレーンテキストです。大きな表は改ページされます。ページ番号には下余白 10 mm 以上を推奨します。',
  save: 'エクスポート…', preview: 'プレビュー', cancel: 'キャンセル', close: '閉じる', defaults: '既定に戻す', preparing: '出力を準備中…', rendering: '文書を描画中…', writing: 'ファイルを書き込み中…', previewing: 'プレビューを閉じると戻ります。', printing: '印刷ダイアログを閉じると戻ります。', completed: '出力を保存しました', printClosed: '印刷ダイアログを閉じました。プリンターの完了状態は取得できません。',
  cancelled: 'エクスポートをキャンセルしました。', invalid: '出力設定と数値の範囲を確認してください。', busy: 'この文書はすでに出力中です。', unsynced: '入力を確定するか未同期の下書きを復元してから出力してください。', closed: '準備中に文書ビューが閉じられました。', timeout: '出力の準備がタイムアウトしました。', renderFailed: '数式または図の描画に失敗しました。修正してから出力してください。',
  missingImage: '画像を埋め込めません', unsupportedImage: '未対応の画像形式', largeImage: '画像が 16 MiB を超えています', missingBrowser: 'Chrome または Edge が見つかりません。実行ファイルを選択するか、インストールして再試行してください。', chooseBrowser: 'ブラウザーを選択', browserPath: 'Chrome/Edge の実行ファイルパス（.app フォルダーではありません）', browserInvalid: 'ブラウザーの実行ファイルが存在しないか実行できません', browserFailed: 'Chrome/Edge を起動できません。実行ファイルとバージョンを確認してください',
  remotePrint: '対話プレビューと印刷にはローカルのデスクトップ VS Code が必要です。リモート側でファイルを出力し、ローカルで開いてください。', open: '開く', reveal: 'フォルダーを表示', overwrite: '上書き', exists: '次の出力ファイルは既に存在します', partial: '保存済みファイル', empty: '出力できる本文がありません。', readyError: 'フォントまたは画像の読み込みに失敗しました', dialogError: '印刷ダイアログを準備できませんでした', installBrowser: 'Chrome/Edge をインストールするか MarkLeaf: Export Browser Path を設定してください。',
}
export function exportStrings(language: string): Strings {
  const lang = language.toLowerCase()
  if (/^zh-(tw|hk|hant)/.test(lang)) return tw
  if (lang.startsWith('zh')) return zh
  return lang.startsWith('ja') ? ja : en
}

// Keep stable option IDs separate from the localized display names.
export function exportOptionLabel(value: string, language: string): string {
  const lang = language.toLowerCase()
  const column = /^zh-(tw|hk|hant)/.test(lang) ? 3 : lang.startsWith('zh') ? 0 : lang.startsWith('ja') ? 2 : 1
  const labels: Record<string, string[]> = {
    sans: ['无衬线', 'Sans serif', 'ゴシック', '無襯線'], serif: ['衬线', 'Serif', '明朝', '襯線'],
    print: ['印刷', 'Print', '印刷', '印刷'], 'print-double': ['双栏印刷', 'Two-column print', '2 段組', '雙欄印刷'],
    latex: ['LaTeX', 'LaTeX', 'LaTeX', 'LaTeX'], 'retro-print': ['复古印刷', 'Retro print', 'レトロ印刷', '復古印刷'],
    minimal: ['网页 · 极简', 'Web · Minimal', 'Web · 極簡', '網頁 · 極簡'], magazine: ['杂志', 'Magazine', '雑誌', '雜誌'], notebook: ['笔记本', 'Notebook', 'ノート', '筆記本'],
    'default-light': ['经典浅色', 'Classic light', 'クラシック・ライト', '經典淺色'], 'apple-blue': ['Apple 蓝', 'Apple blue', 'Apple ブルー', 'Apple 藍'],
    'apple-dark': ['Apple 深色', 'Apple dark', 'Apple ダーク', 'Apple 深色'], dark: ['深色', 'Dark', 'ダーク', '深色'],
    'deep-sea': ['深海', 'Deep sea', '深海', '深海'], espresso: ['浓缩咖啡', 'Espresso', 'エスプレッソ', '濃縮咖啡'], forest: ['森林', 'Forest', '森林', '森林'],
    'high-contrast-dark': ['高对比度深色', 'High contrast dark', '高コントラスト・暗色', '高對比深色'], 'high-contrast-light': ['高对比度浅色', 'High contrast light', '高コントラスト・明色', '高對比淺色'],
    ink: ['水墨', 'Ink', '墨', '水墨'], lavender: ['薰衣草', 'Lavender', 'ラベンダー', '薰衣草'], memo: ['便笺', 'Memo', 'メモ', '便箋'],
    'morandi-cyan': ['莫兰迪青', 'Morandi cyan', 'モランディ・シアン', '莫蘭迪青'], 'morandi-dark': ['莫兰迪深色', 'Morandi dark', 'モランディ・ダーク', '莫蘭迪深色'],
    morandi: ['莫兰迪', 'Morandi', 'モランディ', '莫蘭迪'], 'pure-black': ['纯黑', 'Pure black', '黒', '純黑'], rose: ['玫瑰', 'Rose', 'ローズ', '玫瑰'],
    saltlemon: ['盐柠檬', 'Salt lemon', '塩レモン', '鹽檸檬'], 'yellowed-page': ['泛黄纸张', 'Yellowed page', '古紙', '泛黃紙張'],
    printDialog: ['打印', 'Print', '印刷', '列印'],
  }
  return labels[value]?.[column] ?? value.toUpperCase()
}
