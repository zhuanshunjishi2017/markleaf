import type { Editor } from '@markleaf/editor-core'
import {
  createReadingBehavior, getDocumentOutline, getActiveOutlinePosition, scrollToOutlineHeading, getEditorStatus, rerenderMermaidElements, setAutoConvertUnsafeEmphasis, setBlockHandleVisible,
  setBlockTypeLabels, setCodeHighlightVisible, setEditorFocusMode, setEditorSharedStrings,
  setMarkdownEditingSettings, setMermaidStrings, sharedEditorStrings,
} from '@markleaf/editor-core'
import { defaultSettings, type MarkLeafSettings } from './vscode-settings'

import { styles, resolveTypography, stylesGlobPrefix } from './vscode-styles'

export function createReadingView(editor: Editor, mount: HTMLElement, count: HTMLElement) {
  let settings = { ...defaultSettings }
  let lastFocus: boolean | undefined
  let scrollFrame = 0
  const events = new AbortController()
  const behavior = createReadingBehavior(() => editor, headerBottom)
  const area = document.createElement('div')
  area.id = 'document-area'
  const outline = document.createElement('nav')
  outline.id = 'outline'
  outline.setAttribute('aria-label', '文档大纲')
  mount.replaceWith(area)
  area.append(outline, mount)
  const theme = document.createElement('style')
  const typography = document.createElement('style')
  const custom = document.createElement('style')
  document.head.append(theme, typography, custom)
  let headings: Array<{ position: number; level: number; text: string; button: HTMLButtonElement }> = []
  function headerBottom(): number {
    return document.querySelector('#find-bar:not([hidden])')?.getBoundingClientRect().bottom
      ?? document.querySelector('#toolbar')?.getBoundingClientRect().bottom ?? 0
  }
  function markCurrent(fromCursor: boolean): void {
    const position = getActiveOutlinePosition(editor, fromCursor ? 'cursor' : 'scroll', headerBottom())
    for (const heading of headings) heading.button.setAttribute('aria-current', String(heading.position === position))
  }
  function rebuildOutline(): void {
    headings = []
    const title = document.createElement('strong')
    title.textContent = '大纲'
    outline.replaceChildren(title)
    for (const { position, level, text } of getDocumentOutline(editor)) {
      const button = document.createElement('button')
      button.type = 'button'
      button.textContent = text || '（空标题）'
      button.style.paddingInlineStart = `${8 + (level - 1) * 12}px`
      button.dataset.position = String(position)
      button.addEventListener('click', () => scrollToOutlineHeading(editor, position, headerBottom()))
      headings.push({ position, level, text, button })
      outline.append(button)
    }
    if (!headings.length) { const empty = document.createElement('p'); empty.textContent = '文档中没有标题'; outline.append(empty) }
    markCurrent(true)
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
    markCurrent(true)
  }
  function cursorMoved(): void {
    update()
    behavior.cursorMoved()
  }
  window.addEventListener('scroll', () => {
    if (scrollFrame) return
    scrollFrame = requestAnimationFrame(() => { scrollFrame = 0; markCurrent(false) })
  }, { signal: events.signal, passive: true })
  editor.on('update', rebuildOutline)
  editor.on('selectionUpdate', cursorMoved)
  rebuildOutline()
  return {
    update, rebuildOutline,
    apply(next: MarkLeafSettings, customCss = '', language = 'zh-Hans'): void {
      settings = next
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
      outline.hidden = !next.showOutline
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
      update()
      rerenderMermaidElements(mount)
    },
    dispose(): void {
      events.abort(); cancelAnimationFrame(scrollFrame); behavior.dispose()
      editor.off('update', rebuildOutline); editor.off('selectionUpdate', cursorMoved)
      theme.remove(); typography.remove(); custom.remove()
    },
  }
}
