import { afterEach, describe, expect, it, vi } from 'vitest'
import { readShortcutSettings, updateShortcut } from '../../src/settings'
import { pickFormat, prepareFormatCommand } from '../../src/formatting'
import { Uri, window } from './vscode-mock'

const config = vi.hoisted(() => ({
  user: {} as Record<string, unknown>, workspace: undefined as Record<string, unknown> | undefined,
  folder: undefined as Record<string, unknown> | undefined, registered: true, update: vi.fn(),
}))
vi.mock('vscode', async () => {
  const base = await vi.importActual<typeof import('./vscode-mock')>('vscode')
  return { ...base, ConfigurationTarget: { Global: 1, Workspace: 2, WorkspaceFolder: 3 }, workspace: {
    ...base.workspace,
    getConfiguration: () => ({
      get: () => ({ ...config.user, ...config.workspace, ...config.folder }),
      inspect: () => ({ ...(config.registered ? { defaultValue: {} } : {}), globalValue: config.user, workspaceValue: config.workspace, workspaceFolderValue: config.folder }),
      update: config.update,
    }),
  } }
})
const uri = Uri.file('/project/doc.md')
afterEach(() => { config.user = {}; config.workspace = undefined; config.folder = undefined; config.registered = true; vi.resetAllMocks() })

describe('VS Code shortcut configuration and format command inputs', () => {
  it('explains missing workbench registration, blocks the write, and saves after registration is refreshed', async () => {
    config.registered = false
    // A user JSON entry alone does not mean the extension schema was loaded.
    config.user = { toggleBold: 'Mod+Alt+B' }
    expect(readShortcutSettings(uri as never).error).toContain('Developer: Reload Window')
    await expect(updateShortcut(uri as never, 'insertMathBlock', 'Mod+Shift+L', true)).rejects.toThrow('markleaf.shortcuts')
    expect(config.update).not.toHaveBeenCalled()
    config.registered = true
    expect(readShortcutSettings(uri as never).error).toBeUndefined()
    await updateShortcut(uri as never, 'insertMathBlock', 'Mod+Shift+L', true)
    expect(config.update).toHaveBeenLastCalledWith('shortcuts', { toggleBold: 'Mod+Alt+B', insertMathBlock: 'Mod+Shift+L' }, 1)
  })

  it('patches the current target without copying inherited settings and serializes multiple panel writes', async () => {
    config.user = { toggleBold: 'Mod+Alt+B' }
    config.workspace = { insertMathInline: 'Mod+Alt+M' }
    config.update.mockImplementation(async (_key, value, target) => {
      expect(target).toBe(2)
      config.workspace = value
    })
    await Promise.all([updateShortcut(uri as never, 'insertMathBlock', 'Mod+Alt+Shift+M', true), updateShortcut(uri as never, 'setHeading1', 'Mod+Alt+H', true)])
    expect(config.workspace).toEqual({ insertMathInline: 'Mod+Alt+M', insertMathBlock: 'Mod+Alt+Shift+M', setHeading1: 'Mod+Alt+H' })
    expect(readShortcutSettings(uri as never)).toEqual({ scope: 'workspace', overrides: { ...config.user, ...config.workspace } })
    config.folder = {}
    config.update.mockResolvedValueOnce(undefined)
    await updateShortcut(uri as never, 'toggleItalic', '', true)
    expect(config.update).toHaveBeenLastCalledWith('shortcuts', { toggleItalic: '' }, 3)
  })

  it('rejects collisions and propagates write failures; a later valid save still works', async () => {
    await expect(updateShortcut(uri as never, 'insertMathInline', 'Mod+B', false)).rejects.toThrow('粗体')
    expect(config.update).not.toHaveBeenCalled()
    config.update.mockRejectedValueOnce(new Error('Permission denied'))
    await expect(updateShortcut(uri as never, 'insertMathInline', 'Mod+Alt+M', false)).rejects.toThrow('Permission denied')
    config.update.mockResolvedValueOnce(undefined)
    await updateShortcut(uri as never, 'insertMathInline', 'Mod+Alt+M', false)
    expect(config.update).toHaveBeenLastCalledWith('shortcuts', { insertMathInline: 'Mod+Alt+M' }, 1)
  })

  it('shows the configured platform key in the format menu and reuses the same parameter dialogs', async () => {
    window.showQuickPick.mockImplementationOnce(items => {
      const math = items.find((item: { command?: string }) => item.command === 'insertMathInline')
      expect(math.description).toBe('⌘⌥M')
      return math
    })
    expect(await pickFormat({ actions: { insertMathInline: { enabled: true, checked: false } } }, false, { insertMathInline: 'Mod+Alt+M' }, true)).toEqual({ command: 'insertMathInline' })
    window.showInputBox.mockResolvedValueOnce('2,4')
    expect(await prepareFormatCommand('insertTable', { actions: { insertTable: { enabled: true, checked: false } } })).toEqual({ command: 'insertTable', text: '2,4' })
    expect(await prepareFormatCommand('deleteTable')).toBeUndefined()
    expect(await prepareFormatCommand('arbitraryCommand')).toBeUndefined()
  })
})
