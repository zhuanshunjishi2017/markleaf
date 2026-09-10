export type SharedEditorStrings = {
  linkTooltip: string
  fileLinkTooltip: string
  footnoteTooltip: string
  footnoteNotFound: string
  blockHandleAria: string
  blockParagraph: string
  blockHeading1: string
  blockHeading2: string
  blockHeading3: string
  blockHeading4: string
  blockHeading5: string
  blockHeading6: string
  blockBulletList: string
  blockOrderedList: string
  blockTaskList: string
  blockBlockquote: string
  blockCodeBlock: string
  blockMermaid: string
  blockTable: string
  blockFootnote: string
  blockAlert: string
  mermaidRender: string
  mermaidEmpty: string
  mermaidError: string
  mermaidTimeout: string
  copyCodeBlock: string
  formulaInputAssistant: string
  formulaGroupGreek: string
  formulaGroupOperators: string
  formulaGroupRelations: string
  formulaGroupStructures: string
  formulaGroupFonts: string
  formulaGroupBlocks: string
  formulaGroupArrowsDots: string
  formulaSectionVariants: string
  formulaSectionScriptsDecorations: string
  formulaSectionFractionsRoots: string
  formulaSectionBrackets: string
  formulaSectionIntegrals: string
  formulaSectionVectors: string
  formulaSectionUpright: string
  formulaSectionBlackboard: string
  formulaSectionCalligraphic: string
  formulaSectionScript: string
  formulaSectionAlignment: string
  formulaSectionWrappers: string
  formulaSectionMatrices: string
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
    blockParagraph: '段', blockHeading1: '标₁', blockHeading2: '标₂', blockHeading3: '标₃', blockHeading4: '标₄', blockHeading5: '标₅', blockHeading6: '标₆',
    blockBulletList: '列', blockOrderedList: '序', blockTaskList: '任', blockBlockquote: '引', blockCodeBlock: '码', blockMermaid: '图', blockTable: '表', blockFootnote: '注', blockAlert: '示',
    mermaidRender: '渲染为图表',
    mermaidEmpty: '空 Mermaid 图表',
    mermaidError: 'Mermaid 图表文本格式错误',
    mermaidTimeout: 'Mermaid 图表渲染超时',
    copyCodeBlock: '复制整段代码',
    formulaInputAssistant: '公式键入辅助',
    formulaGroupGreek: '希腊字母', formulaGroupOperators: '运算符', formulaGroupRelations: '关系符号',
    formulaGroupStructures: '结构', formulaGroupFonts: '字体', formulaGroupBlocks: '结构块', formulaGroupArrowsDots: '箭头与点号',
    formulaSectionVariants: '变体', formulaSectionScriptsDecorations: '上下标与修饰', formulaSectionFractionsRoots: '分式与根式',
    formulaSectionBrackets: '括号', formulaSectionIntegrals: '积分与运算', formulaSectionVectors: '向量',
    formulaSectionUpright: '正体', formulaSectionBlackboard: '黑板体', formulaSectionCalligraphic: '花体', formulaSectionScript: '手写体',
    formulaSectionAlignment: '对齐环境', formulaSectionWrappers: '包裹结构', formulaSectionMatrices: '矩阵与行列式',
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
    blockParagraph: '段', blockHeading1: '標₁', blockHeading2: '標₂', blockHeading3: '標₃', blockHeading4: '標₄', blockHeading5: '標₅', blockHeading6: '標₆',
    blockBulletList: '列', blockOrderedList: '序', blockTaskList: '任', blockBlockquote: '引', blockCodeBlock: '碼', blockMermaid: '圖', blockTable: '表', blockFootnote: '註', blockAlert: '示',
    mermaidRender: '算繪為圖表',
    mermaidEmpty: '空 Mermaid 圖表',
    mermaidError: 'Mermaid 圖表文字格式錯誤',
    mermaidTimeout: 'Mermaid 圖表算繪逾時',
    copyCodeBlock: '複製整段程式碼',
    formulaInputAssistant: '公式鍵入輔助',
    formulaGroupGreek: '希臘字母', formulaGroupOperators: '運算符', formulaGroupRelations: '關係符號',
    formulaGroupStructures: '結構', formulaGroupFonts: '字體', formulaGroupBlocks: '結構塊', formulaGroupArrowsDots: '箭頭與點號',
    formulaSectionVariants: '變體', formulaSectionScriptsDecorations: '上下標與修飾', formulaSectionFractionsRoots: '分式與根式',
    formulaSectionBrackets: '括號', formulaSectionIntegrals: '積分與運算', formulaSectionVectors: '向量',
    formulaSectionUpright: '正體', formulaSectionBlackboard: '黑板體', formulaSectionCalligraphic: '花體', formulaSectionScript: '手寫體',
    formulaSectionAlignment: '對齊環境', formulaSectionWrappers: '包裹結構', formulaSectionMatrices: '矩陣與行列式',
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
    blockParagraph: '¶', blockHeading1: 'H1', blockHeading2: 'H2', blockHeading3: 'H3', blockHeading4: 'H4', blockHeading5: 'H5', blockHeading6: 'H6',
    blockBulletList: '•', blockOrderedList: '1.', blockTaskList: '☑', blockBlockquote: '❝', blockCodeBlock: '</>', blockMermaid: '◇', blockTable: '▦', blockFootnote: 'Fn', blockAlert: '!',
    mermaidRender: 'Render as Diagram',
    mermaidEmpty: 'Empty Mermaid diagram',
    mermaidError: 'Invalid Mermaid diagram text',
    mermaidTimeout: 'Mermaid diagram rendering timed out',
    copyCodeBlock: 'Copy code block',
    formulaInputAssistant: 'Formula input assistant',
    formulaGroupGreek: 'Greek letters', formulaGroupOperators: 'Operators', formulaGroupRelations: 'Relations',
    formulaGroupStructures: 'Structures', formulaGroupFonts: 'Fonts', formulaGroupBlocks: 'Structure blocks', formulaGroupArrowsDots: 'Arrows and dots',
    formulaSectionVariants: 'Variants', formulaSectionScriptsDecorations: 'Scripts and accents', formulaSectionFractionsRoots: 'Fractions and roots',
    formulaSectionBrackets: 'Brackets', formulaSectionIntegrals: 'Integrals and operators', formulaSectionVectors: 'Vectors',
    formulaSectionUpright: 'Upright', formulaSectionBlackboard: 'Blackboard bold', formulaSectionCalligraphic: 'Calligraphic', formulaSectionScript: 'Script',
    formulaSectionAlignment: 'Alignment', formulaSectionWrappers: 'Wrappers', formulaSectionMatrices: 'Matrices and determinants',
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
    blockParagraph: '段', blockHeading1: '見₁', blockHeading2: '見₂', blockHeading3: '見₃', blockHeading4: '見₄', blockHeading5: '見₅', blockHeading6: '見₆',
    blockBulletList: '箇', blockOrderedList: '番', blockTaskList: 'タ', blockBlockquote: '引', blockCodeBlock: 'コ', blockMermaid: '図', blockTable: '表', blockFootnote: '注', blockAlert: '示',
    mermaidRender: '図表として描画',
    mermaidEmpty: '空の Mermaid 図表',
    mermaidError: 'Mermaid 図表のテキスト形式が正しくありません',
    mermaidTimeout: 'Mermaid 図表の描画がタイムアウトしました',
    copyCodeBlock: 'コードブロック全体をコピー',
    formulaInputAssistant: '数式入力補助',
    formulaGroupGreek: 'ギリシャ文字', formulaGroupOperators: '演算子', formulaGroupRelations: '関係記号',
    formulaGroupStructures: '構造', formulaGroupFonts: '書体', formulaGroupBlocks: '構造ブロック', formulaGroupArrowsDots: '矢印と点',
    formulaSectionVariants: '異体字', formulaSectionScriptsDecorations: '上付き・下付きと装飾', formulaSectionFractionsRoots: '分数と根号',
    formulaSectionBrackets: '括弧', formulaSectionIntegrals: '積分と演算', formulaSectionVectors: 'ベクトル',
    formulaSectionUpright: '立体', formulaSectionBlackboard: '黒板太字', formulaSectionCalligraphic: 'カリグラフィー', formulaSectionScript: '筆記体',
    formulaSectionAlignment: '配置環境', formulaSectionWrappers: '囲み構造', formulaSectionMatrices: '行列と行列式',
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
  const normalizedLanguage = normalizeSharedEditorLanguage(language)
  const table = tables[normalizedLanguage] ?? tables['zh-Hans']!
  const modifierName = modifier === 'meta' ? 'Command' : 'Ctrl'
  return {
    ...table,
    linkTooltip: table.linkTooltip(modifierName),
    fileLinkTooltip: table.fileLinkTooltip(modifierName),
    footnoteTooltip: table.footnoteTooltip(modifierName),
  }
}

export function normalizeSharedEditorLanguage(language: string): string {
  const normalized = language.trim().replaceAll('_', '-').toLowerCase()
  if (normalized === 'zh-hant' || /^zh-(tw|hk|mo)(-|$)/.test(normalized)) return 'zh-Hant'
  if (normalized === 'zh-hans' || normalized === 'zh' || normalized.startsWith('zh-')) return 'zh-Hans'
  if (normalized === 'en' || normalized.startsWith('en-')) return 'en'
  if (normalized === 'ja' || normalized.startsWith('ja-')) return 'ja'
  return 'zh-Hans'
}
