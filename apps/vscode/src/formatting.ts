import * as vscode from 'vscode'
import { formatGroups, resolveShortcuts, shortcutLabel } from '../webview/src/vscode-shortcuts'
import type { ActionContext, EditorCommand } from '../webview/src/vscode-protocol'

type FormatItem = vscode.QuickPickItem & { command?: string }


export function applicable(command: string, context: ActionContext): boolean {
  return context.actions?.[command]?.enabled === true
}

export async function pickFormat(context: ActionContext = {}, block = false, shortcuts: unknown = {}, mac = false): Promise<EditorCommand | undefined> {
  const resolved = resolveShortcuts(shortcuts, mac)
  const items: FormatItem[] = formatGroups.flatMap(group => {
    const commands = group.commands.filter(([, command]) => applicable(command, context))
    return commands.length ? [
      { label: group.label, kind: vscode.QuickPickItemKind.Separator },
      ...commands.map(([label, command]) => ({ label, command, description: resolved.errors[command] ? '快捷键配置无效' : shortcutLabel(resolved.bindings[command] ?? '', mac) || '未绑定' })),
    ] : []
  })
  const picked = await vscode.window.showQuickPick(items, {
    title: block ? 'MarkLeaf 当前段落操作' : 'MarkLeaf 格式与段落操作',
    placeHolder: '选择操作，或输入名称搜索', matchOnDescription: true,
  })
  if (!picked?.command) return
  return prepareFormatCommand(picked.command, context)
}

export async function prepareFormatCommand(command: string, context: ActionContext = {}): Promise<EditorCommand | undefined> {
  if (!formatGroups.some(group => group.commands.some(([, id]) => id === command)) || !applicable(command, context)) return
  let text: string | undefined
  const input = (prompt: string, value = '', validateInput?: (value: string) => string | undefined) =>
    vscode.window.showInputBox({ prompt, value, validateInput, ignoreFocusOut: true })
  switch (command) {
    case 'insertTable': {
      const size = await input('行数,列数（各 1–100，第一行为表头）', '3,3', value =>
        /^\d+\s*[,，x×]\s*\d+$/.test(value.trim()) && value.trim().split(/\s*[,，x×]\s*/).every(n => +n >= 1 && +n <= 100)
          ? undefined : '请输入 1–100 的整数，例如 3,4')
      if (size === undefined) return
      text = size.trim().split(/\s*[,，x×]\s*/).join(',')
      break
    }
    case 'setTableCaption': text = await input('表格标题（留空清除）', context.caption ?? ''); break
    case 'setMathNumber': text = await input('公式编号（留空清除）', context.mathNumber ?? ''); break
    case 'setCodeBlockLanguage': text = await input('代码块语言（留空为纯文本）', context.codeBlockLanguage ?? ''); break
    case 'insertFootnote': {
      const labels = context.footnoteLabels ?? []
      let suggested = 1
      while (labels.includes(String(suggested))) suggested++
      const label = await input('脚注标签', String(suggested), value => validateFootnoteLabel(value, labels))
      if (label === undefined) return
      const note = await input('脚注内容', '', value => value.trim() ? undefined : '请输入脚注内容')
      if (note === undefined) return
      text = JSON.stringify({ label: label.trim(), note })
      break
    }
    case 'resetFootnoteLabel': {
      const oldLabel = context.footnoteDefinitionLabel!
      const newLabel = await input('新的脚注标签', oldLabel, value => validateFootnoteLabel(value, (context.footnoteLabels ?? []).filter(label => label !== oldLabel)))
      if (newLabel === undefined) return
      if (newLabel.trim() === oldLabel) return
      text = JSON.stringify({ oldLabel, newLabel: newLabel.trim() })
      break
    }
    case 'goToFootnoteReference': text = context.footnoteDefinitionLabel ?? undefined; break
  }
  if (['setTableCaption', 'setMathNumber', 'setCodeBlockLanguage'].includes(command) && text === undefined) return
  return { command, ...(text === undefined ? {} : { text }) }
}

function validateFootnoteLabel(value: string, labels: string[]): string | undefined {
  if (!value.trim() || /[\s\[\]:]/.test(value.trim())) return '标签不能为空，也不能含空白、方括号或冒号'
  if (labels.includes(value.trim())) return '此标签已经存在'
  return undefined
}
