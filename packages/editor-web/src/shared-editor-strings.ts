export type SharedEditorStrings = {
  linkTooltip: string
  fileLinkTooltip: string
  footnoteTooltip: string
  footnoteNotFound: string
  blockHandleAria: string
  mermaidRender: string
  mermaidEmpty: string
  mermaidError: string
  mermaidTimeout: string
  formulaInputAssistant: string
  frontMatterTitle: string
  frontMatterHide: string
  frontMatterValid: string
  frontMatterInvalid: string
}

type PrimaryModifier = 'meta' | 'ctrl'
type LocalizedStrings = Omit<SharedEditorStrings, 'linkTooltip' | 'fileLinkTooltip' | 'footnoteTooltip'> & {
  linkTooltip: (modifier: string) => string
  fileLinkTooltip: (modifier: string) => string
  footnoteTooltip: (modifier: string) => string
}

const tables: Record<string, LocalizedStrings> = {
  'zh-Hans': {
    linkTooltip: modifier => `按住 ${modifier} 并单击以打开链接`,
    fileLinkTooltip: modifier => `按住 ${modifier} 并单击以打开文件`,
    footnoteTooltip: modifier => `按住 ${modifier} 并单击以转到注释定义`,
    footnoteNotFound: '找不到定义',
    blockHandleAria: '段落操作',
    mermaidRender: '渲染为图表',
    mermaidEmpty: '空 Mermaid 图表',
    mermaidError: 'Mermaid 图表文本格式错误',
    mermaidTimeout: 'Mermaid 图表渲染超时',
    formulaInputAssistant: '公式键入辅助',
    frontMatterTitle: '文档信息',
    frontMatterHide: '隐藏',
    frontMatterValid: 'YAML 格式有效',
    frontMatterInvalid: 'YAML格式错误',
  },
  'zh-Hant': {
    linkTooltip: modifier => `按住 ${modifier} 並按一下以開啟連結`,
    fileLinkTooltip: modifier => `按住 ${modifier} 並按一下以開啟檔案`,
    footnoteTooltip: modifier => `按住 ${modifier} 並按一下以前往註解定義`,
    footnoteNotFound: '找不到定義',
    blockHandleAria: '段落操作',
    mermaidRender: '算繪為圖表',
    mermaidEmpty: '空 Mermaid 圖表',
    mermaidError: 'Mermaid 圖表文字格式錯誤',
    mermaidTimeout: 'Mermaid 圖表算繪逾時',
    formulaInputAssistant: '公式鍵入輔助',
    frontMatterTitle: '文件資訊',
    frontMatterHide: '隱藏',
    frontMatterValid: 'YAML 格式有效',
    frontMatterInvalid: 'YAML格式錯誤',
  },
  en: {
    linkTooltip: modifier => `Hold ${modifier} and click to open link`,
    fileLinkTooltip: modifier => `Hold ${modifier} and click to open file`,
    footnoteTooltip: modifier => `Hold ${modifier} and click to go to the footnote definition`,
    footnoteNotFound: 'Definition not found',
    blockHandleAria: 'Paragraph actions',
    mermaidRender: 'Render as Diagram',
    mermaidEmpty: 'Empty Mermaid diagram',
    mermaidError: 'Invalid Mermaid diagram text',
    mermaidTimeout: 'Mermaid diagram rendering timed out',
    formulaInputAssistant: 'Formula input assistant',
    frontMatterTitle: 'Document Information',
    frontMatterHide: 'Hide',
    frontMatterValid: 'Valid YAML',
    frontMatterInvalid: 'Invalid YAML',
  },
  ja: {
    linkTooltip: modifier => `${modifier}を押しながらクリックしてリンクを開きます`,
    fileLinkTooltip: modifier => `${modifier}を押しながらクリックしてファイルを開きます`,
    footnoteTooltip: modifier => `${modifier}を押しながらクリックして脚注の定義に移動します`,
    footnoteNotFound: '定義が見つかりません',
    blockHandleAria: '段落操作',
    mermaidRender: '図表として描画',
    mermaidEmpty: '空の Mermaid 図表',
    mermaidError: 'Mermaid 図表のテキスト形式が正しくありません',
    mermaidTimeout: 'Mermaid 図表の描画がタイムアウトしました',
    formulaInputAssistant: '数式入力補助',
    frontMatterTitle: '文書情報',
    frontMatterHide: '非表示',
    frontMatterValid: '有効な YAML',
    frontMatterInvalid: '無効な YAML',
  },
}

export function sharedEditorStrings(
  language: string,
  modifier: PrimaryModifier,
): SharedEditorStrings {
  const table = tables[language] ?? tables['zh-Hans']!
  const modifierName = modifier === 'meta' ? 'Command' : 'Ctrl'
  return {
    ...table,
    linkTooltip: table.linkTooltip(modifierName),
    fileLinkTooltip: table.fileLinkTooltip(modifierName),
    footnoteTooltip: table.footnoteTooltip(modifierName),
  }
}
