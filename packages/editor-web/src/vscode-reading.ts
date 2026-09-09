import type { Editor } from '@tiptap/core'
import { getEditorStatus, setAutoConvertUnsafeEmphasis, setBlockHandleVisible, setBlockTypeLabels,
  setCodeHighlightVisible, setEditorFocusMode, setEditorSharedStrings, setMarkdownEditingSettings } from './editor'
import { sharedEditorStrings } from './shared-editor-strings'
import { rerenderMermaidElements, setMermaidStrings } from './mermaid'
import { defaultSettings, type MarkLeafSettings } from './vscode-settings'

// Keep the original @depends metadata: CSS processing would remove these
// comments in production. These styles contain no external asset URLs.
const styles = import.meta.glob('../../styles/*.css', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

function resolveTypography(id: string): { classes: string[]; css: string } {
  const classes: string[] = []
  const parts: string[] = []
  const seen = new Set<string>()
  function visit(name: string): void {
    if (seen.has(name)) return
    seen.add(name)
    const css = styles[`../../styles/${name}.css`]
    if (!css) return
    const dependency = /@depends:\s*([\w-]+)/.exec(css)?.[1]
    if (dependency) visit(dependency)
    classes.push(`markleaf-style-${name}`)
    parts.push(css)
  }
  visit(id)
  return { classes, css: parts.join('\n') }
}

export function createReadingView(editor: Editor, mount: HTMLElement, count: HTMLElement) {
  let settings = { ...defaultSettings }
  let lastFocus: boolean | undefined
  let scrollFrame = 0
  let cursorFrame = 0
  let scrollbarTimer: ReturnType<typeof setTimeout> | undefined
  const events = new AbortController()
  function showScrollbar(): void {
    if (!settings.autoHideScrollbars) return
    document.documentElement.style.setProperty('--ml-scrollbar-alpha', '1')
    clearTimeout(scrollbarTimer)
    scrollbarTimer = setTimeout(() => document.documentElement.style.setProperty('--ml-scrollbar-alpha', '0'), 900)
  }
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
    let current = headings[0]
    for (const heading of headings) {
      const node = editor.view.nodeDOM(heading.position)
      if (fromCursor ? heading.position <= editor.state.selection.from
        : node instanceof HTMLElement && node.getBoundingClientRect().top <= headerBottom() + 24) current = heading
    }
    for (const heading of headings) heading.button.setAttribute('aria-current', String(heading === current))
  }
  function rebuildOutline(): void {
    headings = []
    const title = document.createElement('strong')
    title.textContent = '大纲'
    outline.replaceChildren(title)
    editor.state.doc.descendants((node, position) => {
      if (node.type.name !== 'heading') return
      const button = document.createElement('button')
      button.type = 'button'
      button.textContent = node.textContent || '（空标题）'
      button.style.paddingInlineStart = `${8 + (node.attrs.level - 1) * 12}px`
      button.dataset.position = String(position)
      button.addEventListener('click', () => {
        const element = editor.view.nodeDOM(position)
        if (element instanceof HTMLElement) window.scrollTo({ top: Math.max(0, window.scrollY + element.getBoundingClientRect().top - headerBottom() - 16), behavior: 'smooth' })
      })
      headings.push({ position, level: node.attrs.level, text: node.textContent, button })
      outline.append(button)
    })
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
    mount.classList.toggle('markleaf-editor-typewriter', settings.typewriterMode && editor.isEditable)
    markCurrent(true)
  }
  function cursorMoved(): void {
    update()
    cancelAnimationFrame(cursorFrame)
    if (settings.typewriterMode && editor.isEditable && editor.view.hasFocus() && !editor.view.composing) {
      cursorFrame = requestAnimationFrame(() => {
        try {
          const top = editor.view.coordsAtPos(editor.state.selection.head).top
          window.scrollTo({ top: Math.max(0, window.scrollY + top - window.innerHeight * .4), behavior: 'auto' })
        } catch { /* The caret can be unmounted during document replacement. */ }
      })
    }
  }
  window.addEventListener('scroll', () => {
    showScrollbar()
    if (scrollFrame) return
    scrollFrame = requestAnimationFrame(() => { scrollFrame = 0; markCurrent(false) })
  }, { signal: events.signal, passive: true })
  window.addEventListener('mousemove', event => {
    if (event.clientX >= window.innerWidth - 20) showScrollbar()
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
      root.classList.toggle('markleaf-auto-hide-scrollbar', next.autoHideScrollbars)
      document.body.classList.toggle('markleaf-auto-hide-scrollbar', next.autoHideScrollbars)
      mount.lang = next.cjkLanguage
      for (const name of [...mount.classList]) if (name.startsWith('markleaf-style-')) mount.classList.remove(name)
      const resolvedStyle = resolveTypography(next.typography)
      mount.classList.add(...resolvedStyle.classes)
      theme.textContent = next.colorTheme === 'vscode' ? '' : styles[`../../styles/colors-${next.colorTheme}.css`] ?? ''
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
      events.abort(); cancelAnimationFrame(scrollFrame); cancelAnimationFrame(cursorFrame); clearTimeout(scrollbarTimer)
      editor.off('update', rebuildOutline); editor.off('selectionUpdate', cursorMoved)
      theme.remove(); typography.remove(); custom.remove()
    },
  }
}
