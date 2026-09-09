import * as vscode from 'vscode'
import { documentReplacement, normalizeDocumentMarkdown } from './document-edit'
import { localResourceRoots, resolveDocumentLink } from './resources'
import { webviewHtml } from './webview'
import { pickFormat, prepareFormatCommand } from './formatting'
import { importImages, pickImages, saveImageAs } from './images'
import { readSettings, updateSetting, pickPreferences, readShortcutSettings, updateShortcut } from './settings'
import { formatActions } from '../../../packages/editor-web/src/vscode-shortcuts'
import { isWebviewMessage, type DocumentSnapshot, type ExtensionMessage, type HostAction, type ActionContext, type ImageUpload, type WebviewFocus, type WebviewMessage } from '../../../packages/editor-web/src/vscode-protocol'

const viewType = 'markleaf.editor'
type EditMessage = Extract<WebviewMessage, { type: 'edit' }>

function activeResource(): vscode.Uri | undefined {
  const input = vscode.window.tabGroups.activeTabGroup.activeTab?.input
  if (input instanceof vscode.TabInputText || input instanceof vscode.TabInputCustom) return input.uri
  return vscode.window.activeTextEditor?.document.uri
}

class EditorPanel implements vscode.Disposable {
  focus: WebviewFocus = null
  private readonly subscriptions: vscode.Disposable[] = []
  private applying?: { target: string; version?: number }
  private ready = false
  private mac = false
  private disposed = false
  private flushId = 0
  private readonly flushes = new Map<number, { resolve(success: boolean): void; timer: ReturnType<typeof setTimeout> }>()

  constructor(readonly document: vscode.TextDocument, readonly panel: vscode.WebviewPanel, private readonly updateFocus: () => void) {
    this.subscriptions.push(
      panel.onDidChangeViewState(() => {
        if (!panel.active) this.focus = null
        this.updateFocus()
      }),
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
        if (event.affectsConfiguration('markleaf', document.uri)) void this.settings().catch(error => this.report(error))
      }),
    )
  }

  dispose(): void {
    this.disposed = true
    this.focus = null
    this.updateFocus()
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

  requestFormatCommand(command: string): void {
    if (this.ready) this.post({ type: 'requestFormatCommand', command })
  }

  private report(error: unknown): void {
    const message = error instanceof Error ? error.message : String(error)
    this.post({ type: 'error', message })
    void vscode.window.showErrorMessage(`MarkLeaf: ${message}`)
  }

  private async settings(): Promise<void> {
    const settings = readSettings(this.document.uri)
    let customCss = ''
    if (settings.customCss && vscode.workspace.isTrusted) {
      const uri = resolveDocumentLink(this.document, settings.customCss)
      if (!uri || ['http', 'https', 'mailto'].includes(uri.scheme)) throw new Error('自定义样式须为本地或工作区 CSS 文件。')
      customCss = Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8')
    }
    this.post({ type: 'settings', settings, customCss, language: vscode.env.language, shortcuts: readShortcutSettings(this.document.uri) })
  }

  private grantImageDirectory(uri: vscode.Uri): void {
    const roots = this.panel.webview.options.localResourceRoots ?? []
    if (!roots.some(root => root.toString() === uri.toString())) {
      this.panel.webview.options = { ...this.panel.webview.options, localResourceRoots: [...roots, uri] }
    }
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

  private async action(action: HostAction, context: ActionContext = {}, files?: ImageUpload[], formatCommand?: string): Promise<void> {
    switch (action) {
      case 'save':
        if (!await this.document.save()) throw new Error('VS Code 未保存此文档。')
        break
      case 'undo': case 'redo':
        if (!this.panel.active) throw new Error('当前编辑器已切换，请聚焦原文档后再执行撤销或重做。')
        await vscode.commands.executeCommand(action)
        break
      case 'openSource': case 'openSourceBeside': await this.openSource(action === 'openSourceBeside'); break
      case 'format': case 'block': {
        const command = await pickFormat(context, action === 'block', readShortcutSettings(this.document.uri).overrides, this.mac)
        if (command) this.post({ type: 'command', ...command })
        break
      }
      case 'formatCommand': {
        if (!formatCommand || context.editable !== true) return
        const command = await prepareFormatCommand(formatCommand, context)
        if (command) this.post({ type: 'command', ...command })
        break
      }
      case 'insertImage': {
        const paths = await pickImages(this.document, readSettings(this.document.uri), uri => this.grantImageDirectory(uri))
        if (paths) this.post({ type: 'command', command: 'insertImages', text: JSON.stringify(paths) })
        break
      }
      case 'importImages': {
        const paths = await importImages(this.document, readSettings(this.document.uri), files ?? [])
        this.post({ type: 'command', command: 'insertImages', text: JSON.stringify(paths) })
        break
      }
      case 'insertLink': case 'insertImageUrl': {
        const text = await vscode.window.showInputBox({ prompt: action === 'insertImageUrl' ? '图片地址（相对路径或 HTTP/HTTPS URL）' : '链接地址',
          value: action === 'insertLink' ? context.linkHref : '', ignoreFocusOut: true,
          validateInput: text => text.startsWith('#') || resolveDocumentLink(this.document, text) ? undefined : '请输入本地路径或 HTTP/HTTPS 链接' })
        if (text) this.post({ type: 'command', command: action === 'insertImageUrl' ? 'insertImage' : 'setLink', text })
        break
      }
      case 'image': {
        if (!context.imageSource) return
        const choices = [
          ['替换图片…', 'changeImage'], ['图片标题…', 'setImageCaption'], ['顺时针旋转 90°', 'rotateImageClockwise'],
          ['宽度 50%', '50'], ['宽度 75%', '75'], ['宽度 90%', '90'], ['宽度 100%', '100'],
          ['自定义宽度…', 'resizeImage'], ['图片另存为…', 'saveImageAs'],
        ].filter(([, command]) => context.editable !== false || command === 'saveImageAs').map(([label, command]) => ({ label: label!, command: command! }))
        const chosen = await vscode.window.showQuickPick(choices, { title: 'MarkLeaf 当前图片' })
        if (!chosen) return
        let command = chosen.command
        let text: string | undefined
        if (command === 'saveImageAs') { await saveImageAs(this.document, context.imageSource); return }
        if (command === 'changeImage') {
          const source = await vscode.window.showQuickPick(['从文件选择', '输入图片地址'], { title: '替换当前图片' })
          if (!source) return
          if (source === '从文件选择') text = (await pickImages(this.document, readSettings(this.document.uri), uri => this.grantImageDirectory(uri), false))?.[0]
          else text = await vscode.window.showInputBox({ prompt: '替换图片地址', value: context.imageSource,
            validateInput: text => resolveDocumentLink(this.document, text) ? undefined : '请输入有效的图片地址' })
        } else if (command === 'setImageCaption') {
          text = await vscode.window.showInputBox({ prompt: '图片标题（留空清除）', value: context.caption ?? '' })
        } else if (command === 'resizeImage') {
          text = await vscode.window.showInputBox({ prompt: '图片宽度百分比（1–100）', value: '100',
            validateInput: text => Number.isFinite(+text) && +text >= 1 && +text <= 100 ? undefined : '请输入 1–100' })
        } else if (/^\d+$/.test(command)) { text = command; command = 'resizeImage' }
        if (command !== 'rotateImageClockwise' && text === undefined) return
        this.post({ type: 'command', command, text })
        break
      }
      case 'codeLanguage': {
        const text = await vscode.window.showInputBox({ prompt: '代码块语言（留空为纯文本）', value: context.codeBlockLanguage ?? '' })
        if (text !== undefined) this.post({ type: 'command', command: 'setCodeBlockLanguage', text })
        break
      }
      case 'pastePlainText': this.post({ type: 'command', command: 'pastePlainText', text: await vscode.env.clipboard.readText() }); break
      case 'preferences': await pickPreferences(this.document.uri); break
      case 'help': await vscode.commands.executeCommand('workbench.action.openWalkthrough', 'markleaf.markleaf#markleaf.start'); break

    }
  }

  private async receive(message: WebviewMessage): Promise<void> {
    switch (message.type) {
      case 'ready': this.mac = message.mac === true; this.ready = true; try { await this.settings() } finally { this.post(this.snapshot()) }; break
      case 'focus': this.focus = this.panel.active ? message.target : null; this.updateFocus(); break
      case 'edit': await this.edit(message); break
      case 'action':
        try { await this.action(message.action, message.context, message.files, message.command) }
        finally { this.post({ type: 'actionFinished' }) }
        break
      case 'updateSetting': await updateSetting(this.document.uri, message.key, message.value); break
      case 'updateShortcut': {
        let error: string | undefined
        try { await updateShortcut(this.document.uri, message.command, message.binding, this.mac) }
        catch (failure) { error = failure instanceof Error ? failure.message : String(failure) }
        this.post({ type: 'shortcutSaved', requestId: message.requestId, shortcuts: readShortcutSettings(this.document.uri), ...(error ? { error } : {}) })
        break
      }
      case 'openShortcutSettings': await vscode.commands.executeCommand('workbench.action.openSettings', 'markleaf.shortcuts'); break
      case 'flushed': {
        const pending = this.flushes.get(message.requestId)
        if (pending) { clearTimeout(pending.timer); this.flushes.delete(message.requestId); pending.resolve(message.success) }
        break
      }
      case 'resolveImages': {
        const urls: Record<string, string> = Object.create(null)
        for (const path of message.paths) {
          const uri = resolveDocumentLink(this.document, path)
          if (uri && !['http', 'https', 'mailto'].includes(uri.scheme)) {
            // Referenced images can live beside another document or outside
            // the workspace. Recreate their directory grants when reopening.
            this.grantImageDirectory(vscode.Uri.joinPath(uri, '..'))
            urls[path] = this.panel.webview.asWebviewUri(uri).toString()
          }
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
  // An active editor tab can remain active while the terminal or sidebar has
  // keyboard focus. The webview reports its own focus, including input fields.
  const updateFocus = (): void => { void vscode.commands.executeCommand('setContext', 'markleaf.focus', active()?.focus ?? null) }
  updateFocus()
  const provider: vscode.CustomTextEditorProvider = {
    async resolveCustomTextEditor(document, panel) {
      const assets = vscode.Uri.joinPath(context.extensionUri, 'dist', 'webview')
      panel.webview.options = { enableScripts: true, localResourceRoots: localResourceRoots(document, assets) }
      const editor = new EditorPanel(document, panel, updateFocus)
      panels.add(editor)
      panel.onDidDispose(() => { panels.delete(editor); editor.dispose() }, undefined, context.subscriptions)
      try { panel.webview.html = await webviewHtml(panel.webview, assets) }
      catch (error) { panels.delete(editor); editor.dispose(); throw error }
    },
  }
  context.subscriptions.push(
    vscode.window.registerCustomEditorProvider(viewType, provider, {
      webviewOptions: { retainContextWhenHidden: true, enableFindWidget: false },
      supportsMultipleEditorsPerDocument: false,
    }),
    vscode.commands.registerCommand('markleaf.open', async (uri?: vscode.Uri) => {
      const resource = uri ?? activeResource()
      if (!resource) { void vscode.window.showInformationMessage('请先打开或选择一个 Markdown 文件。'); return }
      await vscode.commands.executeCommand('vscode.openWith', resource, viewType)
    }),
    ...(['format', 'insertImage', 'insertImageUrl', 'image', 'find', 'replace', 'copyMarkdown', 'copyPlainText', 'copyHtml', 'pastePlainText',
      'toggleOutline', 'toggleFocus', 'toggleTypewriter', 'toggleRead', 'zoomIn', 'zoomOut', 'zoomReset', 'preferences', 'help', 'shortcuts'] as const)
      .map(action => vscode.commands.registerCommand(`markleaf.${action}`, () => active()?.requestAction(action))),
    ...formatActions.map(({ command }) => vscode.commands.registerCommand(`markleaf.${command}`, () => active()?.requestFormatCommand(command))),
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
