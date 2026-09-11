// In-memory VS Code API boundary. No user files, dialogs or clipboard are used.
import { posix } from 'node:path'
import { vi } from 'vitest'

export class Uri {
  constructor(readonly scheme: string, readonly authority: string, readonly path: string, readonly query = '', readonly fragment = '') {}
  static parse(value: string): Uri {
    const uri = new URL(value)
    return new Uri(uri.protocol.slice(0, -1), uri.hostname, decodeURIComponent(uri.pathname), uri.search.slice(1), uri.hash.slice(1))
  }
  static file(path: string): Uri { return new Uri('file', '', path) }
  static joinPath(uri: Uri, ...parts: string[]): Uri { return uri.with({ path: posix.join(uri.path, ...parts) }) }
  with(change: Partial<Pick<Uri, 'scheme' | 'authority' | 'path' | 'query' | 'fragment'>>): Uri {
    return new Uri(change.scheme ?? this.scheme, change.authority ?? this.authority, change.path ?? this.path, change.query ?? this.query, change.fragment ?? this.fragment)
  }
  toString(): string { return `${this.scheme}://${this.authority}${this.path.split('/').map(encodeURIComponent).join('/')}${this.query ? `?${this.query}` : ''}${this.fragment ? `#${this.fragment}` : ''}` }
}
export const files = new Map<string, Uint8Array>()
export const workspace = {
  workspaceFolders: undefined as Array<{ uri: Uri }> | undefined,
  fs: {
    readFile: vi.fn(async (uri: Uri) => {
      const content = files.get(uri.toString())
      if (!content) throw new Error('File not found')
      return content
    }),
    writeFile: vi.fn(async (uri: Uri, data: Uint8Array) => { files.set(uri.toString(), data) }),
    createDirectory: vi.fn(async () => {}),
  },
}
export const window = { showQuickPick: vi.fn(), showInputBox: vi.fn(), showOpenDialog: vi.fn(), showSaveDialog: vi.fn() }
export const QuickPickItemKind = { Separator: -1 }
