// Labels shared by the compact menu, status bar and outline.
const labels = {
  edit: ['Edit', '编辑', '編輯', '編集'], read: ['Read', '阅读', '閱讀', '閲覧'],
  format: ['Format', '格式', '格式', '書式'], view: ['View', '视图', '檢視', '表示'],
  toggleOutline: ['Outline', '大纲', '大綱', 'アウトライン'], openSource: ['Source', '源码', '原始碼', 'ソース'],
  openSourceBeside: ['Source to the Side', '并排打开源码', '並排開啟原始碼', 'ソースを横に開く'],
  formatMore: ['All Formatting…', '全部格式操作…', '全部格式操作…', 'すべての書式…'],
  formatPainter: ['Format Painter', '格式刷', '複製格式', '書式のコピー'],
  toggleBold: ['Bold', '粗体', '粗體', '太字'], toggleItalic: ['Italic', '斜体', '斜體', '斜体'],
  toggleUnderline: ['Underline', '下划线', '底線', '下線'], toggleStrike: ['Strikethrough', '删除线', '刪除線', '取り消し線'],
  toggleHighlight: ['Highlight', '高亮', '醒目提示', 'ハイライト'], toggleCode: ['Inline Code', '行内代码', '行內程式碼', 'インラインコード'],
  insertLink: ['Insert Link…', '插入链接…', '插入連結…', 'リンクを挿入…'], insertImage: ['Insert Image…', '插入图片…', '插入圖片…', '画像を挿入…'],
  insertImageUrl: ['Insert Image URL…', '插入图片地址…', '插入圖片網址…', '画像 URL を挿入…'], image: ['Image Actions…', '图片操作…', '圖片操作…', '画像の操作…'],
  copyMarkdown: ['Copy as Markdown', '复制为 Markdown', '複製為 Markdown', 'Markdown としてコピー'],
  copyPlainText: ['Copy as Plain Text', '复制为纯文本', '複製為純文字', 'テキストとしてコピー'],
  copyHtml: ['Copy as HTML', '复制为 HTML', '複製為 HTML', 'HTML としてコピー'],
  pastePlainText: ['Paste Plain Text', '粘贴纯文本', '貼上純文字', 'テキストを貼り付け'],
  find: ['Find…', '查找…', '尋找…', '検索…'], replace: ['Replace…', '替换…', '取代…', '置換…'],
  toggleFocus: ['Focus Paragraph', '专注当前段落', '專注目前段落', '段落にフォーカス'],
  toggleTypewriter: ['Typewriter Scrolling', '打字机滚动', '打字機捲動', 'タイプライタースクロール'],
  zoomIn: ['Zoom In', '放大', '放大', '拡大'], zoomOut: ['Zoom Out', '缩小', '縮小', '縮小'],
  zoomReset: ['Reset Zoom', '重置缩放', '重設縮放', 'ズームをリセット'],
  preferences: ['Typography and Settings…', '排版与设置…', '排版與設定…', '組版と設定…'],
  shortcuts: ['Keyboard Shortcuts…', '快捷键…', '快速鍵…', 'ショートカット…'], help: ['Help', '使用帮助', '使用說明', 'ヘルプ'],
  undo: ['Undo', '撤销', '復原', '元に戻す'], redo: ['Redo', '重做', '重做', 'やり直す'],
  changeEncoding: ['Encoding', '编码', '編碼', 'エンコード'], changeEol: ['Line Endings', '换行符', '行尾字元', '改行コード'],
  synced: ['Synced to VS Code', '已同步到 VS Code', '已同步至 VS Code', 'VS Code と同期済み'],
  syncing: ['Synchronizing…', '正在同步…', '正在同步…', '同期中…'], conflict: ['Unsynchronized edits', '存在未同步编辑', '有未同步編輯', '未同期の編集あり'],
  readonly: ['Read-only', '文件只读', '唯讀', '読み取り専用'], readMode: ['Read mode', '阅读模式', '閱讀模式', '閲覧モード'],
  failed: ['Document load failed', '文档加载失败', '文件載入失敗', '読み込み失敗'],
  loading: ['Loading…', '正在加载…', '正在載入…', '読み込み中…'], recover: ['Open Unsynced Draft', '将未同步内容打开为草稿', '開啟未同步草稿', '未同期の下書きを開く'],
  fitTableContents: ['Fit Table to Contents', '按内容调整列宽', '依內容調整欄寬', '内容に合わせて列幅を調整'],
  distributeTableColumns: ['Distribute Columns', '平均分配列宽', '平均分配欄寬', '列幅を均等にする'],
  resetTableWidths: ['Reset Column Widths', '重置列宽', '重設欄寬', '列幅をリセット'],
  saveMermaidSvg: ['Save Diagram as SVG…', '图表另存为 SVG…', '圖表另存為 SVG…', '図を SVG で保存…'],
  saveMermaidPng: ['Save Diagram as PNG (2×)…', '图表另存为 PNG（2×）…', '圖表另存為 PNG（2×）…', '図を PNG（2倍）で保存…'],
  chars: ['characters', '字符', '字元', '文字'], selected: ['selected', '已选', '已選', '選択'],
  expand: ['Expand All', '全部展开', '全部展開', 'すべて展開'], collapse: ['Collapse All', '全部折叠', '全部摺疊', 'すべて折りたたむ'],
  locate: ['Locate', '定位', '定位', '現在位置'], search: ['Search headings', '搜索标题', '搜尋標題', '見出しを検索'],
  empty: ['No headings', '文档中没有标题', '文件沒有標題', '見出しはありません'], noMatch: ['No matching headings', '没有匹配的标题', '沒有相符的標題', '該当する見出しはありません'],
} as const
export type ChromeLabel = keyof typeof labels
export function chromeLabel(key: ChromeLabel, language: string): string {
  const index = /^zh-(tw|hk|hant)/i.test(language) ? 2 : /^zh/i.test(language) ? 1 : /^ja/i.test(language) ? 3 : 0
  return labels[key][index]
}
export function localizeChrome(language: string): void {
  document.documentElement.lang = language
  for (const el of document.querySelectorAll<HTMLElement>('[data-ui], #toolbar [data-action], #toolbar [data-command]')) {
    const key = el.dataset.ui || el.dataset.command || el.dataset.action || ''
    if (!(key in labels)) continue
    const label = chromeLabel(key as ChromeLabel, language)
    if (el.dataset.uiIcon !== undefined) { el.title = label; el.setAttribute('aria-label', label) }
    else el.textContent = label
  }
}
