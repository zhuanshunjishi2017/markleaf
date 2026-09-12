import * as vscode from 'vscode'
import { defaultSettings, colorThemes, typographyStyles, type MarkLeafSettings } from '../webview/src/vscode-settings'
import { isFormatCommand, parseShortcut, resolveShortcuts, shortcutObject, type ShortcutSettings } from '../webview/src/vscode-shortcuts'

function shortcutRegistrationError(config: vscode.WorkspaceConfiguration): string | undefined {
  // Reading with a fallback does not mean the workbench registered the setting.
  // An old window may retain its registry after only the extension host restarts.
  if (config.inspect('shortcuts')?.defaultValue !== undefined) return
  return '当前 VS Code 窗口尚未注册 markleaf.shortcuts。请先保存文档，再从命令面板运行 Developer: Reload Window（开发人员: 重新加载窗口），然后重新录入并保存。请重新加载整个窗口，而不只是重启扩展宿主。'
}

function shortcutScope(config: vscode.WorkspaceConfiguration) {
  const inspected = config.inspect<Record<string, unknown>>('shortcuts')
  if (inspected?.workspaceFolderValue !== undefined) return { scope: 'folder' as const, target: vscode.ConfigurationTarget.WorkspaceFolder, values: inspected.workspaceFolderValue }
  if (inspected?.workspaceValue !== undefined) return { scope: 'workspace' as const, target: vscode.ConfigurationTarget.Workspace, values: inspected.workspaceValue }
  return { scope: 'user' as const, target: vscode.ConfigurationTarget.Global, values: inspected?.globalValue ?? {} }
}

export function readShortcutSettings(uri: vscode.Uri): ShortcutSettings {
  const config = vscode.workspace.getConfiguration('markleaf', uri)
  const error = shortcutRegistrationError(config)
  return { overrides: config.get<unknown>('shortcuts', {}), scope: shortcutScope(config).scope, ...(error ? { error } : {}) }
}

// All panels patch one setting through this queue, reading the latest scope at
// the time of the write so another panel's previous change is not overwritten.
let shortcutWrite: Promise<void> = Promise.resolve()
export function updateShortcut(uri: vscode.Uri, command: string, binding: string, mac: boolean): Promise<void> {
  const write = shortcutWrite.then(async () => {
    if (!isFormatCommand(command)) throw new Error('未知格式操作。')
    const parsed = parseShortcut(binding, mac)
    if (parsed.error !== undefined) throw new Error(parsed.error)
    const config = vscode.workspace.getConfiguration('markleaf', uri)
    const registrationError = shortcutRegistrationError(config)
    if (registrationError) throw new Error(registrationError)
    const effective = config.get<unknown>('shortcuts', {})
    const { target, values } = shortcutScope(config)
    if (!shortcutObject(effective) || !shortcutObject(values)) throw new Error('markleaf.shortcuts 必须是对象，请在 VS Code 设置中修正。')
    const next = { ...effective, [command]: parsed.binding }
    const error = resolveShortcuts(next, mac).errors[command]
    if (error) throw new Error(error)
    await config.update('shortcuts', { ...values, [command]: parsed.binding }, target)
  })
  shortcutWrite = write.catch(() => {})
  return write
}

export function readSettings(uri: vscode.Uri): MarkLeafSettings {
  const config = vscode.workspace.getConfiguration('markleaf', uri)
  return Object.fromEntries(Object.entries(defaultSettings).map(([key, value]) => [key, config.get(key, value)])) as MarkLeafSettings
}

export async function updateSetting(uri: vscode.Uri, key: keyof MarkLeafSettings, value: unknown): Promise<void> {
  if (!Object.hasOwn(defaultSettings, key) || typeof value !== typeof defaultSettings[key]) throw new Error('无效的 MarkLeaf 设置。')
  const config = vscode.workspace.getConfiguration('markleaf', uri)
  const scope = config.inspect(key)
  const target = scope?.workspaceFolderValue !== undefined ? vscode.ConfigurationTarget.WorkspaceFolder
    : scope?.workspaceValue !== undefined ? vscode.ConfigurationTarget.Workspace : vscode.ConfigurationTarget.Global
  await config.update(key, value, target)
}

export async function pickPreferences(uri: vscode.Uri): Promise<void> {
  const settings = readSettings(uri)
  type Item = vscode.QuickPickItem & { key?: keyof MarkLeafSettings; shortcuts?: boolean }
  const items: Item[] = [
    { label: '快捷键…', description: '录入格式操作键位', shortcuts: true },
    { label: '排版样式…', key: 'typography', description: settings.typography },
    { label: '配色主题…', key: 'colorTheme', description: settings.colorTheme },
    { label: '字号…', key: 'fontSize', description: String(settings.fontSize) },
    ...([['showOutline', '大纲侧栏'], ['focusMode', '专注当前段落'], ['typewriterMode', '打字机滚动'],
      ['showCodeHighlight', '代码高亮'], ['showBlockHandle', '段落操作柄'], ['showStatusBar', '状态栏'],
      ['cjkAutoSpacing', '中西文自动间距'], ['ignoreMaxWidth', '使用全部内容宽度']] as const)
      .map(([key, label]) => ({ label: `${settings[key] ? '$(check)' : '$(circle-large-outline)'} ${label}`, key })),
    { label: '全部 MarkLeaf 设置…' },
  ]
  const choice = await vscode.window.showQuickPick(items, { title: 'MarkLeaf 阅读与编辑设置' })
  if (!choice) return
  if (choice.shortcuts) { await vscode.commands.executeCommand('markleaf.shortcuts'); return }
  const key = choice.key
  if (!key) { await vscode.commands.executeCommand('workbench.action.openSettings', '@ext:markleaf.markleaf'); return }
  if (typeof settings[key] === 'boolean') { await updateSetting(uri, key, !settings[key]); return }
  if (key === 'typography' || key === 'colorTheme') {
    const value = await vscode.window.showQuickPick(key === 'typography' ? [...typographyStyles] : [...colorThemes], { title: choice.label })
    if (value) await updateSetting(uri, key, value)
  } else if (key === 'fontSize') {
    const value = await vscode.window.showInputBox({ prompt: '文档字号（10–32 px）', value: String(settings.fontSize),
      validateInput: value => Number.isFinite(+value) && +value >= 10 && +value <= 32 ? undefined : '字号范围为 10–32' })
    if (value !== undefined) await updateSetting(uri, key, +value)
  }
}
