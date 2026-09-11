import '@markleaf/editor-core/styles.css'
import { resolveNativeCapabilities, nativeImageResources } from './native-capabilities'
import {
  Selection,
  createEditorInteractions,
  createReadingBehavior,
  getDocumentOutline,
  getActiveOutlinePosition,
  resolveTypographyStyle,
  resolveMermaidTheme,
  createEditor,
  clearFindHighlights,
  executeEditorCommand,
  exportEditorSelection,
  findInEditor,
  findFootnoteDefinitionBody,
  getEditorCommandPresentation,
  getEditorStatus,
  selectEditorContextAt,
  setEditorFocusMode,
  getMarkdown,
  setAutoConvertUnsafeEmphasis,
  setMarkdownEditingSettings,
  captureVisualSelection,
  collapseVisualSelection,
  getSourceModeJumpTarget,
  isLocalFileLink,
  replaceAllInEditor,
  replaceCurrentInEditor,
  replaceEditorDocument,
  pasteMarkdownText,
  pasteMarkdownTextWithResult,
  pasteClipboardContentWithResult,
  shouldParsePastedTextAsMarkdown,
  resetEditorViewport,
  setBlockHandleVisible,
  setBlockTypeLabels,
  setCodeBlockControlHandlers,
  setEditorSharedStrings,
  restoreVisualSelection,
  type VisualSelectionSnapshot,
} from '@markleaf/editor-core'
import {
  rerenderMermaidElements,
  setMermaidStrings,
  SourceEditor,
  type UnsafeEmphasisRequest,
  generateExportHtml as generateSharedExportHtml,
  escapeHtml as escapeExportHtml,
  isPlainTextDocumentType,
  type DocumentType,
  preserveViewportDuringLayoutChange,
  type ViewportAnchorReader,
  setImageResourceResolver,
  normalizeSharedEditorLanguage,
  sharedEditorStrings,
  isHostCommandAllowed,
} from '@markleaf/editor-core'
// 原生宿主消息协议不属于内核，由 macOS / Windows 适配层持有。
import {
  isHostMessage,
  isRestoreViewportPayload,
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
const sourceToggle = document.querySelector<HTMLButtonElement>('#source-toggle')!

const hostCapabilities = resolveNativeCapabilities(window.chrome?.webview?.hostPlatform)
document.documentElement.classList.toggle(
  'markleaf-themed-visual-selection',
  hostCapabilities.usesThemedVisualSelection,
)

let documentId: string = crypto.randomUUID()
let documentLoaded = false
let scrollRestoreGeneration = 0
let revision = 0
let compositionActive = false
let compositionChanged = false
let suppressUpdate = false
let lastOutlinePosition: number | null | undefined
let outlineTimer = 0
let sourceEditor: SourceEditor | null = null
let sourceMode = false
let sourceIndentWidth = 2
let visualSelectionBeforeSourceMode: VisualSelectionSnapshot | null = null
// 混合前端右键菜单（粗体/斜体/下划线工具栏）是否启用：由宿主下发，仅 Windows 端为 true。
let frontendFormatMenuEnabled = false
let documentType: DocumentType = 'markdown'
let readOnly = false
let editorFocusMode = false
let editorTypewriterMode = false
let autoConvertUnsafeEmphasis = true

setImageResourceResolver(nativeImageResources)
const editorCreationOptions = {
  themedVisualSelection: hostCapabilities.usesThemedVisualSelection,
  handlePaste: handleVisualEditorPaste,
  sourceContextMenu: ({ clientX, clientY }: { clientX: number; clientY: number }) => {
    sendEditorState()
    send('contextMenuRequested', { clientX, clientY, menuHeight: 0, readOnly, sourceMode: false, expandedSource: true,
      canStartFormatPainter: false, formatPainterArmed: false })
  },
}
let editor = createEditor(editorMount, '', false, editorCreationOptions)
setCodeBlockControlHandlers({
  editLanguage: (position, language) => send('codeBlockLanguageRequested', { position, language }),
  copyCode: text => send('copyCodeBlockRequested', { text }),
})
let contextMenuSelection: { from: number; to: number } | null = null
let lastVisualSelection = captureVisualSelection(editor)
const readingBehavior = createReadingBehavior(() => editor)
const scrollEditorCursorToCenter = readingBehavior.cursorMoved
function updateEditorTypewriterMode(scrollToCursor = true): void {
  readingBehavior.setTypewriter(editorTypewriterMode && !sourceMode && !readOnly, scrollToCursor)
}

function updateEditorFocusLine(): void {
  sourceMount.querySelectorAll<HTMLElement>('.markleaf-focus-dim, .markleaf-focus-line').forEach(element => {
    element.classList.remove('markleaf-focus-dim', 'markleaf-focus-line')
  })
  editorMount.classList.remove('markleaf-editor-focus-mode')
  sourceMount.classList.remove('markleaf-editor-focus-mode')
  if (!editorFocusMode || readOnly || sourceMode) return
  editorMount.classList.add('markleaf-editor-focus-mode')
}

// Native window activation and editor focus are separate states. Re-activating
// the window must not resurrect a caret until the editor itself is focused.
let nativeWindowActive = true
const updateCaretVisibility = (): void => {
  const windowActive = nativeWindowActive
    && document.hasFocus()
    && document.visibilityState !== 'hidden'
  const activeElement = document.activeElement
  const visualHasFocus = activeElement !== null && editor.view.dom.contains(activeElement)
  const sourceHasFocus = sourceEditor !== null
    && activeElement !== null
    && sourceEditor.view.dom.contains(activeElement)
  const visualFocused = !readOnly && !editorMount.hidden && visualHasFocus
  const sourceFocused = !readOnly && !sourceMount.hidden && sourceHasFocus
  document.documentElement.classList.toggle('markleaf-window-inactive', !windowActive)
  document.documentElement.classList.toggle(
    'markleaf-editor-caret-visible',
    windowActive && (visualFocused || sourceFocused),
  )
}

const setNativeWindowActive = (active: boolean): void => {
  nativeWindowActive = active
  if (!active) {
    const activeElement = document.activeElement
    if (activeElement && (editorMount.contains(activeElement) || sourceMount.contains(activeElement))) {
      ;(activeElement as HTMLElement).blur()
    }
  }
  updateCaretVisibility()
}

declare global {
  interface Window {
    __markleafSetWindowActive?: (active: boolean) => void
  }
}

window.__markleafSetWindowActive = setNativeWindowActive
window.addEventListener('blur', () => setNativeWindowActive(false))
window.addEventListener('focus', () => setNativeWindowActive(true))
document.addEventListener('visibilitychange', updateCaretVisibility)
document.addEventListener('focusin', updateCaretVisibility)
document.addEventListener('focusout', () => window.setTimeout(updateCaretVisibility, 0))
updateCaretVisibility()

const interactions = createEditorInteractions({
  mount: editorMount, getEditor: () => editor, enabled: () => !sourceMode && !readOnly,
  onMenu: (position, rect) => send('blockMenuRequested', { clientX: rect.left, clientY: rect.bottom + 10, position }),
  label: sharedEditorStrings('zh-Hans', hostCapabilities.primaryActivationModifier).blockHandleAria,
  onStateChanged: () => sendEditorState(),
  links: {
    primaryModifier: hostCapabilities.primaryActivationModifier,
    openLink: url => send('openLink', { url }),
    missingFootnote: (kind, label) => send(kind === 'definition' ? 'footnoteDefinitionMissing' : 'footnoteReferenceMissing', { label }),
  },
})
const blockHandleButton = interactions.button
const ensureBlockHandleOverlay = interactions.ensure
const updateBlockHandleOverlay = interactions.update
window.addEventListener('unload', () => { interactions.dispose(); readingBehavior.dispose() })

let baseCss = ''
let styleCatalog: { id: string; css: string; dependsOn?: string }[] = []

type VisualVariablePayload = {
  lineHeight: string
  fontSize: string
  maxWidth: string
  sourceFontSize: string
  sourceFontFamily: string
  cjkLanguage: string
  visualCjkAutoSpacing: boolean
  ignoreMaxWidth: boolean
  usePointerAnchor?: boolean
  anchorX?: number | null
  anchorY?: number | null
}

declare global {
  interface Window {
    __markleafApplyVisualVariables?: (payload: VisualVariablePayload) => void
  }
}

const currentCaretViewportAnchor: ViewportAnchorReader = () => {
  try {
    if (sourceMode && sourceEditor) {
      const head = sourceEditor.view.state.selection.main.head
      const coords = sourceEditor.view.coordsAtPos(head)
      if (coords && coords.bottom >= 0 && coords.top <= window.innerHeight) {
        return {
          top: (coords.top + coords.bottom) / 2,
          container: sourceEditor.view.scrollDOM,
        }
      }
    }

    const coords = editor.view.coordsAtPos(editor.state.selection.head)
    if (coords.bottom < 0 || coords.top > window.innerHeight) {
      return null
    }
    return { top: (coords.top + coords.bottom) / 2 }
  } catch {
    // The editor can be between document replacement and its first layout pass.
    // preserveViewportDuringLayoutChange will fall back to a visible block anchor.
    return null
  }
}

type PointerPoint = { x: number; y: number }

// Keep the actual mouse/pointer location independent from the text selection. Menu and
// keyboard zoom commands do not carry an event coordinate, so they use the latest point seen
// by the editor. Wheel/pinch events may provide an explicit point from the native shim.
let lastPointerPoint: PointerPoint | null = null
const rememberPointerPoint = (event: MouseEvent | PointerEvent) => {
  if (Number.isFinite(event.clientX) && Number.isFinite(event.clientY)) {
    lastPointerPoint = { x: event.clientX, y: event.clientY }
  }
}
window.addEventListener('mousemove', rememberPointerPoint, { passive: true })
window.addEventListener('pointermove', rememberPointerPoint, { passive: true })

function pointerViewportAnchor(explicitPoint?: PointerPoint): ViewportAnchorReader {
  const point = explicitPoint ?? lastPointerPoint
  if (!point) {
    return () => null
  }

  try {
    if (sourceMode && sourceEditor) {
      const position = sourceEditor.view.posAtCoords({ x: point.x, y: point.y })
      if (position === null) {
        return () => null
      }
      const readTop = () => {
        try {
          const coords = sourceEditor?.view.coordsAtPos(position)
          return coords ? (coords.top + coords.bottom) / 2 : Number.NaN
        } catch {
          return Number.NaN
        }
      }
      const top = readTop()
      if (!Number.isFinite(top)) {
        return () => null
      }
      return () => ({ top, container: sourceEditor?.view.scrollDOM, readTop })
    }

    const resolved = editor.view.posAtCoords({ left: point.x, top: point.y })
    if (!resolved) {
      return () => null
    }
    const position = resolved.pos
    const readTop = () => {
      try {
        const coords = editor.view.coordsAtPos(position)
        return (coords.top + coords.bottom) / 2
      } catch {
        return Number.NaN
      }
    }
    const top = readTop()
    if (!Number.isFinite(top)) {
      return () => null
    }
    return () => ({ top, readTop })
  } catch {
    // The editor can be between document replacement and its first layout pass.
    return () => null
  }
}

// The native host changes these variables for zoom and typography settings. Zoom uses the
// actual mouse pointer as its anchor; other visual changes retain the active caret position.
window.__markleafApplyVisualVariables = (payload) => {
  const explicitPoint = Number.isFinite(payload.anchorX) && Number.isFinite(payload.anchorY)
    ? { x: payload.anchorX as number, y: payload.anchorY as number }
    : undefined
  const anchorReader = payload.usePointerAnchor === true
    ? pointerViewportAnchor(explicitPoint)
    : currentCaretViewportAnchor
  preserveViewportDuringLayoutChange(() => {
    document.documentElement.style.setProperty('--ml-line-height', payload.lineHeight)
    document.documentElement.style.setProperty('--ml-font-size', payload.fontSize)
    document.documentElement.style.setProperty('--ml-max-width', payload.maxWidth)
    document.documentElement.style.setProperty('--ml-source-font-size', payload.sourceFontSize)
    document.documentElement.style.setProperty('--ml-source-font-family', payload.sourceFontFamily)
    document.documentElement.setAttribute('lang', payload.cjkLanguage)
    document.documentElement.style.setProperty('--ml-cjk-lang', payload.cjkLanguage)
    document.documentElement.classList.toggle('markleaf-cjk-autospace', payload.visualCjkAutoSpacing)
    document.documentElement.classList.toggle('markleaf-ignore-max-width', payload.ignoreMaxWidth === true)
  }, anchorReader)
}

let editorLoc: Record<string, string> = {}

function applyAlertTitleLocalization(loc: Record<string, string>): void {
  const root = document.documentElement
  root.style.setProperty('--markleaf-alert-note-title', JSON.stringify(loc.alertNote ?? '备注'))
  root.style.setProperty('--markleaf-alert-tip-title', JSON.stringify(loc.alertTip ?? '提示'))
  root.style.setProperty('--markleaf-alert-important-title', JSON.stringify(loc.alertImportant ?? '重要'))
  root.style.setProperty('--markleaf-alert-warning-title', JSON.stringify(loc.alertWarning ?? '警告'))
  root.style.setProperty('--markleaf-alert-caution-title', JSON.stringify(loc.alertCaution ?? '注意'))
}

function applyFindBarLocalization(loc: Record<string, string>): void {
  editorLoc = loc
  applyAlertTitleLocalization(loc)
  promoteHeadingButton.textContent = loc.formatPromoteHeading ?? '标+'
  promoteHeadingButton.ariaLabel = loc.formatPromoteHeading ?? 'Promote heading'
  demoteHeadingButton.textContent = loc.formatDemoteHeading ?? '标-'
  demoteHeadingButton.ariaLabel = loc.formatDemoteHeading ?? 'Demote heading'
}

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

function getEditorScrollTop(): number {
  const value = sourceMode && sourceEditor
    ? sourceEditor.view.scrollDOM.scrollTop
    : Math.max(
      document.scrollingElement?.scrollTop ?? 0,
      document.documentElement.scrollTop ?? 0,
      document.body.scrollTop ?? 0,
      editorMount.scrollTop ?? 0,
    )
  return Number.isFinite(value) && value >= 0 ? value : 0
}

function restoreEditorScrollTop(value: unknown): void {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) return
  const top = Math.max(0, value)
  if (sourceMode && sourceEditor) {
    sourceEditor.view.scrollDOM.scrollTop = top
    return
  }
  const scrollingElement = document.scrollingElement ?? document.documentElement
  scrollingElement.scrollTop = top
  document.documentElement.scrollTop = top
  document.body.scrollTop = top
}

function restoreEditorScrollTopAfterLayout(value: unknown): void {
  const generation = ++scrollRestoreGeneration
  const restore = () => {
    if (generation === scrollRestoreGeneration) restoreEditorScrollTop(value)
  }
  restore()
  window.requestAnimationFrame(() => window.requestAnimationFrame(restore))
  window.setTimeout(restore, 50)
  window.setTimeout(restore, 150)
  window.setTimeout(restore, 300)
  if (document.fonts) void document.fonts.ready.then(restore)
  for (const image of Array.from(editorMount.querySelectorAll<HTMLImageElement>('img'))) {
    if (!image.complete) image.addEventListener('load', restore, { once: true })
  }
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
  send('outlineChanged', { headings: sourceMode ? [] : getDocumentOutline(editor) })
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
  sendOutlineSelection(sourceMode ? null : getActiveOutlinePosition(editor, 'cursor'))
}

function sendOutlineSelectionFromScroll(): void {
  sendOutlineSelection(sourceMode ? null : getActiveOutlinePosition(editor, 'scroll'))
}

function sendCommandState(): void {
  const sourceSelection = sourceEditor?.view.state.selection.main
  const state = getEditorCommandPresentation(editor, {
    readOnly, sourceMode, documentType, focusMode: editorFocusMode, typewriterMode: editorTypewriterMode,
  }, {
    ...(sourceEditor && sourceSelection ? {
      canUndo: sourceEditor.canUndo(), canRedo: sourceEditor.canRedo(), hasSelection: !sourceSelection.empty,
    } : {}),
    ...interactions.state(),
  })
  send('commandStateChanged', { ...state, sourceMode, readOnly })
}

function sendEditorStatus(): void {
  if (sourceEditor) {
    send('editorStatusChanged', sourceEditor.getStatus())
    return
  }
  send('editorStatusChanged', getEditorStatus(editor))
}

function sendEditorState(): void {
  sendCommandState()
  sendEditorStatus()
}

const updateFormatPainterCursor = () => interactions.update()

function bindEditorEvents(targetEditor: typeof editor): void {
  targetEditor.on('update', ({ transaction }) => {
    // 仅装饰/元数据事务（如块手柄高亮）不改变文档，跳过脏标记与大纲刷新。
    if (!transaction.docChanged) {
      return
    }
    if (suppressUpdate || compositionActive) {
      if (compositionActive) {
        compositionChanged = true
      }
      return
    }

    revision += 1
    send('dirtyChanged', { dirty: true })
    scheduleOutline()
    updateBlockHandleOverlay()
    sendEditorState()
  })

  targetEditor.on('selectionUpdate', () => {
    updateEditorFocusLine()
    if (!suppressUpdate && targetEditor === editor) scrollEditorCursorToCenter()
    if (!compositionActive) {
      lastVisualSelection = captureVisualSelection(targetEditor)
      send('selectionChanged', {
        from: targetEditor.state.selection.from,
        to: targetEditor.state.selection.to,
        sourceMode: false,
      })
      updateBlockHandleOverlay()
      sendEditorState()
      sendOutlineSelectionFromCursor()
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
      updateBlockHandleOverlay()
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

function sendSourceSelection(from: number, to: number): void {
  send('selectionChanged', { from, to, sourceMode: true })
}

function requestUnsafeEmphasisResolution(request: UnsafeEmphasisRequest): void {
  send('unsafeEmphasisRequested', request, request.id)
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
  interactions.cancel()
  updateFormatPainterCursor()
  if (enabled) {
    visualSelectionBeforeSourceMode = captureVisualSelection(editor)
    const jumpTarget = getSourceModeJumpTarget(editor)
    sourceEditor = new SourceEditor(sourceMount, getMarkdown(editor), markSourceChanged, sourceIndentWidth, readOnly, requestUnsafeEmphasisResolution, documentType === 'markdown', sendSourceSelection)
    editorMount.hidden = true
    sourceMount.hidden = false
    sourceMode = true
    updateEditorTypewriterMode()
    updateEditorFocusLine()
    if (jumpTarget.type === 'tableEnd') {
      sourceEditor.setSelectionToTableEnd(jumpTarget.tableIndex, true)
    } else if (jumpTarget.type === 'afterTable') {
      sourceEditor.setSelectionAfterTableRenderedLines(jumpTarget.tableIndex, jumpTarget.lineOffset, true)
    } else {
      sourceEditor.setSelectionToRenderedLineEnd(jumpTarget.line, true)
    }
  } else {
    const markdown = sourceEditor?.getText() ?? getMarkdown(editor)
    const visualSelection = visualSelectionBeforeSourceMode
    sourceEditor?.destroy()
    sourceEditor = null
    visualSelectionBeforeSourceMode = null
    suppressUpdate = true
    editor = replaceEditorDocument(editor, editorMount, markdown, readOnly, editorCreationOptions)
    bindEditorEvents(editor)
    ensureBlockHandleOverlay()
    suppressUpdate = false
    sourceMount.hidden = true
    editorMount.hidden = false
    sourceMode = false
    updateEditorTypewriterMode()
    if (editorFocusMode && !readOnly) setEditorFocusMode(editor, true)
    updateEditorFocusLine()
    restoreVisualSelection(editor, visualSelection, true)
    lastVisualSelection = captureVisualSelection(editor)
    scheduleOutline()
    sendOutlineSelectionFromCursor()
  }
  updateCaretVisibility()
  updateBlockHandleOverlay()
  sendEditorState()
}

function closeFind(): void {
  if (!sourceMode) clearFindHighlights(editor)
  if (sourceMode) sourceEditor?.focus()
  else editor.commands.focus()
}

function updateFindResult(backwards: boolean): void {
  const result = sourceMode
    ? sourceEditor?.find(findQuery, findCaseSensitive, findWholeWord, backwards) ?? { current: 0, total: 0 }
    : findInEditor(editor, findQuery, findCaseSensitive, findWholeWord, backwards)
  send('findResult', result)
}

function replaceCurrent(): void {
  const result = sourceMode
    ? sourceEditor?.replaceCurrent(findQuery, findReplace, findCaseSensitive, findWholeWord)
      ?? { current: 0, total: 0 }
    : replaceCurrentInEditor(editor, findQuery, findReplace, findCaseSensitive, findWholeWord)
  send('findResult', result)
}

function replaceEveryMatch(): void {
  const count = sourceMode
    ? sourceEditor?.replaceAll(findQuery, findReplace, findCaseSensitive, findWholeWord) ?? 0
    : replaceAllInEditor(editor, findQuery, findReplace, findCaseSensitive, findWholeWord)
  send('findResult', { current: count, total: count, replaced: count })
}

sourceToggle.addEventListener('click', () => setSourceMode(!sourceMode))
window.addEventListener('keydown', event => {
  if (event.defaultPrevented) return
  if (event.key === 'Escape') {
    // Esc 折叠当前选区：视觉/源码编辑器的高亮装饰随选区清空而消失。
    const collapsed = sourceMode
      ? sourceEditor?.collapseSelection() ?? false
      : collapseVisualSelection(editor)
    if (collapsed) {
      event.preventDefault()
      sendEditorState()
    }
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

// ---- 链接 / 注释角标悬停提示（手形光标 + 提示文本） ----
const editorTooltip = document.createElement('div')
editorTooltip.className = 'editor-tooltip'
editorTooltip.hidden = true
document.body.appendChild(editorTooltip)

let tooltipKind: 'link' | 'footnote' | null = null
let tooltipIsLocalFile = false
let tooltipHideTimer = 0

function editorTooltipTexts(): { link: string; file: string; footnote: string; footnoteNotFound: string } {
  const strings = sharedEditorStrings(
    markleafLanguage,
    hostCapabilities.primaryActivationModifier,
  )
  return {
    link: strings.linkTooltip,
    file: strings.fileLinkTooltip,
    footnote: strings.footnoteTooltip,
    footnoteNotFound: strings.footnoteNotFound,
  }
}

function hideEditorTooltip(delay = 0): void {
  window.clearTimeout(tooltipHideTimer)
  if (delay > 0) {
    tooltipHideTimer = window.setTimeout(() => { editorTooltip.hidden = true }, delay)
  } else {
    editorTooltip.hidden = true
  }
}

function positionEditorTooltip(event: MouseEvent): void {
  window.clearTimeout(tooltipHideTimer)
  editorTooltip.hidden = false
  const rect = editorTooltip.getBoundingClientRect()
  const offset = 14
  let left = event.clientX + offset
  let top = event.clientY + offset
  if (left + rect.width > window.innerWidth - 8) left = Math.max(8, event.clientX - rect.width - offset)
  if (top + rect.height > window.innerHeight - 8) top = Math.max(8, event.clientY - rect.height - offset)
  editorTooltip.style.left = `${left}px`
  editorTooltip.style.top = `${top}px`
}

function buildEditorTooltip(kind: 'link' | 'footnote', detail: string | null, isLocalFile = false): void {
  const texts = editorTooltipTexts()
  editorTooltip.textContent = ''

  const detailEl = document.createElement('div')
  detailEl.className = 'editor-tooltip-definition'
  if (kind === 'link') {
    detailEl.textContent = detail ?? ''
  } else {
    const body = detail?.trim() ?? ''
    detailEl.textContent = body.length > 0 ? detail! : texts.footnoteNotFound
  }
  editorTooltip.appendChild(detailEl)

  const hint = document.createElement('div')
  hint.className = 'editor-tooltip-hint'
  hint.textContent = kind === 'link'
    ? (isLocalFile ? texts.file : texts.link)
    : texts.footnote
  editorTooltip.appendChild(hint)
}

function updateEditorTooltip(event: MouseEvent): void {
  const target = event.target
  if (!(target instanceof Element)) {
    tooltipKind = null
    hideEditorTooltip()
    return
  }
  const footnoteEl = target.closest<HTMLElement>('sup[data-footnote-ref]')
  const anchorEl = target.closest<HTMLAnchorElement>('a[href]')
  const kind = footnoteEl ? 'footnote' : anchorEl ? 'link' : null
  if (kind) {
    const localFile = kind === 'link' && isLocalFileLink(anchorEl?.getAttribute('href') ?? '')
    const detail = kind === 'footnote'
      ? findFootnoteDefinitionBody(editor, footnoteEl!.getAttribute('data-footnote-ref') ?? '')
      : (anchorEl?.getAttribute('href') ?? '')
    if (tooltipKind !== kind || tooltipIsLocalFile !== localFile) {
      tooltipKind = kind
      tooltipIsLocalFile = localFile
      buildEditorTooltip(kind, detail, localFile)
    }
    positionEditorTooltip(event)
  } else {
    tooltipKind = null
    hideEditorTooltip()
  }
}

editorMount.addEventListener('mousemove', updateEditorTooltip)
editorMount.addEventListener('mouseleave', () => {
  tooltipKind = null
  hideEditorTooltip()
})
editorMount.addEventListener('mousedown', () => {
  tooltipKind = null
  hideEditorTooltip()
})

const formatMenu = document.createElement('div')
formatMenu.id = 'format-menu'
formatMenu.className = 'format-menu'
formatMenu.hidden = true
const formatButtons: Array<{ command: string; glyph: string; label: string }> = [
  { command: 'toggleBold', glyph: '', label: 'Bold' },
  { command: 'toggleItalic', glyph: '', label: 'Italic' },
  { command: 'toggleUnderline', glyph: '', label: 'Underline' },
  { command: 'toggleStrike', glyph: '\uEDE0', label: 'Strikethrough' },
  { command: 'toggleHighlight', glyph: '\uE7E6', label: 'Text highlight' },
]
const formatButtonElements: HTMLButtonElement[] = []

// 原生菜单弹出期间是模态的并捕获鼠标，按下按钮时该 mousedown 会先关闭原生菜单、
// 再透传给 WebView2。这里用 mousedown 而非 click，确保在菜单关闭、宿主发送
// hideFormatMenu 之前就触发命令，避免按钮在 click 的 down/up 之间被隐藏而失效。
function attachFormatCommand(button: HTMLButtonElement, command: string): void {
  button.addEventListener('mousedown', (event) => {
    if (event.button !== 0) {
      return
    }
    event.preventDefault()
    if (sourceMode) {
      return
    }
    if (command === 'formatPainter') {
      if (contextMenuSelection) editor.commands.setTextSelection(contextMenuSelection)
      if (interactions.state().formatPainterArmed) interactions.cancel()
      else interactions.arm()
      contextMenuSelection = null
      updateFormatPainterCursor()
      hideFormatMenu()
      sendEditorState()
      return
    }
    executeEditorCommand(editor, command)
    hideFormatMenu()
    sendEditorState()
  })
}

for (const entry of formatButtons) {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'format-menu-button'
  button.dataset.command = entry.command
  button.textContent = entry.glyph
  button.setAttribute('aria-label', entry.label)
  attachFormatCommand(button, entry.command)
  formatButtonElements.push(button)
  formatMenu.appendChild(button)
}

// 标题场景下的垂直分割线 + “标+ / 标-” 按钮
const formatSeparator = document.createElement('div')
formatSeparator.className = 'format-menu-separator'
formatSeparator.hidden = true
formatMenu.appendChild(formatSeparator)

function createHeadingButton(command: string): HTMLButtonElement {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'format-menu-button format-menu-heading-button'
  button.hidden = true
  attachFormatCommand(button, command)
  formatMenu.appendChild(button)
  return button
}

const promoteHeadingButton = createHeadingButton('promoteHeading')
const demoteHeadingButton = createHeadingButton('demoteHeading')
const headingButtonElements = [promoteHeadingButton, demoteHeadingButton]

const clearFormatSeparator = document.createElement('div')
clearFormatSeparator.className = 'format-menu-separator'
formatMenu.appendChild(clearFormatSeparator)

const formatPainterButton = document.createElement('button')
formatPainterButton.type = 'button'
formatPainterButton.className = 'format-menu-button'
formatPainterButton.dataset.command = 'formatPainter'
formatPainterButton.textContent = '\uEC34'
formatPainterButton.setAttribute('aria-label', 'Format painter')
formatPainterButton.title = 'Format painter'
attachFormatCommand(formatPainterButton, 'formatPainter')
formatMenu.appendChild(formatPainterButton)

const clearFormatButton = document.createElement('button')
clearFormatButton.type = 'button'
clearFormatButton.className = 'format-menu-button'
clearFormatButton.dataset.command = 'clearFormat'
clearFormatButton.textContent = '\uE75C'
clearFormatButton.setAttribute('aria-label', 'Clear formatting')
clearFormatButton.title = 'Clear formatting'
attachFormatCommand(clearFormatButton, 'clearFormat')
formatMenu.appendChild(clearFormatButton)

document.body.appendChild(formatMenu)

function parseCssColor(value: string): [number, number, number] | null {
  const hex = value.match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i)
  if (hex) {
    const raw = hex[1]!
    const expanded = raw.length === 3 ? raw.split('').map((c) => c + c).join('') : raw
    return [
      parseInt(expanded.slice(0, 2), 16),
      parseInt(expanded.slice(2, 4), 16),
      parseInt(expanded.slice(4, 6), 16),
    ]
  }
  const rgb = value.match(/rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)/i)
  if (rgb) {
    return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3])]
  }
  return null
}

function isDarkTheme(): boolean {
  const value = getComputedStyle(document.documentElement).getPropertyValue('--bg-primary').trim()
  const rgb = parseCssColor(value)
  if (!rgb) {
    return false
  }
  const luminance = (0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]) / 255
  return luminance < 0.5
}

function syncThemeModeClass(): void {
  document.body.classList.toggle('markleaf-theme-dark', isDarkTheme())
}

let formatMenuHideTimer = 0

function showFormatMenu(
  clientX: number,
  clientY: number,
  state: ReturnType<typeof getEditorCommandPresentation>,
): void {
  window.clearTimeout(formatMenuHideTimer)
  formatMenu.classList.toggle('format-menu-dark', isDarkTheme())
  formatMenu.style.left = `${clientX}px`
  formatMenu.style.top = `${clientY}px`
  formatMenu.hidden = false
  void formatMenu.offsetWidth
  formatMenu.classList.add('format-menu-visible')

  for (const button of [...formatButtonElements, ...headingButtonElements]) {
    const action = state.actions[button.dataset.command ?? '']
    button.classList.toggle('format-menu-button-active', action?.checked === true)
    button.disabled = action?.enabled !== true
  }
  const isHeading = state.headingLevel !== null
  formatSeparator.hidden = !isHeading
  for (const button of headingButtonElements) button.hidden = !isHeading

}

function hideFormatMenu(): void {
  if (formatMenu.hidden) {
    return
  }
  formatMenu.classList.remove('format-menu-visible')
  window.clearTimeout(formatMenuHideTimer)
  formatMenuHideTimer = window.setTimeout(() => {
    formatMenu.hidden = true
  }, 60)
}

editorMount.addEventListener('contextmenu', (event) => {
  event.preventDefault()
  const documentBounds = editor.view.dom.getBoundingClientRect()
  const outsideDocument = event.clientX < documentBounds.left
    || event.clientX > documentBounds.right
    || event.clientY < documentBounds.top
    || event.clientY > documentBounds.bottom
  if (outsideDocument) {
    hideFormatMenu()
    send('contextMenuRequested', {
      clientX: event.clientX,
      clientY: event.clientY,
      menuHeight: 0,
      canStartFormatPainter: false,
      formatPainterArmed: false,
      readOnly,
      outsideDocument: true,
    })
    return
  }
  selectEditorContextAt(editor, { left: event.clientX, top: event.clientY })
  editor.commands.focus()
  contextMenuSelection = {
    from: editor.state.selection.from,
    to: editor.state.selection.to,
  }
  sendEditorState()
  const state = getEditorCommandPresentation(editor, { readOnly, sourceMode, documentType }, interactions.state())
  const showFormat = frontendFormatMenuEnabled && state.actions.toggleBold?.enabled === true
  if (showFormat) {
    showFormatMenu(event.clientX, event.clientY, state)
  } else {
    hideFormatMenu()
  }
  send('contextMenuRequested', {
    clientX: event.clientX,
    clientY: event.clientY,
    menuHeight: showFormat ? formatMenu.offsetHeight : 0,
    canStartFormatPainter: interactions.state().canStartFormatPainter,
    formatPainterArmed: !sourceMode && interactions.state().formatPainterArmed,
    readOnly,
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
    readOnly,
    sourceMode: true,
    expandedSource: false,
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

function handleVisualEditorPaste(event: ClipboardEvent): boolean {
  const clipboard = event.clipboardData
  if (Array.from(clipboard?.items ?? []).some((item) => item.type.startsWith('image/'))) {
    send('pasteImage', {})
    return true
  }
  if (readOnly || sourceMode || !clipboard) return false
  const plainText = clipboard.getData('text/plain')
  const html = clipboard.getData('text/html')
  if (!shouldParsePastedTextAsMarkdown(editor, plainText, html)) return false
  return pasteMarkdownText(editor, plainText)
}

async function handleMessage(value: unknown): Promise<void> {
  if (!isHostMessage(value)) {
    send('error', { message: 'Invalid host message.' })
    return
  }

  const message: HostMessage = value

  // 文档尚未加载时，宿主的会话 documentId 还是随机占位值，与前端不一致；
  // 此时 applyStyles/setAutoHideScrollbar 等文档无关的偏好推送必须放行。
  if (message.type !== 'loadDocument' && message.type !== 'setDocumentType' && message.type !== 'applyStyles' && message.type !== 'localizeFindBar'
      && documentLoaded && message.documentId !== documentId) {
    return
  }

  switch (message.type) {
    case 'localizeFindBar': {
      const payload = message.payload as Record<string, string>
      if (payload) applyFindBarLocalization(payload)
      break
    }
    case 'applyStyles': {
      const payload = message.payload as {
        baseCss?: unknown
        colorThemeCss?: unknown
        styles?: unknown
        activeStyle?: unknown
        frontendFormatMenu?: unknown
      }
      if (typeof payload?.frontendFormatMenu === 'boolean') {
        frontendFormatMenuEnabled = payload.frontendFormatMenu
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
      syncThemeModeClass()
      break
    }
    case 'loadDocument': {
      interactions.cancel()
      updateFormatPainterCursor()
      const payload = message.payload as {
        markdown?: unknown
        documentType?: unknown
        readOnly?: unknown
        initialDirty?: unknown
        visualSelection?: { from?: unknown; to?: unknown }
        sourceSelection?: { from?: unknown; to?: unknown }
        scrollTop?: unknown
        restoreViewState?: unknown
      }
      if (typeof payload?.markdown !== 'string') {
        send('error', { message: 'loadDocument requires a markdown string.' }, message.requestId)
        return
      }
      documentId = message.documentId
      documentLoaded = true
      revision = message.revision
      documentType = isPlainTextDocumentType(payload?.documentType) ? 'plainText' : 'markdown'
      readOnly = payload?.readOnly === true
      const restoreViewState = payload?.restoreViewState !== false
      suppressUpdate = true
      if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
      sourceEditor?.destroy()
      sourceEditor = null
      if (documentType === 'plainText') {
        sourceMode = true
        sourceMount.hidden = false
        editorMount.hidden = true
        sourceEditor = new SourceEditor(sourceMount, payload.markdown, markSourceChanged, sourceIndentWidth, readOnly, requestUnsafeEmphasisResolution, false, sendSourceSelection)
        if (restoreViewState && typeof payload.sourceSelection?.from === 'number' && typeof payload.sourceSelection?.to === 'number') {
          sourceEditor.setSelection(payload.sourceSelection.from, payload.sourceSelection.to)
        } else {
          sourceEditor.setSelection(0, 0)
        }
      } else {
        sourceMode = false
        sourceMount.hidden = true
        editorMount.hidden = false
        const visualSelection = typeof payload.visualSelection?.from === 'number'
          && typeof payload.visualSelection?.to === 'number'
          ? { from: payload.visualSelection.from, to: payload.visualSelection.to }
          : null
        editor = replaceEditorDocument(
          editor,
          editorMount,
          payload.markdown,
          readOnly,
          editorCreationOptions,
        )
        bindEditorEvents(editor)
        if (editorFocusMode && !readOnly) setEditorFocusMode(editor, true)
        updateEditorTypewriterMode(false)
        updateEditorFocusLine()
        ensureBlockHandleOverlay()
        if (restoreViewState && visualSelection) {
          // Restore the logical selection without focusing or scrolling it into
          // view. The document's saved scroll position is independent from the
          // caret and remains authoritative when switching tabs.
          const from = Math.max(0, Math.min(visualSelection.from, editor.state.doc.content.size))
          const to = Math.max(0, Math.min(visualSelection.to, editor.state.doc.content.size))
          editor.commands.setTextSelection({ from, to })
        } else {
          editor.view.dispatch(editor.state.tr.setSelection(Selection.atStart(editor.state.doc)))
        }
        lastVisualSelection = captureVisualSelection(editor)
      }
      suppressUpdate = false
      updateCaretVisibility()
      send('documentLoaded', undefined, message.requestId)
      if (payload.initialDirty === true) send('dirtyChanged', { dirty: true })
      restoreEditorScrollTopAfterLayout(restoreViewState ? payload.scrollTop : 0)
      updateBlockHandleOverlay()
      sendOutline()
      sendEditorState()
      sendOutlineSelectionFromCursor()
      break
    }
    case 'restoreViewport': {
      if (!documentLoaded || !isRestoreViewportPayload(message.payload)) break
      const payload = message.payload
      if (payload.selection) {
        if (sourceMode) sourceEditor?.setSelection(payload.selection.from, payload.selection.to)
        else restoreVisualSelection(editor, payload.selection)
      }
      if (typeof payload.scrollTop === 'number' && payload.scrollTop >= 0) {
        if (sourceMode) sourceEditor?.setScrollTop(payload.scrollTop)
        else restoreEditorScrollTop(payload.scrollTop)
      }
      break
    }
    case 'setDocumentType': {
      const payload = message.payload as { documentType?: unknown }
      const nextType = isPlainTextDocumentType(payload?.documentType) ? 'plainText' : 'markdown'
      if (nextType === documentType) break
      const markdown = getActiveMarkdown()
      interactions.cancel()
      updateFormatPainterCursor()
      suppressUpdate = true
      sourceEditor?.destroy()
      sourceEditor = null
      documentType = nextType
      if (nextType === 'plainText') {
        sourceMode = true
        sourceMount.hidden = false
        editorMount.hidden = true
        sourceEditor = new SourceEditor(sourceMount, markdown, markSourceChanged, sourceIndentWidth, readOnly, requestUnsafeEmphasisResolution, false, sendSourceSelection)
      } else {
        sourceMode = false
        sourceMount.hidden = true
        editorMount.hidden = false
        editor = replaceEditorDocument(
          editor,
          editorMount,
          markdown,
          readOnly,
          editorCreationOptions,
        )
        bindEditorEvents(editor)
        if (editorFocusMode && !readOnly) setEditorFocusMode(editor, true)
        updateEditorTypewriterMode()
        updateEditorFocusLine()
        ensureBlockHandleOverlay()
        resetEditorViewport(editor, editorMount)
        lastVisualSelection = captureVisualSelection(editor)
      }
      suppressUpdate = false
      updateCaretVisibility()
      updateBlockHandleOverlay()
      sendEditorState()
      sendOutline()
      sendOutlineSelectionFromCursor()
      break
    }
    case 'requestSnapshot':
      send('snapshot', { markdown: getActiveMarkdown(), scrollTop: getEditorScrollTop() }, message.requestId)
      break
    case 'unsafeEmphasisResponse': {
      const payload = message.payload as { action?: unknown }
      if (message.requestId && (payload?.action === 'literal' || payload?.action === 'html')) {
        sourceEditor?.resolveUnsafeEmphasis(message.requestId, payload.action)
      }
      break
    }
    case 'command': {
      const payload = message.payload as {
        command?: unknown
        text?: unknown
        html?: unknown
        clientX?: unknown
        clientY?: unknown
      }
      if (typeof payload?.command === 'string') {
        if (!isHostCommandAllowed(payload.command, { readOnly, documentType })) {
          if (message.requestId) send('commandResult', { success: false }, message.requestId)
          break
        }
        if (payload.command === 'toggleSourceMode') {
          setSourceMode(!sourceMode)
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setEditorFocusMode') {
          editorFocusMode = payload.text === '1'
          if (!sourceMode && !readOnly) setEditorFocusMode(editor, editorFocusMode)
          updateEditorFocusLine()
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setEditorTypewriterMode') {
          editorTypewriterMode = payload.text === '1'
          updateEditorTypewriterMode()
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'hideFormatMenu') {
          hideFormatMenu()
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setStyle') {
          applyMarkleafStyle(typeof payload.text === 'string' ? payload.text : 'serif')
          syncThemeModeClass()
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
          closeFind()
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
        if (payload.command === 'setAutoConvertUnsafeEmphasis') {
          autoConvertUnsafeEmphasis = payload.text !== '0'
          setAutoConvertUnsafeEmphasis(autoConvertUnsafeEmphasis)
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setMarkdownEditingSettings') {
          try {
            setMarkdownEditingSettings(JSON.parse(String(payload.text ?? '{}')))
          }
          catch {
            setMarkdownEditingSettings({})
          }
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setAutoHideScrollbar') {
          applyAutoHideScrollbar(payload.text === '1')
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'setBlockHandleVisible') {
          setBlockHandleVisible(editor, payload.text === '1')
          updateBlockHandleOverlay()
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
          } else if (interactions.state().formatPainterArmed) {
            // 再次点击 = 关闭（对齐 Word 的切换语义）。
            interactions.cancel()
            success = true
          } else {
            success = interactions.arm()
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
            success = interactions.arm()
          }
          contextMenuSelection = null
          updateFormatPainterCursor()
          if (message.requestId) send('commandResult', { success }, message.requestId)
          sendEditorState()
          break
        }
        if (payload.command === 'formatPainterApply') {
          const success = interactions.apply()
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
              visualCjkAutoSpacing?: unknown
              colorSchemeCss?: unknown
              title?: unknown
              keepTablesTogether?: unknown
              keepHeadingsWithNextBlock?: unknown
            }
            try { options = JSON.parse(payload.text) as Record<string, unknown> } catch { break }
            const style = typeof options.style === 'string' ? options.style : 'serif'
            const format = typeof options.format === 'string' ? options.format : 'html'
            const header = typeof options.header === 'string' ? options.header : ''
            const footer = typeof options.footer === 'string' ? options.footer : ''
            const fontSize = typeof options.fontSize === 'number' ? options.fontSize : 16
            const lineHeight = typeof options.lineHeight === 'number' ? options.lineHeight : 1.6
            const maxWidth = typeof options.maxWidth === 'number' ? options.maxWidth : 820
            const visualCjkAutoSpacing = typeof options.visualCjkAutoSpacing === 'boolean'
              ? options.visualCjkAutoSpacing
              : true
            const colorSchemeCss = typeof options.colorSchemeCss === 'string' ? options.colorSchemeCss : ''
            const title = typeof options.title === 'string' ? options.title : ''
            const keepTablesTogether = options.keepTablesTogether === true
            const keepHeadingsWithNextBlock = options.keepHeadingsWithNextBlock === true
            const rawBodyHtml = sourceMode
              ? `<pre><code>${escapeExportHtml(sourceEditor?.getText() ?? '')}</code></pre>`
              : editor.getHTML()
            const resolved = resolveStyle(style)
            const html = await generateSharedExportHtml({
              rawBodyHtml,
              resolved,
              format,
              mermaidTheme: resolveMermaidTheme(resolved.css),
              header,
              footer,
              fontSize,
              lineHeight,
              maxWidth,
              visualCjkAutoSpacing,
              colorSchemeCss,
              baseCss,
              title,
              // 宿主专属截图规则：WKWebView 需要滚动范围，WebView2 需要裁剪容器。
              imageCaptureMode: hostCapabilities.imageCaptureMode,
              keepTablesTogether,
              keepHeadingsWithNextBlock,
              editorLoc,
            })
            send('exportContent', { html }, message.requestId)
          }
          break
        }
        const coordinates = typeof payload.clientX === 'number' && typeof payload.clientY === 'number'
          ? { left: payload.clientX, top: payload.clientY }
          : undefined
        const commandText = typeof payload.text === 'string' ? payload.text : undefined
        const commandHtml = typeof payload.html === 'string' ? payload.html : undefined
        if (!sourceMode
          && (payload.command === 'indentListItem' || payload.command === 'outdentListItem')) {
          restoreVisualSelection(editor, lastVisualSelection)
        }
        let commandOutcome: string | undefined
        let commandError: string | undefined
        const success = sourceMode
          ? payload.command === 'undo'
            ? sourceEditor?.undo() ?? false
            : payload.command === 'redo'
              ? sourceEditor?.redo() ?? false
              : payload.command === 'deleteSelection'
            ? sourceEditor?.deleteSelection() ?? false
            : payload.command === 'pasteText' && commandText !== undefined
              ? sourceEditor?.replaceSelection(commandText) ?? false
            // 源码模式一律按字面文本插入：Markdown 与剪贴板内容都不解析语法。
            : (payload.command === 'pasteMarkdown' || payload.command === 'pasteClipboard')
                && commandText !== undefined
              ? (commandOutcome = 'plainText', sourceEditor?.replaceSelection(commandText) ?? false)
            : payload.command === 'insertMermaid'
              ? sourceEditor?.insertMermaidCodeBlock() ?? false
            : payload.command === 'selectAll'
                ? sourceEditor?.selectAll() ?? false
                : false
          : payload.command === 'pasteMarkdown' && commandText !== undefined
            ? (() => {
                const result = pasteMarkdownTextWithResult(editor, commandText)
                commandOutcome = result.outcome
                commandError = result.error
                return result.success
              })()
            : payload.command === 'pasteClipboard'
              ? (() => {
                  // 只在 HTML 可用（如 HTML-only 剪贴板来源）时也走同一策略。
                  const result = pasteClipboardContentWithResult(
                    editor,
                    commandText ?? '',
                    commandHtml ?? '',
                  )
                  commandOutcome = result.outcome
                  commandError = result.error
                  return result.success
                })()
              : executeEditorCommand(
                editor,
                payload.command,
                commandText,
                coordinates,
              )
        if (message.requestId) {
          send('commandResult', { success, outcome: commandOutcome, error: commandError }, message.requestId)
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

// Windows WebView2 仍从前端接管 Ctrl+滚轮；macOS 在 WKWebView 子类中原生处理
// 修饰键滚轮，避免全局非 passive 监听器让普通滚动退出 WebKit 异步滚动快路径。
if (hostCapabilities.installsFrontendWheelHandler) {
  window.addEventListener(
    'wheel',
    (event) => {
      if (!event.ctrlKey) {
        return
      }
      event.preventDefault()
      send('zoomWheel', {
        deltaY: event.deltaY,
        clientX: event.clientX,
        clientY: event.clientY,
        source: 'pinch',
      })
    },
    { passive: false },
  )
}

const applyAutoHideScrollbar = readingBehavior.setAutoHideScrollbar

let markleafLanguage = 'zh-Hans'

// 查找状态：由原生查找面板（FindPanelController）通过命令驱动
let findQuery = ''
let findReplace = ''
let findCaseSensitive = false
let findWholeWord = false

function injectStyleSheet(id: string, css: string): void {
  let style = document.getElementById(id) as HTMLStyleElement | null
  if (!style) {
    style = document.createElement('style')
    style.id = id
    document.head.appendChild(style)
  }
  style.textContent = css
}

function setMarkleafLanguage(lang: string): void {
  markleafLanguage = normalizeSharedEditorLanguage(lang)
  const strings = sharedEditorStrings(markleafLanguage, hostCapabilities.primaryActivationModifier)
  setBlockTypeLabels(strings)
  blockHandleButton.setAttribute('aria-label', strings.blockHandleAria)
  setEditorSharedStrings(strings)
  setMermaidStrings(strings)
  editor.view.dispatch(editor.state.tr.setMeta('addToHistory', false))
}

setMarkleafLanguage(markleafLanguage)
send('ready')

;(window as any).__markleaf_tab__ = (shift = false) => {
  if (sourceEditor) {
    shift ? sourceEditor.insertShiftTab() : sourceEditor.insertTab()
    return
  }
  const command = shift ? 'outdentListItem' : 'indentListItem'
  executeEditorCommand(editor, command)
  lastVisualSelection = captureVisualSelection(editor)
  sendEditorState()
}

const resolveStyle = (styleId: string) => resolveTypographyStyle(styleId, styleCatalog)

function applyMarkleafStyle(styleId: string): void {
  const resolved = resolveStyle(styleId)
  const toRemove = Array.from(editorMount.classList).filter((cls) => cls.startsWith('markleaf-style-'))
  editorMount.classList.remove(...toRemove)
  if (resolved.rootClass) {
    for (const cls of resolved.rootClass.split(' ')) {
      editorMount.classList.add(cls)
    }
  }
  // Mermaid measures text while rendering. Re-render after a typography
  // switch so its SVG dimensions use the newly active document font.
  rerenderMermaidElements(editorMount)
}
