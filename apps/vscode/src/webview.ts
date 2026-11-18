import * as vscode from 'vscode'
import { randomBytes } from 'node:crypto'

function escapeAttribute(value: string): string {
  return value.replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]!)
}

export async function webviewHtml(webview: vscode.Webview, assets: vscode.Uri): Promise<string> {
  const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(assets, '.vite', 'manifest.json'))
  const manifest = JSON.parse(Buffer.from(bytes).toString('utf8')) as Record<string, { file: string; css?: string[] }>
  const entry = manifest['src/vscode.ts']
  if (!entry) throw new Error('MarkLeaf webview build is missing. Run pnpm build:vscode.')
  const assetUrl = (file: string): string => escapeAttribute(webview.asWebviewUri(vscode.Uri.joinPath(assets, file)).toString())
  const nonce = randomBytes(18).toString('base64')
  const csp = `default-src 'none'; script-src 'nonce-${nonce}' ${webview.cspSource}; style-src ${webview.cspSource} 'unsafe-inline'; img-src ${webview.cspSource} https: http: data:; font-src ${webview.cspSource} data:; connect-src ${webview.cspSource}; base-uri 'none'; form-action 'none';`
  return `<!doctype html>
<html lang="${escapeAttribute(vscode.env?.language ?? 'zh-CN')}"><head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="${escapeAttribute(csp)}">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<link rel="stylesheet" href="${assetUrl('kernel/editor-core.css')}">
${(entry.css ?? []).map(css => `<link rel="stylesheet" href="${assetUrl(css)}">`).join('\n')}
<title>MarkLeaf</title>
</head><body>
<div id="app">
  <div id="editor-chrome">
  <header id="toolbar" aria-label="Markdown 工具栏">
    <strong class="brand">MarkLeaf</strong>
    <button id="mode" type="button">阅读</button>
    <details class="toolbar-menu"><summary data-ui="format">格式</summary><div class="menu-content">
      ${['toggleBold', 'toggleItalic', 'toggleUnderline', 'toggleStrike', 'toggleHighlight', 'toggleCode', 'formatPainter',
        'fitTableContents', 'distributeTableColumns', 'resetTableWidths'].map(id => `<button data-command="${id}" data-edit type="button">${id}</button>`).join('')}
      <button data-action="format" data-ui="formatMore" data-edit type="button">全部格式操作…</button>
    </div></details>
    <details class="toolbar-menu"><summary data-ui="edit">编辑</summary><div class="menu-content">
      <button data-action="insertLink" data-edit type="button">链接</button>
      <button data-action="insertImage" data-edit type="button">图片</button>
      <button data-action="insertImageUrl" data-edit type="button">插入图片地址…</button>
      <button data-action="image" type="button">当前图片操作…</button>
      <button data-action="copyMarkdown" type="button">复制为 Markdown</button>
      <button data-action="copyPlainText" type="button">复制为纯文本</button>
      <button data-action="copyHtml" type="button">复制为 HTML</button>
      <button data-action="pastePlainText" data-edit type="button">粘贴纯文本</button>
      <button data-action="find" type="button">查找…</button>
      <button data-action="replace" type="button">替换…</button>
    </div></details>
    <details class="toolbar-menu"><summary data-ui="view">视图</summary><div class="menu-content">
      <button data-action="openSourceBeside" type="button">并排打开源码</button>
      <button data-action="toggleOutline" type="button">大纲</button>
      <button data-action="toggleFocus" type="button">专注当前段落</button>
      <button data-action="toggleTypewriter" type="button">打字机滚动</button>
      <button data-action="zoomIn" type="button">放大</button>
      <button data-action="zoomOut" type="button">缩小</button>
      <button data-action="zoomReset" type="button">重置缩放</button>
      <button data-action="preferences" type="button">排版、主题与设置…</button>
      <button data-action="shortcuts" type="button">快捷键…</button>
      <button data-action="help" type="button">使用帮助</button>
    </div></details>
    <details class="toolbar-menu"><summary data-export-label="export">导出…</summary><div class="menu-content">
      <button data-action="saveMermaidSvg" disabled type="button">图表另存为 SVG…</button>
      <button data-action="saveMermaidPng" disabled type="button">图表另存为 PNG（2×）…</button>
      <button data-action="exportPdf" data-export-label="exportPdf" type="button">导出 PDF…</button>
      <button data-action="exportHtml" data-export-label="exportHtml" type="button">导出 HTML…</button>
      <button data-action="exportImage" data-export-label="exportImage" type="button">导出图片…</button>
      <button data-action="print" data-export-label="print" type="button">打印…</button>
      <button data-action="exportLast" data-export-label="last" type="button">按上次设置导出</button>
    </div></details>
    <span class="spacer"></span>
    <button data-action="undo" data-ui-icon data-edit type="button" title="撤销">↶</button>
    <button data-action="redo" data-ui-icon data-edit type="button" title="重做">↷</button>
  </header>
  <div id="notice" role="status" hidden><span id="notice-text"></span><button id="recover" data-ui="recover" type="button" hidden>将未同步内容打开为草稿</button></div>
  </div>
  <main id="editor" class="markleaf-style-minimal" aria-label="Markdown 文档" aria-busy="true"></main>
  <footer>
    <button data-action="toggleOutline" data-ui="toggleOutline" type="button">大纲</button>
    <button data-action="openSource" data-ui="openSource" type="button">源码</button>
    <span id="sync-status" role="status">正在加载…</span><span id="word-count"></span>
    <button data-action="changeEncoding" type="button"></button><button data-action="changeEol" type="button"></button>
    <button data-action="zoomOut" data-ui="zoomOut" data-ui-icon type="button">−</button>
    <button data-action="zoomReset" type="button">100%</button>
    <button data-action="zoomIn" data-ui="zoomIn" data-ui-icon type="button">+</button>
  </footer>
</div>
<script nonce="${nonce}" type="module" src="${assetUrl(entry.file)}"></script>
</body></html>`
}
