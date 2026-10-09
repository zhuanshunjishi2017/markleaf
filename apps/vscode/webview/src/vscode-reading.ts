import type { Editor } from '@markleaf/editor-core'
import {
  createReadingBehavior, getEditorStatus, rerenderMermaidElements, setAutoConvertUnsafeEmphasis, setBlockHandleVisible,
  setBlockTypeLabels, setCodeHighlightVisible, setEditorFocusMode, setEditorSharedStrings,
  setMarkdownEditingSettings, setMermaidStrings, sharedEditorStrings,
} from '@markleaf/editor-core'
import { defaultSettings, type MarkLeafSettings } from './vscode-settings'
import { createOutlineView } from './vscode-outline'

import { styles, resolveTypography, stylesGlobPrefix } from './vscode-styles'

export function createReadingView(editor: Editor, mount: HTMLElement, count: HTMLElement) {
  let settings = { ...defaultSettings }
  let lastFocus: boolean | undefined
  let applied = false
  let appliedCss = ''
  let appliedLanguage = ''
  const behavior = createReadingBehavior(() => editor, headerBottom)
  const area = document.createElement('div')
  area.id = 'document-area'
  const outline = createOutlineView(editor, headerBottom)
  mount.replaceWith(area)
  area.append(outline.element, mount)
  const theme = document.createElement('style')
  const typography = document.createElement('style')
  const custom = document.createElement('style')
  document.head.append(theme, typography, custom)
  function headerBottom(): number {
    return document.querySelector('#editor-chrome')!.getBoundingClientRect().bottom
  }
  function update(): void {
    const state = getEditorStatus(editor)
    const labels: Record<string, string> = { paragraph: '正文', blockquote: '引用', alert: '提示框', codeBlock: '代码块',
      bulletList: '无序列表', orderedList: '有序列表', taskList: '任务列表', table: '表格', image: '图片', footnoteDefinition: '脚注' }
    const block = labels[state.blockType] ?? state.blockType.replace('heading', 'H')
    count.textContent = `${state.totalCharacterCount.toLocaleString()} 字符${state.selectedCharacterCount ? ` · 已选 ${state.selectedCharacterCount}` : ''} · ${block} · ${state.line}:${state.column}`
    count.title = `非空白 ${state.nonWhitespaceCharacterCount} · 中日韩文字 ${state.cjkCharacterCount} · 西文单词 ${state.westernWordCount}\n公式 ${state.formulaCount} · 代码 ${state.codeLineCount} 行 · 段落 ${state.paragraphCount}`
    const focus = settings.focusMode && editor.isEditable
    if (focus !== lastFocus) { lastFocus = focus; setEditorFocusMode(editor, focus) }
    mount.classList.toggle('markleaf-editor-focus-mode', focus)
    behavior.setTypewriter(settings.typewriterMode && editor.isEditable, false)
  }
  function cursorMoved(): void {
    update()
    outline.updateCurrent('cursor')
    behavior.cursorMoved()
  }
  editor.on('selectionUpdate', cursorMoved)
  return {
    update, rebuildOutline: outline.refresh,
    apply(next: MarkLeafSettings, customCss = '', language = 'zh-Hans'): void {
      const diagramStyleChanged = applied && (settings.fontFamily !== next.fontFamily
        || settings.typography !== next.typography || settings.colorTheme !== next.colorTheme
        || appliedCss !== customCss || appliedLanguage !== language)
      settings = next
      applied = true
      appliedCss = customCss
      appliedLanguage = language
      const root = document.documentElement
      root.style.setProperty('--ml-font-size', `${Math.max(10, Math.min(32, next.fontSize)) * Math.max(50, Math.min(200, next.zoom)) / 100}px`)
      root.style.setProperty('--ml-max-width', `${Math.max(320, Math.min(1600, next.maxWidth))}px`)
      root.style.setProperty('--ml-line-height', String(Math.max(1, Math.min(3, next.lineHeight))))
      root.style.setProperty('--ml-source-font-size', `${next.sourceFontSize}px`)
      root.style.setProperty('--ml-source-font-family', next.sourceFontFamily || 'var(--vscode-editor-font-family, monospace)')
      root.classList.toggle('markleaf-cjk-autospace', next.cjkAutoSpacing)
      root.classList.toggle('markleaf-ignore-max-width', next.ignoreMaxWidth)
      behavior.setAutoHideScrollbar(next.autoHideScrollbars)
      mount.lang = next.cjkLanguage
      for (const name of [...mount.classList]) if (name.startsWith('markleaf-style-')) mount.classList.remove(name)
      const resolvedStyle = resolveTypography(next.typography)
      mount.classList.add(...resolvedStyle.classes)
      theme.textContent = next.colorTheme === 'vscode' ? '' : styles[`${stylesGlobPrefix}colors-${next.colorTheme}.css`] ?? ''
      typography.textContent = resolvedStyle.css
      custom.textContent = customCss
      editor.view.dom.style.fontFamily = next.fontFamily
      area.classList.toggle('with-outline', next.showOutline)
      outline.setVisible(next.showOutline)
      const footer = document.querySelector('footer')
      if (footer) footer.hidden = !next.showStatusBar
      setMarkdownEditingSettings(next)
      setAutoConvertUnsafeEmphasis(next.autoConvertUnsafeEmphasis)
      setCodeHighlightVisible(editor, next.showCodeHighlight)
      setBlockHandleVisible(editor, next.showBlockHandle)
      const strings = sharedEditorStrings(language, /Mac/i.test(navigator.platform) ? 'meta' : 'ctrl')
      setEditorSharedStrings(strings)
      setMermaidStrings(strings)
      setBlockTypeLabels(strings)
      document.querySelector('.ml-block-handle')?.setAttribute('aria-label', strings.blockHandleAria)
      outline.refresh()
      update()
      // Initial node views render after this synchronous settings pass.
      if (diagramStyleChanged) rerenderMermaidElements(mount)
    },
    dispose(): void {
      outline.dispose(); behavior.dispose()
      editor.off('selectionUpdate', cursorMoved)
      theme.remove(); typography.remove(); custom.remove()
    },
  }
}
