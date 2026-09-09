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
<html lang="zh-CN"><head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="${escapeAttribute(csp)}">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
${(entry.css ?? []).map(css => `<link rel="stylesheet" href="${assetUrl(css)}">`).join('\n')}
<title>MarkLeaf</title>
</head><body>
<div id="app">
  <header id="toolbar" aria-label="Markdown 工具栏">
    <strong class="brand">MarkLeaf</strong>
    <button id="mode" type="button" title="切换阅读与编辑模式">阅读</button>
    <span class="divider"></span>
    <button data-command="toggleBold" data-edit type="button" title="粗体 (Ctrl/Cmd+B)"><b>B</b></button>
    <button data-command="toggleItalic" data-edit type="button" title="斜体 (Ctrl/Cmd+I)"><i>I</i></button>
    <button data-command="toggleUnderline" data-edit type="button" title="下划线 (Ctrl/Cmd+U)"><u>U</u></button>
    <button data-command="toggleStrike" data-edit type="button" title="删除线"><s>S</s></button>
    <button data-command="toggleHighlight" data-edit type="button" title="高亮"><mark>H</mark></button>
    <button data-command="toggleCode" data-edit type="button" title="行内代码">&lt;/&gt;</button>
    <button data-action="format" data-edit type="button">格式…</button>
    <button data-action="insertLink" data-edit type="button">链接</button>
    <button data-action="insertImage" data-edit type="button">图片</button>
    <span class="spacer"></span>
    <button data-action="undo" data-edit type="button" title="撤销">↶</button>
    <button data-action="redo" data-edit type="button" title="重做">↷</button>
    <button data-action="openSource" type="button" title="切换源码 / 渲染视图 (Ctrl/Cmd+Shift+V)">源码</button>
    <button data-action="openSourceBeside" type="button" title="在侧边打开 VS Code 源码编辑器">并排</button>
  </header>
  <div id="notice" role="status" hidden><span id="notice-text"></span><button id="recover" type="button" hidden>将未同步内容打开为草稿</button></div>
  <main id="editor" class="markleaf-style-sans" aria-label="Markdown 文档" aria-busy="true"></main>
  <footer><span id="sync-status" role="status">正在加载…</span><span id="word-count"></span></footer>
</div>
<script nonce="${nonce}" type="module" src="${assetUrl(entry.file)}"></script>
</body></html>`
}
