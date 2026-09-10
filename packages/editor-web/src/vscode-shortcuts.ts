// VS Code-only actions and keybindings. Native host shortcut catalogs are unchanged.
export const formatGroups: Array<{ label: string; commands: Array<[string, string]> }> = [
  { label: '文字格式', commands: [
    ['粗体', 'toggleBold'], ['斜体', 'toggleItalic'], ['下划线', 'toggleUnderline'],
    ['删除线', 'toggleStrike'], ['高亮', 'toggleHighlight'], ['行内代码', 'toggleCode'],
    ['清除格式', 'clearFormat'], ['格式刷', 'formatPainter'],
  ] },
  { label: '段落与标题', commands: [
    ['正文', 'setParagraph'], ['一级标题', 'setHeading1'], ['二级标题', 'setHeading2'],
    ['三级标题', 'setHeading3'], ['四级标题', 'setHeading4'], ['五级标题', 'setHeading5'],
    ['六级标题', 'setHeading6'], ['提升标题级别', 'promoteHeading'], ['降低标题级别', 'demoteHeading'],
    ['在当前段落前插入', 'insertLineBefore'], ['在当前段落后插入', 'insertLineAfter'],
    ['复制当前段落', 'duplicateParagraph'], ['删除当前段落', 'deleteParagraph'],
  ] },
  { label: '列表与引用', commands: [
    ['无序列表', 'toggleBulletList'], ['有序列表', 'toggleOrderedList'], ['任务列表', 'toggleTaskList'],
    ['增加列表缩进', 'indentListItem'], ['减少列表缩进', 'outdentListItem'],
    ['引用', 'toggleBlockquote'], ['代码块', 'toggleCodeBlock'], ['分隔线', 'insertHorizontalRule'],
  ] },
  { label: '提示框', commands: [
    ['备注 (NOTE)', 'insertAlertNote'], ['提示 (TIP)', 'insertAlertTip'],
    ['重要 (IMPORTANT)', 'insertAlertImportant'], ['警告 (WARNING)', 'insertAlertWarning'],
    ['注意 (CAUTION)', 'insertAlertCaution'],
  ] },
  { label: '表格', commands: [
    ['插入表格…', 'insertTable'], ['上方插入行', 'addRowBefore'], ['下方插入行', 'addRowAfter'],
    ['删除行', 'deleteRow'], ['左侧插入列', 'addColumnBefore'], ['右侧插入列', 'addColumnAfter'],
    ['删除列', 'deleteColumn'], ['当前列左对齐', 'alignTableLeft'], ['当前列居中', 'alignTableCenter'],
    ['当前列右对齐', 'alignTableRight'], ['表格标题…', 'setTableCaption'], ['删除表格', 'deleteTable'],
  ] },
  { label: '公式与图表', commands: [
    ['行内公式', 'insertMathInline'], ['独立公式', 'insertMathBlock'], ['Mermaid 图表', 'insertMermaid'],
    ['编辑公式源码', 'editMath'], ['切换行内 / 独立公式', 'convertMath'], ['公式编号…', 'setMathNumber'], ['删除公式', 'deleteMath'],
    ['编辑图表源码', 'editMermaid'], ['渲染当前 Mermaid', 'updateMermaid'], ['重新渲染当前图表', 'rerenderMermaid'],
    ['重新渲染所有图表', 'rerenderAllMermaid'], ['删除图表', 'deleteMermaid'],
  ] },
  { label: '脚注与元数据', commands: [
    ['插入脚注…', 'insertFootnote'], ['重命名当前脚注…', 'resetFootnoteLabel'],
    ['回到脚注引用', 'goToFootnoteReference'], ['清除当前脚注引用', 'clearFootnoteReferences'],
    ['删除脚注', 'deleteFootnote'], ['显示 / 插入 Front Matter', 'showFrontMatter'],
  ] },
  { label: '代码', commands: [
    ['代码块语言…', 'setCodeBlockLanguage'], ['复制代码', 'copyCodeBlock'], ['退出代码块', 'exitCode'],
  ] },
]

export const formatActions = formatGroups.flatMap(group => group.commands.map(([label, command]) => ({ label, command, group: group.label })))
export const isFormatCommand = (command: unknown): command is string => typeof command === 'string' && formatActions.some(action => action.command === command)

// These are the existing Tiptap format keys. Structural keys (Enter, Tab,
// Backspace, arrows) and VS Code document commands stay with their owners.
export const defaultShortcuts: Readonly<Record<string, string>> = {
  toggleBold: 'Mod+B', toggleItalic: 'Mod+I', toggleUnderline: 'Mod+U', toggleStrike: 'Mod+Shift+S',
  toggleCode: 'Mod+E', setParagraph: 'Mod+Alt+0',
  setHeading1: 'Mod+Alt+1', setHeading2: 'Mod+Alt+2', setHeading3: 'Mod+Alt+3',
  setHeading4: 'Mod+Alt+4', setHeading5: 'Mod+Alt+5', setHeading6: 'Mod+Alt+6',
  toggleBulletList: 'Mod+Shift+8', toggleOrderedList: 'Mod+Shift+7', toggleTaskList: 'Mod+Shift+9',
  toggleBlockquote: 'Mod+Shift+B', toggleCodeBlock: 'Mod+Alt+C',
}
export const formatCommandsWithInput = new Set(['insertTable', 'setTableCaption', 'setMathNumber', 'setCodeBlockLanguage',
  'insertFootnote', 'resetFootnoteLabel', 'goToFootnoteReference'])

export type ShortcutSettings = { overrides: unknown; scope: 'user' | 'workspace' | 'folder'; error?: string }
export type ResolvedShortcuts = { bindings: Record<string, string>; errors: Record<string, string>; byKey: Map<string, string> }
type ParsedShortcut = { binding: string; key: string; error?: never } | { binding?: never; key?: never; error: string }

export function shortcutObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

export function parseShortcut(value: unknown, mac: boolean): ParsedShortcut {
  if (typeof value !== 'string') return { error: '键位必须是字符串；空字符串表示不绑定。' }
  if (value === '') return { binding: '', key: '' }
  const parts = value.split('+').map(part => part.trim().toUpperCase())
  const main = parts.pop() ?? ''
  if (!/^(?:[A-Z0-9]|F(?:[1-9]|1\d|2[0-4]))$/.test(main)) return { error: '使用单组组合键：修饰键 + A–Z、0–9 或 F1–F24。' }
  const aliases: Record<string, string> = { MOD: 'Mod', CTRL: 'Ctrl', CONTROL: 'Ctrl', CMD: 'Cmd', META: 'Cmd', COMMAND: 'Cmd', ALT: 'Alt', OPTION: 'Alt', SHIFT: 'Shift' }
  const modifiers = parts.map(part => aliases[part])
  if (modifiers.some(part => !part) || new Set(modifiers).size !== modifiers.length) return { error: '修饰键可用 Mod、Ctrl、Cmd、Alt、Shift，且不可重复。' }
  const actual = modifiers.map(part => part === 'Mod' ? mac ? 'Cmd' : 'Ctrl' : part)
  if (new Set(actual).size !== actual.length) return { error: 'Mod 与当前平台主修饰键重复。' }
  if (!actual.some(part => ['Ctrl', 'Cmd', 'Alt'].includes(part!)) && !main.startsWith('F')) return { error: '字母与数字必须搭配 Ctrl、Cmd 或 Alt，避免覆盖正常输入。' }
  const ordered = ['Mod', 'Ctrl', 'Cmd', 'Alt', 'Shift'].filter(part => modifiers.includes(part))
  const key = [...['Ctrl', 'Cmd', 'Alt', 'Shift'].filter(part => actual.includes(part)), main].join('+')
  return { binding: [...ordered, main].join('+'), key }
}

// Protect the document commands handled by vscode.ts / package.json, including
// common close/quit commands. Other user-installed/global shortcuts cannot be
// inspected through the public VS Code extension API.
function reservedKeys(mac: boolean): Set<string> {
  const keys = ['Mod+S', 'Mod+Z', 'Mod+Shift+Z', 'Mod+Y', 'Mod+A', 'Mod+C', 'Mod+X', 'Mod+V',
    'Mod+Shift+V', 'Mod+Alt+V', 'Mod+F', 'Mod+W', 'Mod+P', 'Mod+Shift+P', 'F1', 'Alt+F4',
    ...(mac ? ['Cmd+Alt+F', 'Cmd+Q', 'Cmd+H', 'Cmd+M'] : ['Ctrl+H'])]
  return new Set(keys.map(key => parseShortcut(key, mac).key!))
}

export function resolveShortcuts(overrides: unknown, mac: boolean): ResolvedShortcuts {
  const bindings: Record<string, string> = Object.create(null)
  const errors: Record<string, string> = Object.create(null)
  const byKey = new Map<string, string>()
  if (!shortcutObject(overrides)) return { bindings, errors: { settings: 'markleaf.shortcuts 必须是对象，请在 VS Code 设置中修正。' }, byKey }
  const owners = new Map<string, string[]>()
  const reserved = reservedKeys(mac)
  for (const action of formatActions) {
    const value = Object.hasOwn(overrides, action.command) ? overrides[action.command] : defaultShortcuts[action.command] ?? ''
    const parsed = parseShortcut(value, mac)
    if (parsed.error !== undefined) { errors[action.command] = parsed.error; continue }
    bindings[action.command] = parsed.binding
    if (!parsed.key) continue
    if (reserved.has(parsed.key)) { errors[action.command] = '此键位由 VS Code 文档操作或系统使用，请选择其他组合。'; continue }
    owners.set(parsed.key, [...owners.get(parsed.key) ?? [], action.command])
  }
  for (const key of Object.keys(overrides)) if (!isFormatCommand(key)) errors[key] = `未知操作：${key}`
  for (const [key, commands] of owners) {
    if (commands.length === 1) byKey.set(key, commands[0]!)
    else for (const command of commands) errors[command] = `与「${commands.filter(id => id !== command).map(id => formatActions.find(action => action.command === id)!.label).join('、')}」重复，请先清除或改绑。`
  }
  return { bindings, errors, byKey }
}

export function shortcutLabel(value: string, mac: boolean): string {
  if (!value) return ''
  return value.split('+').map(part => part === 'Mod' ? mac ? '⌘' : 'Ctrl'
    : part === 'Cmd' ? mac ? '⌘' : 'Meta' : part === 'Alt' && mac ? '⌥' : part === 'Shift' && mac ? '⇧' : part === 'Ctrl' && mac ? '⌃' : part).join(mac ? '' : '+')
}
