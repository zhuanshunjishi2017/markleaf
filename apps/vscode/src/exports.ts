import * as vscode from 'vscode'
import { basename, extname } from 'node:path'
import { homedir } from 'node:os'
import { readSettings } from './settings'
import { browserExecutable, exportWithBrowser } from './export-browser'
import { embedExportImages } from './export-resources'
import { exportDefaults, isExportOptions, type ExportFormat, type ExportHtmlResult, type ExportOptions } from '../webview/src/vscode-export-options'
import { exportStrings } from '../webview/src/vscode-export-strings'
import type { ExtensionMessage, WebviewMessage } from '../webview/src/vscode-protocol'

type Rendered = Extract<WebviewMessage, { type: 'exportRendered' }>
type Pending = { requestId: number; resolve(result: ExportHtmlResult): void; reject(error: unknown): void }

export function exportPaths(target: vscode.Uri, count: number): vscode.Uri[] {
  if (count === 1) return [target]
  const extension = extname(target.path)
  const stem = target.path.slice(0, -extension.length)
  return Array.from({ length: count }, (_, index) => target.with({ path: `${stem}-${String(index + 1).padStart(2, '0')}${extension}` }))
}

export class DocumentExports implements vscode.Disposable {
  private controller?: AbortController
  private requestId = 0
  private pending?: Pending
  private disposed = false
  constructor(private readonly document: vscode.TextDocument, private readonly state: vscode.Memento,
    private readonly post: (message: ExtensionMessage) => void, private readonly flush: () => Promise<boolean>) {}

  private options(): ExportOptions {
    const saved = this.state.get<unknown>('markleaf.exportOptions')
    return isExportOptions(saved) ? { ...saved } : { ...exportDefaults }
  }
  open(format?: ExportFormat): void {
    this.post({ type: 'exportOptions', options: { ...this.options(), ...(format ? { format } : {}) } })
  }
  last(): Promise<void> { return this.start(this.options(), false) }
  cancel(): void { this.controller?.abort() }
  dispose(): void { this.disposed = true; this.cancel(); this.pending?.reject(new Error(exportStrings(vscode.env.language).closed)) }
  rendered(message: Rendered): void {
    if (!this.pending || this.pending.requestId !== message.requestId) return
    if (message.error !== undefined) this.pending.reject(new Error(message.error))
    else if (message.result) this.pending.resolve(message.result)
  }

  private async snapshot(options: ExportOptions, signal: AbortSignal): Promise<ExportHtmlResult> {
    const s = exportStrings(vscode.env.language)
    if (!await this.flush()) throw new Error(s.unsynced)
    signal.throwIfAborted()
    if (this.disposed || this.document.isClosed) throw new Error(s.closed)
    return new Promise((resolve, reject) => {
      const requestId = ++this.requestId
      const abort = (): void => finish(undefined, signal.reason)
      const timer = setTimeout(() => finish(undefined, new Error(s.timeout)), 90000)
      const finish = (result?: ExportHtmlResult, error?: unknown): void => {
        clearTimeout(timer)
        signal.removeEventListener('abort', abort)
        if (this.pending?.requestId === requestId) this.pending = undefined
        if (result) resolve(result); else reject(error)
      }
      this.pending = { requestId, resolve: result => finish(result), reject: error => finish(undefined, error) }
      signal.addEventListener('abort', abort, { once: true })
      this.post({ type: 'renderExport', requestId, markdown: this.document.getText(), title: basename(this.document.uri.path, extname(this.document.uri.path)),
        options, settings: readSettings(this.document.uri), language: vscode.env.language })
    })
  }

  private async executable(): Promise<string | undefined> {
    const s = exportStrings(vscode.env.language)
    const config = vscode.workspace.getConfiguration('markleaf')
    const configured = config.get<string>('exportBrowserPath', '')
    const path = await browserExecutable(configured)
    if (path) return path
    const action = await vscode.window.showWarningMessage(configured ? `${s.browserInvalid}: ${configured}` : s.missingBrowser, s.chooseBrowser)
    if (action !== s.chooseBrowser) return undefined
    const selected = await vscode.window.showInputBox({ prompt: s.browserPath, value: configured, ignoreFocusOut: true,
      validateInput: async value => await browserExecutable(value.trim()) && value.trim() ? undefined : s.browserInvalid })
    if (!selected) return undefined
    const executable = await browserExecutable(selected.trim())
    if (!executable) throw new Error(s.browserInvalid)
    await config.update('exportBrowserPath', executable, vscode.ConfigurationTarget.Global)
    return executable
  }

  async start(options: ExportOptions, preview: boolean): Promise<void> {
    const language = vscode.env.language
    const s = exportStrings(language)
    if (this.disposed) return
    if (this.controller) { this.post({ type: 'exportFinished', message: s.busy, error: true }); return }
    if (!isExportOptions(options)) { this.post({ type: 'exportFinished', message: s.invalid, error: true }); return }
    const controller = this.controller = new AbortController()
    const signal = controller.signal
    const written: vscode.Uri[] = []
    let message = s.cancelled
    let failed = false
    try {
      if ((preview || options.format === 'print') && vscode.env.remoteName) throw new Error(s.remotePrint)
      let target: vscode.Uri | undefined
      if (!preview && options.format !== 'print') {
        const source = this.document.uri
        const directory = ['file', 'vscode-remote'].includes(source.scheme) && !this.document.isUntitled
          ? vscode.Uri.joinPath(source, '..') : vscode.workspace.workspaceFolders?.[0]?.uri ?? vscode.Uri.file(homedir())
        const title = basename(source.path, extname(source.path)) || 'MarkLeaf'
        target = await vscode.window.showSaveDialog({ defaultUri: vscode.Uri.joinPath(directory, `${title}.${options.format}`),
          filters: { [options.format.toUpperCase()]: [options.format] }, saveLabel: s.save })
        if (!target) return
      }
      signal.throwIfAborted()
      const needsBrowser = preview || options.format !== 'html'
      const executablePath = needsBrowser ? await this.executable() : undefined
      if (needsBrowser && !executablePath) return
      signal.throwIfAborted()
      await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `MarkLeaf · ${s.title}`, cancellable: true }, async (progress, token) => {
        const cancellation = token.onCancellationRequested(() => controller.abort())
        if (token.isCancellationRequested) controller.abort()
        try {
          progress.report({ message: s.preparing })
          const snapshot = await this.snapshot(options, signal)
          const html = await embedExportImages(this.document, snapshot, signal, language)
          signal.throwIfAborted()
          let paths: vscode.Uri[] = []
          const prepareFiles = async (count: number): Promise<void> => {
            paths = exportPaths(target!, count)
            // Save As confirmed only the selected path, not derived split names.
            if (count > 1) {
              const existing: string[] = []
              for (const path of paths) {
                try { await vscode.workspace.fs.stat(path); existing.push(path.fsPath || path.path) }
                catch (error) { if ((error as { code?: string }).code !== 'FileNotFound') throw error }
              }
              if (existing.length && await vscode.window.showWarningMessage(`${s.exists}:\n${existing.join('\n')}`, { modal: true }, s.overwrite) !== s.overwrite) controller.abort()
            }
            signal.throwIfAborted()
          }
          const writeFile = async (bytes: Uint8Array, index: number): Promise<void> => {
            signal.throwIfAborted()
            progress.report({ message: `${s.writing} ${index + 1}/${paths.length}` })
            await vscode.workspace.fs.writeFile(paths[index]!, bytes)
            written.push(paths[index]!)
          }
          if (!needsBrowser) {
            await prepareFiles(1)
            await writeFile(Buffer.from(html, 'utf8'), 0)
          } else {
            await exportWithBrowser({ html, options, executablePath: executablePath!, signal, language, preview,
              report: message => progress.report({ message }), prepareFiles, writeFile })
          }
          signal.throwIfAborted()
          message = preview ? s.preview : options.format === 'print' ? s.printClosed : `${s.completed}:\n${written.map(path => path.fsPath || path.path).join('\n')}`
          if (!preview && options.format !== 'print') await this.state.update('markleaf.exportOptions', options)
        } finally { cancellation.dispose() }
      })
    } catch (error) {
      failed = !signal.aborted
      message = signal.aborted ? s.cancelled : error instanceof Error ? error.message : String(error)
      if (written.length) message += `\n${s.partial}:\n${written.map(path => path.fsPath || path.path).join('\n')}`
    } finally {
      this.controller = undefined
      if (!this.disposed) this.post({ type: 'exportFinished', message, error: failed })
    }
    if (this.disposed) return
    if (failed) { void vscode.window.showErrorMessage(`MarkLeaf: ${message}`); return }
    if (written.length && !signal.aborted) {
      const action = await vscode.window.showInformationMessage(message, s.open, s.reveal)
      if (action === s.reveal) await vscode.commands.executeCommand('revealFileInOS', written[0])
      else if (action === s.open) {
        if (written[0]!.scheme === 'file') await vscode.env.openExternal(written[0]!)
        else await vscode.commands.executeCommand('vscode.open', written[0])
      }
    }
  }
}
