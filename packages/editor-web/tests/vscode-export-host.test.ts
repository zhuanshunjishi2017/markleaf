import { beforeEach, describe, expect, it, vi } from 'vitest'
import type * as vscode from 'vscode'
import { Uri, files, window, workspace } from './vscode-mock'
import { exportDefaults } from '../src/vscode-export-options'
import { defaultSettings } from '../src/vscode-settings'
import { DocumentExports, exportPaths } from '../../../apps/vscode/src/exports'
import { embedExportImages } from '../../../apps/vscode/src/export-resources'
import type { ExtensionMessage } from '../src/vscode-protocol'

vi.mock('../../../apps/vscode/src/export-browser', () => ({ browserExecutable: vi.fn(), exportWithBrowser: vi.fn() }))
vi.mock('vscode', async () => {
  const mock = await import('./vscode-mock')
  return { ...mock, env: { language: 'en' }, ProgressLocation: { Notification: 15 },
    window: { ...mock.window, showWarningMessage: vi.fn(), showInformationMessage: vi.fn(), showErrorMessage: vi.fn(),
      withProgress: vi.fn((_options, task) => task({ report: vi.fn() }, { onCancellationRequested: () => ({ dispose: vi.fn() }) })) },
    workspace: { ...mock.workspace, getConfiguration: () => ({ get: (key: keyof typeof defaultSettings, fallback: unknown) => defaultSettings[key] ?? fallback }),
      fs: { ...mock.workspace.fs, stat: vi.fn(async (uri: Uri) => {
        if (!mock.files.has(uri.toString())) throw Object.assign(new Error('File not found'), { code: 'FileNotFound' })
        return { size: mock.files.get(uri.toString())!.length }
      }) } },
  }
})
function doc(uri = 'file:///project/readme.md'): vscode.TextDocument {
  return { uri: Uri.parse(uri), getText: () => 'Current snapshot', isUntitled: false, isClosed: false } as unknown as vscode.TextDocument
}
beforeEach(() => { files.clear(); vi.clearAllMocks() })

describe('VS Code export ownership and filesystem', () => {
  it('embeds local and remote-workspace images with original URI authority', async () => {
    const document = doc('vscode-remote://ssh-remote+server/project/readme.md')
    files.set('vscode-remote://ssh-remote%2Bserver/project/image.png', new Uint8Array([1]))
    const image = Uri.joinPath(document.uri as unknown as Uri, '..', 'image.png')
    files.set(image.toString(), new Uint8Array([1, 2, 3]))
    const result = await embedExportImages(document, { html: '<img src="markleaf-export-image:0">', images: ['./image.png'] }, new AbortController().signal, 'en')
    expect(result).toBe('<img src="data:image/png;base64,AQID">')
  })
  it('propagates missing image and cancellation without empty-image substitution', async () => {
    await expect(embedExportImages(doc(), { html: '<img src="markleaf-export-image:0">', images: ['./missing.png'] }, new AbortController().signal, 'en')).rejects.toThrow(/Unable to embed image.*missing.png/)
    const abort = new AbortController(); abort.abort()
    await expect(embedExportImages(doc(), { html: '', images: ['./image.png'] }, abort.signal, 'en')).rejects.toBeTruthy()
  })
  it('uses the synchronized TextDocument snapshot, ignores late responses and persists options only after output succeeds', async () => {
    const state = { get: vi.fn(), update: vi.fn() }
    const messages: ExtensionMessage[] = []
    let owner: DocumentExports
    const flush = vi.fn(async () => true)
    owner = new DocumentExports(doc(), state as unknown as vscode.Memento, message => {
      messages.push(message)
      if (message.type === 'renderExport') queueMicrotask(() => owner.rendered({ type: 'exportRendered', requestId: message.requestId, result: { html: '<html>Full document</html>', images: [] } }))
    }, flush)
    const target = Uri.file('/project/export.html')
    window.showSaveDialog.mockResolvedValueOnce(target)
    await owner.start({ ...exportDefaults, format: 'html' }, false)
    expect(flush).toHaveBeenCalledOnce()
    expect(messages.find(message => message.type === 'renderExport')).toMatchObject({ markdown: 'Current snapshot' })
    expect(Buffer.from(files.get(target.toString())!).toString()).toBe('<html>Full document</html>')
    expect(state.update).toHaveBeenCalledWith('markleaf.exportOptions', { ...exportDefaults, format: 'html' })
    const writes = workspace.fs.writeFile.mock.calls.length
    owner.rendered({ type: 'exportRendered', requestId: 1, result: { html: 'late', images: [] } })
    expect(workspace.fs.writeFile.mock.calls.length).toBe(writes)
    owner.dispose()
  })
  it('does not write or save options when synchronization fails or the view is closed during preparation', async () => {
    const state = { get: vi.fn(), update: vi.fn() }
    const target = Uri.file('/project/export.html')
    const messages: ExtensionMessage[] = []
    const failure = new DocumentExports(doc(), state as unknown as vscode.Memento, message => messages.push(message), async () => false)
    window.showSaveDialog.mockResolvedValueOnce(target)
    await failure.start({ ...exportDefaults, format: 'html' }, false)
    expect(messages.at(-1)).toMatchObject({ type: 'exportFinished', error: true })
    expect(workspace.fs.writeFile).not.toHaveBeenCalled()
    const closing = new DocumentExports(doc(), state as unknown as vscode.Memento, message => { if (message.type === 'renderExport') closing.dispose() }, async () => true)
    window.showSaveDialog.mockResolvedValueOnce(target)
    await closing.start({ ...exportDefaults, format: 'html' }, false)
    expect(workspace.fs.writeFile).not.toHaveBeenCalled()
    expect(state.update).not.toHaveBeenCalled()
    failure.dispose()
  })
  it('keeps split-image paths in the selected directory and preserves the single-image filename', () => {
    const target = Uri.file('/project/report.png') as unknown as vscode.Uri
    expect(exportPaths(target, 1)).toEqual([target])
    expect(exportPaths(target, 3).map(uri => uri.path)).toEqual(['/project/report-01.png', '/project/report-02.png', '/project/report-03.png'])
  })
})
