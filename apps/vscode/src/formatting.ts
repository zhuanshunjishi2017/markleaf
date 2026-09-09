import * as vscode from 'vscode'

type FormatItem = vscode.QuickPickItem & { command?: string }

const groups: Array<{ label: string; commands: Array<[string, string]> }> = [
  { label: '文字格式', commands: [
    ['粗体', 'toggleBold'], ['斜体', 'toggleItalic'], ['下划线', 'toggleUnderline'],
    ['删除线', 'toggleStrike'], ['高亮', 'toggleHighlight'], ['行内代码', 'toggleCode'],
    ['清除格式', 'clearFormat'],
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
    ['插入表格 (3 × 3)', 'insertTable'], ['上方插入行', 'addRowBefore'], ['下方插入行', 'addRowAfter'],
    ['删除行', 'deleteRow'], ['左侧插入列', 'addColumnBefore'], ['右侧插入列', 'addColumnAfter'],
    ['删除列', 'deleteColumn'], ['删除表格', 'deleteTable'],
  ] },
  { label: '公式与图表', commands: [
    ['行内公式', 'insertMathInline'], ['独立公式', 'insertMathBlock'], ['Mermaid 图表', 'insertMermaid'],
  ] },
]

export async function pickFormat(): Promise<string | undefined> {
  const items: FormatItem[] = groups.flatMap(group => [
    { label: group.label, kind: vscode.QuickPickItemKind.Separator },
    ...group.commands.map(([label, command]) => ({ label, command, description: group.label })),
  ])
  const picked = await vscode.window.showQuickPick(items, {
    title: 'MarkLeaf 格式与段落操作',
    placeHolder: '选择操作，或输入名称搜索',
    matchOnDescription: true,
  })
  return picked?.command
}
