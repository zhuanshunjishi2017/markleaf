import { describe, expect, it } from 'vitest'
import { sharedEditorStrings } from '../src/shared-editor-strings'

describe('shared editor strings', () => {
  it('uses the actual host modifier in English activation hints', () => {
    expect(sharedEditorStrings('en', 'meta').linkTooltip)
      .toBe('Hold Command and click to open link')
    expect(sharedEditorStrings('en', 'ctrl').footnoteTooltip)
      .toBe('Hold Ctrl and click to go to the footnote definition')
  })

  it('provides complete Mermaid and block-handle strings in every language', () => {
    const expected = {
      'zh-Hans': ['段落操作', '渲染为图表', '空 Mermaid 图表', 'Mermaid 图表文本格式错误', 'Mermaid 图表渲染超时'],
      'zh-Hant': ['段落操作', '算繪為圖表', '空 Mermaid 圖表', 'Mermaid 圖表文字格式錯誤', 'Mermaid 圖表算繪逾時'],
      en: ['Paragraph actions', 'Render as Diagram', 'Empty Mermaid diagram', 'Invalid Mermaid diagram text', 'Mermaid diagram rendering timed out'],
      ja: ['段落操作', '図表として描画', '空の Mermaid 図表', 'Mermaid 図表のテキスト形式が正しくありません', 'Mermaid 図表の描画がタイムアウトしました'],
    } as const

    for (const [language, values] of Object.entries(expected)) {
      const strings = sharedEditorStrings(language, 'ctrl')
      expect([
        strings.blockHandleAria,
        strings.mermaidRender,
        strings.mermaidEmpty,
        strings.mermaidError,
        strings.mermaidTimeout,
      ]).toEqual(values)
    }
  })

  it('falls back to Simplified Chinese for an unknown language', () => {
    expect(sharedEditorStrings('unknown', 'ctrl'))
      .toEqual(sharedEditorStrings('zh-Hans', 'ctrl'))
  })

  it('provides compact block-handle labels in every language', () => {
    expect(sharedEditorStrings('zh-Hans', 'ctrl')).toMatchObject({
      blockParagraph: '段',
      blockHeading1: '标₁',
      blockBulletList: '列',
      blockOrderedList: '序',
      blockTaskList: '任',
      blockBlockquote: '引',
      blockCodeBlock: '码',
      blockTable: '表',
      blockFootnote: '注',
      blockMermaid: '图',
    })
    expect(sharedEditorStrings('en', 'ctrl')).toMatchObject({
      blockParagraph: '¶',
      blockHeading1: 'H1',
      blockBulletList: '•',
      blockOrderedList: '1.',
      blockCodeBlock: '</>',
    })
    expect(sharedEditorStrings('ja', 'ctrl').blockHeading1).toBe('見₁')
    expect(sharedEditorStrings('zh-Hant', 'ctrl').blockHeading1).toBe('標₁')
  })

  it('localizes formula assistant groups and sections', () => {
    expect(sharedEditorStrings('zh-Hans', 'ctrl')).toMatchObject({
      formulaGroupGreek: '希腊字母',
      formulaSectionFractionsRoots: '分式与根式',
    })
    expect(sharedEditorStrings('en', 'ctrl')).toMatchObject({
      formulaGroupGreek: 'Greek letters',
      formulaSectionFractionsRoots: 'Fractions and roots',
    })
    expect(sharedEditorStrings('ja', 'ctrl').formulaSectionMatrices).toBe('行列と行列式')
    expect(sharedEditorStrings('zh-Hant', 'ctrl').formulaSectionMatrices).toBe('矩陣與行列式')
  })
})
