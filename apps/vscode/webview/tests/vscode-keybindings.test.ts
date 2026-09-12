import { afterEach, describe, expect, it, vi } from 'vitest'
import type * as vscode from 'vscode'
import type { ExtensionMessage, WebviewMessage } from '../src/vscode-protocol'
import { isWebviewMessage } from '../src/vscode-protocol'
import { activate } from '../../src/extension'

const host = vi.hoisted(() => ({
  commands: new Map<string, (...args: unknown[]) => unknown>(),
  execute: vi.fn(async (..._args: unknown[]) => {}),
  provider: undefined as vscode.CustomTextEditorProvider | undefined,
  textEditor: undefined as vscode.TextEditor | undefined,
}))

vi.mock('vscode', async () => {
  const base = await vi.importActual<typeof import('./vscode-mock')>('vscode')
  const subscribe = () => ({ dispose() {} })
  return {
    ...base,
    ConfigurationTarget: { Global: 1, Workspace: 2, WorkspaceFolder: 3 },
    ViewColumn: { Beside: -2 },
    commands: {
      executeCommand: host.execute,
      registerCommand: (id: string, callback: (...args: unknown[]) => unknown) => {
        host.commands.set(id, callback)
        return { dispose: () => host.commands.delete(id) }
      },
    },
    workspace: {
      ...base.workspace,
      onDidChangeTextDocument: subscribe,
      onWillSaveTextDocument: subscribe,
      onDidChangeConfiguration: subscribe,
      getConfiguration: () => ({ get: (_key: string, fallback: unknown) => fallback, inspect: () => undefined }),
      fs: { ...base.workspace.fs, isWritableFileSystem: () => true },
    },
    window: {
      ...base.window,
      get activeTextEditor() { return host.textEditor },
      registerCustomEditorProvider: (_id: string, provider: vscode.CustomTextEditorProvider) => {
        host.provider = provider
        return { dispose() {} }
      },
      showErrorMessage: vi.fn(),
    },
    env: { language: 'en' },
  }
})
vi.mock('../../src/webview', () => ({ webviewHtml: async () => '<html></html>' }))

const subscriptions: vscode.Disposable[] = []
afterEach(() => {
  subscriptions.splice(0).reverse().forEach(item => item.dispose())
  host.textEditor = undefined
  host.provider = undefined
  vi.clearAllMocks()
})

describe('VS Code shortcut command and focus boundary', () => {
  it('validates the webview focus targets used by keybinding contexts', () => {
    for (const target of ['document', 'input', null]) expect(isWebviewMessage({ type: 'focus', target })).toBe(true)
    for (const target of [true, 'terminal', undefined]) expect(isWebviewMessage({ type: 'focus', target })).toBe(false)
  })

  it('switches both ways, waits for pending text, and releases focus on blur, tab changes and disposal', async () => {
    const { Uri } = await import('vscode')
    const uri = Uri.file('/project/readme.md')
    const document = { uri, languageId: 'markdown', version: 1, getText: () => '# Title' } as vscode.TextDocument
    activate({ extensionUri: Uri.file('/extension'), subscriptions } as vscode.ExtensionContext)
    const toggle = host.commands.get('markleaf.toggleEditor')!
    host.textEditor = { document } as vscode.TextEditor
    await toggle()
    expect(host.execute).toHaveBeenLastCalledWith('vscode.openWith', uri, 'markleaf.editor')

    let receive!: (message: WebviewMessage) => void
    let changed!: () => void
    let disposed!: () => void
    const messages: ExtensionMessage[] = []
    const subscribe = () => ({ dispose() {} })
    const panel = {
      active: true, viewColumn: 1,
      onDidChangeViewState: (callback: () => void) => { changed = callback; return subscribe() },
      onDidDispose: (callback: () => void) => { disposed = callback; return subscribe() },
      webview: {
        options: {}, html: '',
        onDidReceiveMessage: (callback: typeof receive) => { receive = callback; return subscribe() },
        postMessage: async (message: ExtensionMessage) => { messages.push(message); return true },
      },
    }
    await host.provider!.resolveCustomTextEditor(document, panel as unknown as vscode.WebviewPanel, {} as vscode.CancellationToken)
    receive({ type: 'ready' })
    await vi.waitFor(() => expect(messages.some(message => message.type === 'document')).toBe(true))
    await host.commands.get('markleaf.insertMathInline')!()
    expect(messages.at(-1)).toEqual({ type: 'requestFormatCommand', command: 'insertMathInline' })
    await host.commands.get('markleaf.shortcuts')!()
    expect(messages.at(-1)).toEqual({ type: 'requestAction', action: 'shortcuts' })
    for (const target of ['document', 'input', null] as const) {
      receive({ type: 'focus', target })
      expect(host.execute).toHaveBeenLastCalledWith('setContext', 'markleaf.focus', target)
    }

    receive({ type: 'focus', target: 'document' })
    host.execute.mockClear()
    const switching = toggle()
    const flush = messages.at(-1)!
    expect(flush.type).toBe('flush')
    expect(host.execute).not.toHaveBeenCalled()
    if (flush.type !== 'flush') throw new Error('Expected a flush request')
    receive({ type: 'flushed', requestId: flush.requestId, success: true })
    await switching
    expect(host.execute).toHaveBeenLastCalledWith('vscode.openWith', uri, 'default', { viewColumn: 1 })

    host.execute.mockClear()
    const blocked = toggle()
    const unsynced = messages.at(-1)!
    if (unsynced.type !== 'flush') throw new Error('Expected a flush request')
    receive({ type: 'flushed', requestId: unsynced.requestId, success: false })
    await blocked
    expect(host.execute).not.toHaveBeenCalled()

    panel.active = false
    changed()
    expect(host.execute).toHaveBeenLastCalledWith('setContext', 'markleaf.focus', null)
    receive({ type: 'focus', target: 'document' })
    expect(host.execute).toHaveBeenLastCalledWith('setContext', 'markleaf.focus', null)
    panel.active = true
    changed()
    expect(host.execute).toHaveBeenLastCalledWith('setContext', 'markleaf.focus', null)
    receive({ type: 'focus', target: 'document' })
    disposed()
    expect(host.execute).toHaveBeenLastCalledWith('setContext', 'markleaf.focus', null)
  })
})
