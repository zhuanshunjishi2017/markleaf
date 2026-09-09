import * as vscode from 'vscode'
import { documentReplacement, normalizeDocumentMarkdown } from './document-edit'
import { localResourceRoots, resolveDocumentLink } from './resources'
import { webviewHtml } from './webview'
import { pickFormat } from './formatting'
import { isWebviewMessage, type DocumentSnapshot, type ExtensionMessage, type HostAction, type WebviewMessage } from '../../../packages/editor-web/src/vscode-protocol'

const viewType = 'markleaf.editor'
type EditMessage = Extract<WebviewMessage, { type: 'edit' }>

function activeResource(): vscode.Uri | undefined {
  const input = vscode.window.tabGroups.activeTabGroup.activeTab?.input
  if (input instanceof vscode.TabInputText || input instanceof vscode.TabInputCustom) return input.uri
  return vscode.window.activeTextEditor?.document.uri
}

class EditorPanel implements vscode.Disposable {
  private readonly subscriptions: vscode.Disposable[] = []
  private applying?: { target: string; version?: number }
  private ready = false
  private disposed = false
  private flushId = 0
  private readonly flushes = new Map<number, { resolve(success: boolean): void; timer: ReturnType<typeof setTimeout> }>()

  constructor(readonly document: vscode.TextDocument, readonly panel: vscode.WebviewPanel) {
    this.subscriptions.push(
      panel.webview.onDidReceiveMessage((message: unknown) => {
        if (isWebviewMessage(message)) void this.receive(message).catch(error => this.report(error))
      }),
      vscode.workspace.onDidChangeTextDocument(event => {
        if (event.document !== document || event.contentChanges.length === 0) return
        if (this.applying?.target === document.getText()) {
          this.applying.version = document.version
          return
        }
        this.post(this.snapshot())
      }),
      vscode.workspace.onWillSaveTextDocument(event => {
        if (event.document === document && this.ready && !this.disposed) {
          // VS Code also invokes this path for menu Save and Auto Save.
          event.waitUntil(this.flush().then(success => {
            if (!success) throw new Error('MarkLeaf has unsynchronized edits; recover the draft before saving.')
          }))
        }
      }),
      vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration('markleaf', document.uri)) this.settings()
      }),
    )
  }

  dispose(): void {
    this.disposed = true
    this.subscriptions.forEach(subscription => subscription.dispose())
    for (const pending of this.flushes.values()) { clearTimeout(pending.timer); pending.resolve(false) }
    this.flushes.clear()
  }

  private snapshot(): DocumentSnapshot {
    return { type: 'document', version: this.document.version, markdown: this.document.getText(),
      writable: vscode.workspace.fs.isWritableFileSystem(this.document.uri.scheme) !== false }
  }

  private post(message: ExtensionMessage): void {
    if (!this.disposed) void this.panel.webview.postMessage(message)
  }

  requestAction(action: HostAction): void {
    if (this.ready) this.post({ type: 'requestAction', action })
  }

  private report(error: unknown): void {
    const message = error instanceof Error ? error.message : String(error)
    this.post({ type: 'error', message })
    void vscode.window.showErrorMessage(`MarkLeaf: ${message}`)
  }

  private settings(): void {
    const config = vscode.workspace.getConfiguration('markleaf', this.document.uri)
    this.post({ type: 'settings', fontSize: config.get('fontSize', 16), maxWidth: config.get('maxWidth', 820), defaultMode: config.get('defaultMode', 'edit') })
  }

  flush(): Promise<boolean> {
    if (!this.ready || this.disposed) return Promise.resolve(false)
    return new Promise(resolve => {
      const requestId = ++this.flushId
      const timer = setTimeout(() => { this.flushes.delete(requestId); resolve(false) }, 1200)
      this.flushes.set(requestId, { resolve, timer })
      this.post({ type: 'flush', requestId })
    })
  }

  async openSource(beside: boolean): Promise<void> {
    if (!await this.flush()) throw new Error('未同步的内容仍在 MarkLeaf 中，请先完成输入或打开冲突草稿。')
    await vscode.commands.executeCommand('vscode.openWith', this.document.uri, 'default', {
      viewColumn: beside ? vscode.ViewColumn.Beside : this.panel.viewColumn,
    })
  }

  private async edit(message: EditMessage): Promise<void> {
    const reject = (error: string): void => this.post({ type: 'rejected', sequence: message.sequence, document: this.snapshot(), error })
    if (this.applying || message.baseVersion !== this.document.version || this.document.isClosed) {
      reject('文件版本已改变。未同步的编辑保留在此处，请打开为草稿后与源码合并。')
      return
    }
    const before = this.document.getText()
    const target = normalizeDocumentMarkdown(message.markdown, before, this.document.eol === vscode.EndOfLine.CRLF ? '\r\n' : '\n')
    const replacement = documentReplacement(before, target)
    if (!replacement) {
      this.post({ type: 'accepted', sequence: message.sequence, version: this.document.version })
      return
    }
    const edit = new vscode.WorkspaceEdit()
    edit.replace(this.document.uri, new vscode.Range(this.document.positionAt(replacement.start), this.document.positionAt(replacement.end)), replacement.text)
    const applying = this.applying = { target, version: undefined as number | undefined }
    try {
      const applied = await vscode.workspace.applyEdit(edit)
      if (!applied || applying.version !== this.document.version || this.document.getText() !== target) {
        reject('VS Code 未能确认此次编辑，文件可能已发生变化。未同步内容已保留，请打开为草稿。')
      } else {
        this.post({ type: 'accepted', sequence: message.sequence, version: this.document.version })
      }
    } catch (error) {
      reject(`写入 VS Code 文档失败：${error instanceof Error ? error.message : String(error)}。未同步内容已保留。`)
    } finally {
      this.applying = undefined
    }
  }

  private async action(action: HostAction): Promise<void> {
    switch (action) {
      case 'save':
        if (!await this.document.save()) throw new Error('VS Code 未保存此文档。')
        break
      case 'undo': case 'redo':
        if (!this.panel.active) throw new Error('当前编辑器已切换，请聚焦原文档后再执行撤销或重做。')
        await vscode.commands.executeCommand(action)
        break
      case 'openSource': case 'openSourceBeside': await this.openSource(action === 'openSourceBeside'); break
      case 'format': {
        const command = await pickFormat()
        if (command) this.post({ type: 'command', command })
        break
      }
      case 'insertLink': case 'insertImage': {
        const text = await vscode.window.showInputBox({ prompt: action === 'insertImage' ? '图片地址（相对于 Markdown 文件的路径，或 HTTP/HTTPS URL）' : '链接地址', ignoreFocusOut: true })
        if (text) {
          if (!resolveDocumentLink(this.document, text)) throw new Error('请输入本地路径或 HTTP/HTTPS 链接。')
          this.post({ type: 'command', command: action === 'insertImage' ? 'insertImage' : 'setLink', text })
        }
        break
      }
    }
  }

  private async receive(message: WebviewMessage): Promise<void> {
    switch (message.type) {
      case 'ready': this.ready = true; this.settings(); this.post(this.snapshot()); break
      case 'edit': await this.edit(message); break
      case 'action':
        try { await this.action(message.action) }
        finally { this.post({ type: 'actionFinished' }) }
        break
      case 'flushed': {
        const pending = this.flushes.get(message.requestId)
        if (pending) { clearTimeout(pending.timer); this.flushes.delete(message.requestId); pending.resolve(message.success) }
        break
      }
      case 'resolveImages': {
        const urls: Record<string, string> = Object.create(null)
        for (const path of message.paths) {
          const uri = resolveDocumentLink(this.document, path)
          if (uri && !['http', 'https', 'mailto'].includes(uri.scheme)) urls[path] = this.panel.webview.asWebviewUri(uri).toString()
        }
        this.post({ type: 'images', urls })
        break
      }
      case 'recoverDraft': {
        // An untitled VS Code document makes the unsaved content visible and recoverable.
        const draft = await vscode.workspace.openTextDocument({ language: 'markdown', content: message.markdown })
        await vscode.window.showTextDocument(draft, { viewColumn: vscode.ViewColumn.Beside, preview: false })
        this.post({ type: 'recovered', document: this.snapshot() })
        break
      }
      case 'copy': await vscode.env.clipboard.writeText(message.text); break
      case 'codeLanguage': {
        const language = await vscode.window.showInputBox({ prompt: '代码块语言（例如 typescript、python、mermaid）', value: message.language })
        if (language !== undefined) this.post({ type: 'command', command: 'setCodeBlockLanguageAt', text: JSON.stringify({ position: message.position, language }) })
        break
      }
      case 'openLink': {
        const uri = resolveDocumentLink(this.document, message.href)
        if (!uri) throw new Error('不支持此链接地址。')
        if (['http', 'https', 'mailto'].includes(uri.scheme)) await vscode.env.openExternal(uri)
        else await vscode.commands.executeCommand('vscode.open', uri)
        break
      }
      case 'error': this.report(message.message); break
    }
  }
}

export function activate(context: vscode.ExtensionContext): void {
  const panels = new Set<EditorPanel>()
  const active = (uri?: vscode.Uri): EditorPanel | undefined => [...panels].find(item => uri
    ? item.document.uri.toString() === uri.toString() : item.panel.active)
  const provider: vscode.CustomTextEditorProvider = {
    async resolveCustomTextEditor(document, panel) {
      const assets = vscode.Uri.joinPath(context.extensionUri, 'dist', 'webview')
      panel.webview.options = { enableScripts: true, localResourceRoots: localResourceRoots(document, assets) }
      const editor = new EditorPanel(document, panel)
      panels.add(editor)
      panel.onDidDispose(() => { panels.delete(editor); editor.dispose() }, undefined, context.subscriptions)
      try { panel.webview.html = await webviewHtml(panel.webview, assets) }
      catch (error) { panels.delete(editor); editor.dispose(); throw error }
    },
  }
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(viewType, provider, {
      webviewOptions: { retainContextWhenHidden: true, enableFindWidget: true },
      supportsMultipleEditorsPerDocument: false,
    }),
    vscode.commands.registerCommand('markleaf.open', async (uri?: vscode.Uri) => {
      const resource = uri ?? activeResource()
      if (!resource) { void vscode.window.showInformationMessage('请先打开或选择一个 Markdown 文件。'); return }
      await vscode.commands.executeCommand('vscode.openWith', resource, viewType)
    }),
    vscode.commands.registerCommand('markleaf.format', () => active()?.requestAction('format')),
    vscode.commands.registerCommand('markleaf.toggleEditor', async () => {
      try {
        const visual = active()
        if (visual) await visual.openSource(false)
        else {
          const document = vscode.window.activeTextEditor?.document
          if (document?.languageId === 'markdown') await vscode.commands.executeCommand('vscode.openWith', document.uri, viewType)
        }
      } catch (error) {
        void vscode.window.showErrorMessage(`MarkLeaf: ${error instanceof Error ? error.message : String(error)}`)
      }
    }),
    ...(['openSource', 'openSourceBeside'] as const).map(command => vscode.commands.registerCommand(`markleaf.${command}`, async (uri?: vscode.Uri) => {
      try { await active(uri)?.openSource(command === 'openSourceBeside') }
      catch (error) { void vscode.window.showErrorMessage(`MarkLeaf: ${error instanceof Error ? error.message : String(error)}`) }
    })),
    { dispose: () => panels.forEach(panel => panel.dispose()) },
  )
}
