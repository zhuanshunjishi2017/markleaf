import './styles.css'
import {
  createEditor,
  clearFindHighlights,
  executeEditorCommand,
  exportEditorSelection,
  findInEditor,
  getEditorCommandState,
  getEditorStatus,
  getMarkdown,
  isAllowedLink,
  replaceAllInEditor,
  replaceCurrentInEditor,
  replaceEditorDocument,
  resetEditorViewport,
} from './editor'
import { SourceEditor } from './source-editor'
import { isPlainTextDocumentType, type DocumentType } from './document-mode'
import {
  executeFormatPainterApply,
  FormatPainterController,
  captureFormat,
  normalizeContextMenuCaretPosition,
} from './format-painter'
import {
  isHostMessage,
  postToHost,
  postToHostWithAdditionalObjects,
  protocolVersion,
  type HostMessage,
} from './protocol'

const editorElement = document.querySelector<HTMLElement>('#editor')

if (!editorElement) {
  throw new Error('Editor mount element was not found.')
}
const editorMount = editorElement
const sourceMount = document.querySelector<HTMLElement>('#source-editor')!
const findBar = document.querySelector<HTMLFormElement>('#find-bar')!
const findInput = document.querySelector<HTMLInputElement>('#find-input')!
const replaceInput = document.querySelector<HTMLInputElement>('#replace-input')!
const caseInput = document.querySelector<HTMLInputElement>('#find-case')!
const wholeInput = document.querySelector<HTMLInputElement>('#find-whole')!
const findResult = document.querySelector<HTMLElement>('#find-result')!
const findPrevious = document.querySelector<HTMLButtonElement>('#find-previous')!
const findNext = document.querySelector<HTMLButtonElement>('#find-next')!
const replaceOne = document.querySelector<HTMLButtonElement>('#replace-one')!
const replaceAll = document.querySelector<HTMLButtonElement>('#replace-all')!
const findClose = document.querySelector<HTMLButtonElement>('#find-close')!
const sourceToggle = document.querySelector<HTMLButtonElement>('#source-toggle')!

let documentId: string = crypto.randomUUID()
let documentLoaded = false
let revision = 0
let compositionActive = false
let compositionChanged = false
let suppressUpdate = false
let lastOutlinePosition: number | null | undefined
let outlineTimer = 0
let sourceEditor: SourceEditor | null = null
let sourceMode = false
let sourceIndentWidth = 2
let replaceMode = false
let documentType: DocumentType = 'markdown'

let editor = createEditor(editorMount)
const formatPainter = new FormatPainterController()
let contextMenuSelection: { from: number; to: number } | null = null

let baseCss = ''
let styleCatalog: { id: string; css: string; dependsOn?: string }[] = []
let scrollbarHideTimer = 0

function send(type: Parameters<typeof postToHost>[0]['type'], payload?: unknown, requestId?: string): void {
  postToHost({
    protocolVersion,
    type,
    requestId,
    documentId,
    revision,
    payload,
  })
}

function sendWithAdditionalObjects(
  type: Parameters<typeof postToHost>[0]['type'],
  payload: unknown,
  additionalObjects: object[],
): void {
  postToHostWithAdditionalObjects({
    protocolVersion,
    type,
    documentId,
    revision,
    payload,
  }, additionalObjects)
}

function sendOutline(): void {
  const headings: Array<{ level: number; text: string; position: number }> = []
  editor.state.doc.descendants((node, position) => {
    if (node.type.name === 'heading') {
      headings.push({ level: node.attrs.level as number, text: node.textContent, position })
    }
  })
  send('outlineChanged', { headings })
}

function scheduleOutline(): void {
  window.clearTimeout(outlineTimer)
  outlineTimer = window.setTimeout(sendOutline, 250)
}

function sendOutlineSelection(position: number | null): void {
  if (position === lastOutlinePosition) {
    return
  }
  lastOutlinePosition = position
  send('outlineSelectionChanged', { position })
}

function sendOutlineSelectionFromCursor(): void {
  const cursor = editor.state.selection.from
  let activePosition: number | null = null
  editor.state.doc.descendants((node, position) => {
    if (node.type.name === 'heading' && position < cursor) {
      activePosition = position
    }
  })
  sendOutlineSelection(activePosition)
}

function sendOutlineSelectionFromScroll(): void {
  const headings = Array.from(editor.view.dom.querySelectorAll<HTMLElement>('h1, h2, h3, h4, h5, h6'))
  if (headings.length === 0) {
    sendOutlineSelection(null)
    return
  }

  const threshold = Math.max(80, window.innerHeight * 0.2)
  const active = headings.filter(heading => heading.getBoundingClientRect().top <= threshold).at(-1) ?? headings[0]!
  sendOutlineSelection(editor.view.posAtDOM(active, 0))
}

function sendCommandState(): void {
  const sourceSelection = sourceEditor?.view.state.selection.main
  send('commandStateChanged', {
    ...getEditorCommandState(editor),
    hasSelection: sourceSelection ? !sourceSelection.empty : getEditorCommandState(editor).hasSelection,
    sourceMode,
    canStartFormatPainter: !sourceMode && captureFormat(editor) !== null,
    formatPainterArmed: !sourceMode && formatPainter.isArmed,
  })
}

function sendEditorStatus(): void {
  if (sourceEditor) {
    const text = sourceEditor.getText()
    send('editorStatusChanged', {
      characterCount: Array.from(text).filter(character => !/\s/u.test(character)).length,
      selectedCharacterCount: 0,
      blockType: 'paragraph',
      line: 1,
      column: 1,
    })
    return
  }
  send('editorStatusChanged', getEditorStatus(editor))
}

function sendEditorState(): void {
  sendCommandState()
  sendEditorStatus()
}

function updateFormatPainterCursor(): void {
  editorMount.classList.toggle('format-painter-armed', !sourceMode && formatPainter.isArmed)
}

function bindEditorEvents(targetEditor: typeof editor): void {
  targetEditor.on('update', () => {
    if (suppressUpdate || compositionActive) {
      if (compositionActive) {
        compositionChanged = true
      }
      return
    }

    revision += 1
    send('dirtyChanged', { dirty: true })
    scheduleOutline()
    sendEditorState()
  })

  targetEditor.on('selectionUpdate', () => {
    if (!compositionActive) {
      send('selectionChanged', {
        from: targetEditor.state.selection.from,
        to: targetEditor.state.selection.to,
      })
      sendEditorState()
      sendOutlineSelectionFromCursor()
    }
  })

  // 格式刷在「鼠标抬起」时应用，而不是在选区开始变化的瞬间（对齐 Word 的涂抹交互）。
  targetEditor.view.dom.addEventListener('mouseup', () => {
    if (compositionActive) return
    const wasArmed = formatPainter.isArmed
    formatPainter.applyOnSelection(targetEditor)
    if (wasArmed !== formatPainter.isArmed) {
      updateFormatPainterCursor()
      sendEditorState()
    }
  })

  targetEditor.view.dom.addEventListener('compositionstart', () => {
    compositionActive = true
    compositionChanged = false
  })

  targetEditor.view.dom.addEventListener('compositionend', () => {
    compositionActive = false
    if (compositionChanged) {
      revision += 1
      send('dirtyChanged', { dirty: true })
      scheduleOutline()
      sendEditorState()
    }
    compositionChanged = false
  })
}

bindEditorEvents(editor)

function markSourceChanged(documentChanged: boolean): void {
  if (documentChanged) {
    revision += 1
    send('dirtyChanged', { dirty: true })
  }
  sendEditorState()
}

function getSelectionExport(): { text: string; markdown: string; html: string } {
  if (sourceEditor) {
    const text = sourceEditor.getSelectedText()
    return { text, markdown: text, html: '' }
  }
  return exportEditorSelection(editor)
}

function getActiveMarkdown(): string {
  return sourceEditor?.getText() ?? getMarkdown(editor)
}

function setSourceMode(enabled: boolean): void {
  if (documentType === 'plainText') return
  if (enabled === sourceMode) return
  formatPainter.cancel()
  updateFormatPainterCursor()
  if (enabled) {
    sourceEditor = new SourceEditor(sourceMount, getMarkdown(editor), markSourceChanged, sourceIndentWidth)
    editorMount.hidden = true
    sourceMount.hidden = false
    sourceMode = true
    sourceEditor.focus()
  } else {
    const markdown = sourceEditor?.getText() ?? getMarkdown(editor)
    sourceEditor?.destroy()
    sourceEditor = null
    suppressUpdate = true
    editor = replaceEditorDocument(editor, editorMount, markdown)
    bindEditorEvents(editor)
    suppressUpdate = false
    sourceMount.hidden = true
    editorMount.hidden = false
    sourceMode = false
    scheduleOutline()
    sendOutlineSelectionFromCursor()
    editor.commands.focus()
  }
  sendEditorState()
}

function showFindBar(showReplace: boolean): void {
  replaceMode = showReplace
  findBar.hidden = false
  replaceInput.hidden = !showReplace
  replaceOne.hidden = !showReplace
  replaceAll.hidden = !showReplace
  findInput.focus()
  findInput.select()
  updateFindResult(false)
}

function closeFindBar(): void {
  findBar.hidden = true
  if (!sourceMode) clearFindHighlights(editor)
  if (sourceMode) sourceEditor?.focus()
  else editor.commands.focus()
}

function updateFindResult(backwards: boolean): void {
  const result = sourceMode
    ? sourceEditor?.find(findQuery, findCaseSensitive, findWholeWord, backwards) ?? { current: 0, total: 0 }
    : findInEditor(editor, findQuery, findCaseSensitive, findWholeWord, backwards)
  findResult.textContent = `${result.current}/${result.total}`
  send('findResult', result)
}

function replaceCurrent(): void {
  const result = sourceMode
    ? sourceEditor?.replaceCurrent(findQuery, findReplace, findCaseSensitive, findWholeWord)
      ?? { current: 0, total: 0 }
    : replaceCurrentInEditor(editor, findQuery, findReplace, findCaseSensitive, findWholeWord)
  findResult.textContent = `${result.current}/${result.total}`
  send('findResult', result)
}

function replaceEveryMatch(): void {
  const count = sourceMode
    ? sourceEditor?.replaceAll(findQuery, findReplace, findCaseSensitive, findWholeWord) ?? 0
    : replaceAllInEditor(editor, findQuery, findReplace, findCaseSensitive, findWholeWord)
  findResult.textContent = count === 0 ? '0/0' : (markleafLanguage === 'zh-Hant' ? `已取代 ${count} 處` : markleafLanguage === 'en' ? `Replaced ${count} occurrence${count === 1 ? '' : 's'}` : markleafLanguage === 'ja' ? `${count} 件を置換しました` : `已替换 ${count} 处`)
  send('findResult', { current: count, total: count, replaced: count })
}

findBar.addEventListener('submit', event => {
  event.preventDefault()
  updateFindResult(false)
})
findInput.addEventListener('input', () => updateFindResult(false))
caseInput.addEventListener('change', () => updateFindResult(false))
wholeInput.addEventListener('change', () => updateFindResult(false))
findPrevious.addEventListener('click', () => updateFindResult(true))
findNext.addEventListener('click', () => updateFindResult(false))
replaceOne.addEventListener('click', replaceCurrent)
replaceAll.addEventListener('click', replaceEveryMatch)
findClose.addEventListener('click', closeFindBar)
sourceToggle.addEventListener('click', () => setSourceMode(!sourceMode))
window.addEventListener('keydown', event => {
  if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === 'f') {
    event.preventDefault()
    showFindBar(false)
    return
  }
  if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === 'h') {
    event.preventDefault()
    showFindBar(true)
    return
  }
  if (event.key === 'Escape' && formatPainter.isArmed) {
    event.preventDefault()
    formatPainter.cancel()
    updateFormatPainterCursor()
    sendEditorState()
    return
  }
  if (event.key === 'Escape' && !findBar.hidden) {
    event.preventDefault()
    closeFindBar()
  }
})

let scrollFrame = 0
window.addEventListener('scroll', () => {
  if (scrollFrame !== 0) {
    return
  }
  scrollFrame = window.requestAnimationFrame(() => {
    scrollFrame = 0
    sendOutlineSelectionFromScroll()
  })
}, { passive: true })

editorMount.addEventListener('click', (event) => {
  if (!(event.target instanceof Element)) {
    return
  }

  const anchor = event.target.closest<HTMLAnchorElement>('a[href]')
  const url = anchor?.getAttribute('href')
  if (!url || !isAllowedLink(url)) {
    return
  }

  event.preventDefault()
  send('openLink', { url })
})

editorMount.addEventListener('contextmenu', (event) => {
  event.preventDefault()
  const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (resolved) {
    const selection = editor.state.selection
    if (selection.empty || resolved.pos < selection.from || resolved.pos > selection.to) {
      editor.commands.setTextSelection(normalizeContextMenuCaretPosition(editor, resolved.pos))
    }
  }
  editor.commands.focus()
  contextMenuSelection = {
    from: editor.state.selection.from,
    to: editor.state.selection.to,
  }
  sendEditorState()
  send('contextMenuRequested', {
    clientX: event.clientX,
    clientY: event.clientY,
    canStartFormatPainter: !sourceMode && captureFormat(editor) !== null,
    formatPainterArmed: !sourceMode && formatPainter.isArmed,
  })
})

sourceMount.addEventListener('contextmenu', (event) => {
  event.preventDefault()
  sourceEditor?.focus()
  sendEditorState()
  send('contextMenuRequested', {
    clientX: event.clientX,
    clientY: event.clientY,
    canStartFormatPainter: false,
    formatPainterArmed: false,
  })
})

editorMount.addEventListener('dragover', (event) => {
  if (event.dataTransfer?.types.includes('Files')) {
    event.preventDefault()
    event.dataTransfer.dropEffect = 'copy'
  }
})

editorMount.addEventListener('drop', (event) => {
  const files = Array.from(event.dataTransfer?.files ?? []).slice(0, 32)
  if (files.length > 0) {
    event.preventDefault()
    event.stopPropagation()
    sendWithAdditionalObjects('dropFiles', {
      count: files.length,
      clientX: event.clientX,
      clientY: event.clientY,
    }, files)
  }
})

editorMount.addEventListener('paste', (event) => {
  if (Array.from(event.clipboardData?.items ?? []).some((item) => item.type.startsWith('image/'))) {
    event.preventDefault()
    send('pasteImage', {})
  }
})

function handleMessage(value: unknown): void {
  if (!isHostMessage(value)) {
    send('error', { message: 'Invalid host message.' })
    return
  }

  const message: HostMessage = value

  // 文档尚未加载时，宿主的会话 documentId 还是随机占位值，与前端不一致；
  // 此时 applyStyles/setAutoHideScrollbar 等文档无关的偏好推送必须放行。
  if (message.type !== 'loadDocument' && message.type !== 'applyStyles'
      && documentLoaded && message.documentId !== documentId) {
    return
  }

  switch (message.type) {
    case 'applyStyles': {
      const payload = message.payload as {
        baseCss?: unknown
        colorThemeCss?: unknown
        styles?: unknown
        activeStyle?: unknown
      }
      if (typeof payload?.baseCss === 'string') baseCss = payload.baseCss
      if (Array.isArray(payload?.styles)) {
        styleCatalog = payload.styles.filter((s): s is { id: string; css: string; dependsOn?: string } =>
          typeof s === 'object'
          && s !== null
          && typeof (s as { id?: unknown }).id === 'string'
          && typeof (s as { css?: unknown }).css === 'string')
      }
      // 注入顺序即 DOM 中优先级：base < 颜色主题 < 排版样式
      injectStyleSheet('markleaf-base-style', baseCss)
      if (typeof payload?.colorThemeCss === 'string') {
        injectStyleSheet('markleaf-color-theme', payload.colorThemeCss)
      }
      for (const style of styleCatalog) {
        injectStyleSheet(`markleaf-style-${style.id}`, style.css)
      }
      applyMarkleafStyle(typeof payload?.activeStyle === 'string' ? payload.activeStyle : 'serif')
      break
    }
    case 'loadDocument': {
      formatPainter.cancel()
      updateFormatPainterCursor()
      const payload = message.payload as { markdown?: unknown; documentType?: unknown }
      if (typeof payload?.markdown !== 'string') {
        send('error', { message: 'loadDocument requires a markdown string.' }, message.requestId)
        return
      }
      documentId = message.documentId
      documentLoaded = true
      revision = message.revision
      documentType = isPlainTextDocumentType(payload?.documentType) ? 'plainText' : 'markdown'
      suppressUpdate = true
      sourceEditor?.destroy()
      sourceEditor = null
      if (documentType === 'plainText') {
        sourceMode = true
        sourceMount.hidden = false
        editorMount.hidden = true
        sourceEditor = new SourceEditor(sourceMount, payload.markdown, markSourceChanged, sourceIndentWidth)
      } else {
        sourceMode = false
        sourceMount.hidden = true
        editorMount.hidden = false
        editor = replaceEditorDocument(editor, editorMount, payload.markdown)
        bindEditorEvents(editor)
        resetEditorViewport(editor, editorMount)
      }
      suppressUpdate = false
      send('documentLoaded', undefined, message.requestId)
      sendOutline()
      sendEditorState()
      sendOutlineSelectionFromCursor()
      break
    }
    case 'requestSnapshot':
      send('snapshot', { markdown: getActiveMarkdown() }, message.requestId)
      break
    case 'command': {
      const payload = message.payload as {
        command?: unknown
        text?: unknown
        clientX?: unknown
        clientY?: unknown
        applyToCurrentTextBlockWhenEmpty?: unknown
      }
      if (typeof payload?.command === 'string') {
        if (payload.command === 'find' || payload.command === 'replace') {
          showFindBar(payload.command === 'replace')
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'toggleSourceMode') {
          setSourceMode(!sourceMode)
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setStyle') {
          applyMarkleafStyle(typeof payload.text === 'string' ? payload.text : 'serif')
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setSourceSelection') {
          const parts = String(payload.text ?? '').split(',').map(Number)
          const from = parts[0] ?? NaN
          if (Number.isFinite(from)) {
            const to = parts.length >= 2 && Number.isFinite(parts[1]) ? parts[1]! : from
            sourceEditor?.setSelection(from, to)
          }
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'findText' || payload.command === 'findNext' || payload.command === 'findPrev') {
          // text: query\tcase\twhole（findText 额外带方向）；findNext/findPrev 沿用当前状态
          if (typeof payload.text === 'string') {
            const parts = payload.text.split('\t')
            findQuery = parts[0] ?? ''
            findCaseSensitive = parts[1] === '1'
            findWholeWord = parts[2] === '1'
          }
          updateFindResult(payload.command === 'findPrev')
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'replaceOne' || payload.command === 'replaceAll') {
          const parts = String(payload.text ?? '').split('\t')
          findQuery = parts[0] ?? ''
          findReplace = parts[1] ?? ''
          findCaseSensitive = parts[2] === '1'
          findWholeWord = parts[3] === '1'
          if (payload.command === 'replaceOne') replaceCurrent()
          else replaceEveryMatch()
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'findClose') {
          closeFindBar()
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setLanguage') {
          if (typeof payload.text === 'string') setMarkleafLanguage(payload.text)
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setSourceIndent') {
          const width = Number(payload.text) || 2
          sourceIndentWidth = Math.max(1, Math.min(8, Math.round(width)))
          sourceEditor?.setIndentWidth(sourceIndentWidth)
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setAutoHideScrollbar') {
          applyAutoHideScrollbar(payload.text === '1')
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'exportSelection') {
          send('selectionExport', getSelectionExport(), message.requestId)
          break
        }
        if (payload.command === 'formatPainter') {
          let success = false
          if (sourceMode) {
            success = false
          } else if (formatPainter.isArmed) {
            // 再次点击 = 关闭（对齐 Word 的切换语义）。
            formatPainter.cancel()
            success = true
          } else {
            success = formatPainter.arm(editor)
          }
          updateFormatPainterCursor()
          if (message.requestId) send('commandResult', { success }, message.requestId)
          sendEditorState()
          break
        }
        if (payload.command === 'formatPainterArm') {
          let success = false
          if (!sourceMode) {
            if (contextMenuSelection) {
              editor.commands.setTextSelection(contextMenuSelection)
            }
            success = formatPainter.arm(editor)
          }
          contextMenuSelection = null
          updateFormatPainterCursor()
          if (message.requestId) send('commandResult', { success }, message.requestId)
          sendEditorState()
          break
        }
        if (payload.command === 'formatPainterApply') {
          const success = executeFormatPainterApply(editor, formatPainter, sourceMode)
          updateFormatPainterCursor()
          if (message.requestId) send('commandResult', { success }, message.requestId)
          sendEditorState()
          break
        }
        if (payload.command === 'exportDocument') {
          if (typeof payload.text === 'string') {
            let options: {
              format?: unknown
              style?: unknown
              header?: unknown
              footer?: unknown
              fontSize?: unknown
              lineHeight?: unknown
              maxWidth?: unknown
              colorSchemeCss?: unknown
            }
            try { options = JSON.parse(payload.text) as Record<string, unknown> } catch { break }
            const style = typeof options.style === 'string' ? options.style : 'serif'
            const format = typeof options.format === 'string' ? options.format : 'html'
            const header = typeof options.header === 'string' ? options.header : ''
            const footer = typeof options.footer === 'string' ? options.footer : ''
            const fontSize = typeof options.fontSize === 'number' ? options.fontSize : 16
            const lineHeight = typeof options.lineHeight === 'number' ? options.lineHeight : 1.6
            const maxWidth = typeof options.maxWidth === 'number' ? options.maxWidth : 820
            const colorSchemeCss = typeof options.colorSchemeCss === 'string' ? options.colorSchemeCss : ''
            const html = generateExportHtml(style, format, header, footer, fontSize, lineHeight, maxWidth, colorSchemeCss)
            send('exportContent', { html }, message.requestId)
          }
          break
        }
        const coordinates = typeof payload.clientX === 'number' && typeof payload.clientY === 'number'
          ? { left: payload.clientX, top: payload.clientY }
          : undefined
        const commandText = typeof payload.text === 'string' ? payload.text : undefined
        const success = sourceMode
          ? payload.command === 'deleteSelection'
            ? sourceEditor?.deleteSelection() ?? false
            : payload.command === 'pasteText' && commandText !== undefined
              ? sourceEditor?.replaceSelection(commandText) ?? false
              : false
          : executeEditorCommand(
            editor,
            payload.command,
            commandText,
            coordinates,
            payload.applyToCurrentTextBlockWhenEmpty === true,
          )
        if (message.requestId) {
          send('commandResult', { success }, message.requestId)
        }
        sendEditorState()
      }
      break
    }
  }
}

window.chrome?.webview?.addEventListener('message', (event) => handleMessage(event.data))

window.addEventListener('error', (event) => {
  send('error', { message: event.message || 'Unhandled frontend error.' })
})

window.addEventListener('unhandledrejection', () => {
  send('error', { message: 'Unhandled frontend promise rejection.' })
})

// Ctrl+滚轮由宿主接管缩放（WebView2 内置缩放已禁用），这里阻止页面滚动并把
// 滚动方向上报给宿主，避免浏览器在合成器层面吞掉该输入。
window.addEventListener(
  'wheel',
  (event) => {
    if (!event.ctrlKey) {
      return
    }
    event.preventDefault()
    send('zoomWheel', { deltaY: event.deltaY })
  },
  { passive: false },
)

// 自动隐藏滚动条：滚动时或鼠标移至右边缘时显示滑块，停止后约 800ms 隐藏。
// 同时操作 html 和 body，覆盖不同 overflow 归属场景下的滚动条。
const isAutoHideScrollbarActive = () =>
  document.documentElement.classList.contains('markleaf-auto-hide-scrollbar')

const onScrollShow = () => {
  if (!isAutoHideScrollbarActive()) {
    return
  }
  showScrollbar()
}
window.addEventListener('scroll', onScrollShow, { passive: true, capture: true })
document.addEventListener('scroll', onScrollShow, { passive: true, capture: true })

window.addEventListener(
  'mousemove',
  (event) => {
    if (!isAutoHideScrollbarActive()) {
      return
    }
    if (event.clientX >= window.innerWidth - 20) {
      showScrollbar()
    }
  },
  { passive: true },
)

function showScrollbar(): void {
  document.documentElement.classList.add('markleaf-scrolling')
  document.body.classList.add('markleaf-scrolling')
  window.clearTimeout(scrollbarHideTimer)
  scrollbarHideTimer = window.setTimeout(() => {
    document.documentElement.classList.remove('markleaf-scrolling')
    document.body.classList.remove('markleaf-scrolling')
  }, 800)
}

function applyAutoHideScrollbar(enabled: boolean): void {
  document.documentElement.classList.toggle('markleaf-auto-hide-scrollbar', enabled)
  document.body.classList.toggle('markleaf-auto-hide-scrollbar', enabled)
  if (!enabled) {
    document.documentElement.classList.remove('markleaf-scrolling')
    document.body.classList.remove('markleaf-scrolling')
  }
}

// ---- i18n：查找栏文案（跟随宿主语言，zh-Hans 为默认） ----
const FIND_BAR_STRINGS: Record<string, Record<string, string>> = {
  'zh-Hans': { find: '查找', replaceWith: '替换为', prev: '上一个', next: '下一个', replace: '替换', replaceAll: '全部替换', close: '关闭', case: '区分大小写', whole: '全词', closeAria: '关闭查找栏' },
  'zh-Hant': { find: '尋找', replaceWith: '取代為', prev: '上一個', next: '下一個', replace: '取代', replaceAll: '全部取代', close: '關閉', case: '區分大小寫', whole: '全詞', closeAria: '關閉搜尋列' },
  en: { find: 'Find', replaceWith: 'Replace with', prev: 'Previous', next: 'Next', replace: 'Replace', replaceAll: 'Replace All', close: 'Close', case: 'Case Sensitive', whole: 'Whole Word', closeAria: 'Close Find Bar' },
  ja: { find: '検索', replaceWith: '置換後の文字列', prev: '前へ', next: '次へ', replace: '置換', replaceAll: 'すべて置換', close: '閉じる', case: '大文字と小文字を区別', whole: '単語全体', closeAria: '検索バーを閉じる' },
}

function applyFindBarLanguage(lang: string): void {
  const table: Record<string, string> = FIND_BAR_STRINGS[lang] ?? FIND_BAR_STRINGS['zh-Hans'] ?? {}
  const findInput = document.getElementById('find-input') as HTMLInputElement | null
  const replaceInput = document.getElementById('replace-input') as HTMLInputElement | null
  if (findInput) {
    findInput.placeholder = table.find ?? ''
    findInput.setAttribute('aria-label', table.find ?? '')
  }
  if (replaceInput) {
    replaceInput.placeholder = table.replaceWith ?? ''
    replaceInput.setAttribute('aria-label', table.replaceWith ?? '')
  }
  const setText = (id: string, text: string) => {
    const el = document.getElementById(id)
    if (el) el.textContent = text
  }
  setText('find-previous', table.prev ?? '')
  setText('find-next', table.next ?? '')
  setText('replace-one', table.replace ?? '')
  setText('replace-all', table.replaceAll ?? '')
  setText('find-close', table.close ?? '')
  const setLabelText = (labelId: string, inputId: string, text: string) => {
    const label = document.getElementById(labelId)
    const input = document.getElementById(inputId)
    if (label) label.textContent = text
    if (input) input.setAttribute('aria-label', text)
  }
  setLabelText('find-case-label', 'find-case', table.case ?? '')
  setLabelText('find-whole-label', 'find-whole', table.whole ?? '')
  const closeBtn = document.getElementById('find-close')
  if (closeBtn) closeBtn.setAttribute('aria-label', table.closeAria ?? '')
}

let markleafLanguage = 'zh-Hans'

// 查找状态：由原生查找面板（FindPanelController）通过命令驱动
let findQuery = ''
let findReplace = ''
let findCaseSensitive = false
let findWholeWord = false
function setMarkleafLanguage(lang: string): void {
  markleafLanguage = lang
  applyFindBarLanguage(lang)
}

applyFindBarLanguage(markleafLanguage)
send('ready')

;(window as any).__markleaf_tab__ = (shift = false) => {
  if (sourceEditor) {
    shift ? sourceEditor.insertShiftTab() : sourceEditor.insertTab()
  }
}

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

type StyleEntry = { id: string; css: string; dependsOn?: string }

function injectStyleSheet(id: string, css: string): void {
  let style = document.getElementById(id) as HTMLStyleElement | null
  if (!style) {
    style = document.createElement('style')
    style.id = id
    document.head.appendChild(style)
  }
  style.textContent = css
}

function resolveStyle(styleId: string): { rootClass: string; css: string } {
  const def = styleCatalog.find((entry) => entry.id === styleId)
  if (!def) {
    return { rootClass: '', css: '' }
  }

  const classes: string[] = []
  const cssParts: string[] = []
  const seen = new Set<string>()

  const visit = (current: StyleEntry | undefined): void => {
    if (!current || seen.has(current.id)) {
      return
    }
    seen.add(current.id)
    // 依赖样式先于自身注入，保证自身规则在级联中后出现并覆盖依赖。
    if (current.dependsOn) {
      visit(styleCatalog.find((entry) => entry.id === current.dependsOn))
    }
    if (current.css.trim()) {
      classes.push(`markleaf-style-${current.id}`)
      cssParts.push(current.css)
    }
  }

  visit(def)
  return { rootClass: classes.join(' '), css: cssParts.join('\n') }
}

function applyMarkleafStyle(styleId: string): void {
  const resolved = resolveStyle(styleId)
  const toRemove = Array.from(editorMount.classList).filter((cls) => cls.startsWith('markleaf-style-'))
  editorMount.classList.remove(...toRemove)
  if (resolved.rootClass) {
    for (const cls of resolved.rootClass.split(' ')) {
      editorMount.classList.add(cls)
    }
  }
}

function generateExportHtml(
  style: string,
  format: string,
  header: string,
  footer: string,
  fontSize = 16,
  lineHeight = 1.6,
  maxWidth = 820,
  colorSchemeCss = '',
): string {
  const rawBodyHtml = sourceMode
    ? `<pre><code>${escapeHtml(sourceEditor?.getText() ?? '')}</code></pre>`
    : editor.getHTML()
  const bodyHtml = rawBodyHtml.replace(
    /https:\/\/assets\.local\/image\?path=([^"']+)/g,
    (_, encoded: string) => {
      try { return decodeURIComponent(encoded) } catch { return encoded }
    },
  )
  const isPdf = format === 'pdf'
  const resolved = resolveStyle(style)
  const rootClass = [
    resolved.rootClass,
    isPdf ? 'markleaf-export-pdf' : '',
  ].filter(Boolean).join(' ')

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>MarkLeaf 导出文档</title>
<style>
* { box-sizing: border-box; }
${baseCss}
${colorSchemeCss}
${resolved.css}
/* 导出文档的排版内边距（编辑器侧由 #editor 承担）。 */
.markleaf-document {
  padding: 44px 56px 96px;
}
:root {
  --ml-font-size: ${fontSize}px;
  --ml-line-height: ${lineHeight};
  --ml-max-width: ${maxWidth}px;
}
html { font-size: var(--ml-font-size); }
body { margin: 0; background: var(--bg-primary); }
/* ---- PDF export: let print-dialog margins control spacing ---- */
.markleaf-export-pdf .markleaf-document {
  padding-left: 5px;
  padding-right: 5px;
  width: 100%;
}
.markleaf-export-pdf.markleaf-style-print .markleaf-document {
  padding-left: 5px;
  padding-right: 5px;
}
.markleaf-export-pdf .export-header,
.markleaf-export-pdf .export-footer {
  padding-left: 5px;
  padding-right: 5px;
  width: 100%;
}

/* PDF export: prevent horizontal overflow (scrollbars) and wrap long lines. */
.markleaf-export-pdf .markleaf-document pre {
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
  word-break: break-all;
  overflow-x: hidden;
  box-decoration-break: clone;
}
.markleaf-export-pdf .markleaf-document pre code {
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
  word-break: break-all;
}
.markleaf-export-pdf .markleaf-document blockquote {
  box-decoration-break: clone;
}
.markleaf-export-pdf .markleaf-document table {
  width: 100%;
  table-layout: fixed;
  word-wrap: break-word;
  overflow-wrap: break-word;
}

.export-header, .export-footer {
  width: min(100%, var(--ml-max-width));
  margin: 0 auto;
  padding: 8px 56px;
}
.export-header { border-bottom: 1px solid #d8dee4; }
.export-footer { border-top: 1px solid #d8dee4; margin-top: 24px; }
</style>
</head>
<body>
<div id="export-root"${rootClass ? ` class="${rootClass}"` : ''}>
${header ? `<div class="export-header">${header}</div>` : ''}
<div class="markleaf-document">${bodyHtml}</div>
${footer ? `<div class="export-footer">${footer}</div>` : ''}
</div>
</body>
</html>`
}
