import './styles.css'
import {
  createEditor,
  clearFindHighlights,
  executeEditorCommand,
  exportEditorSelection,
  findInEditor,
  findFootnoteDefinitionBody,
  getEditorCommandState,
  getEditorStatus,
  getBlockHandleInfo,
  getMarkdown,
  captureVisualSelection,
  collapseVisualSelection,
  getSourceModeJumpTarget,
  isAllowedLink,
  replaceAllInEditor,
  replaceCurrentInEditor,
  replaceEditorDocument,
  resetEditorViewport,
  scrollToFootnoteDefinition,
  setBlockHighlight,
  setBlockHandleVisible,
  setBlockTypeLabels,
  setEditorSharedStrings,
  restoreVisualSelection,
  renderEscapedCaptionHtml,
  type VisualSelectionSnapshot,
} from './editor'
import { katexCss, renderMathInHtml } from './math'
import { renderMermaidInHtml, setMermaidStrings } from './mermaid'
import { SourceEditor, type UnsafeEmphasisRequest } from './source-editor'
import { isPlainTextDocumentType, type DocumentType } from './document-mode'
import {
  executeFormatPainterApply,
  FormatPainterController,
  captureFormat,
  normalizeContextMenuCaretPosition,
} from './format-painter'
import { applyFormatPainterFromDomSelection } from './format-painter-dom-events'
import {
  isHostMessage,
  postToHost,
  postToHostWithAdditionalObjects,
  protocolVersion,
  type HostMessage,
} from './protocol'
import { preserveViewportDuringLayoutChange, type ViewportAnchorReader } from './zoom-anchor'
import { bindReducedMotionPreference, createScrollbarAlphaController } from './scrollbar-motion'
import { hasPrimaryActivationModifier, resolveHostCapabilities } from './host-capabilities'
import { sharedEditorStrings } from './shared-editor-strings'
import { isHostCommandAllowed } from './host-command-policy'

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
const caseText = document.querySelector<HTMLElement>('#find-case-text')!
const wholeText = document.querySelector<HTMLElement>('#find-whole-text')!
const findResult = document.querySelector<HTMLElement>('#find-result')!
const findPrevious = document.querySelector<HTMLButtonElement>('#find-previous')!
const findNext = document.querySelector<HTMLButtonElement>('#find-next')!
const replaceOne = document.querySelector<HTMLButtonElement>('#replace-one')!
const replaceAll = document.querySelector<HTMLButtonElement>('#replace-all')!
const findClose = document.querySelector<HTMLButtonElement>('#find-close')!
const sourceToggle = document.querySelector<HTMLButtonElement>('#source-toggle')!

bindReducedMotionPreference(
  window.matchMedia('(prefers-reduced-motion: reduce)'),
  document.documentElement,
  document.body,
)

const hostCapabilities = resolveHostCapabilities(window.chrome?.webview?.hostPlatform)
document.documentElement.classList.toggle(
  'markleaf-host-macos',
  hostCapabilities.usesThemedVisualSelection,
)

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
let visualSelectionBeforeSourceMode: VisualSelectionSnapshot | null = null
// 混合前端右键菜单（粗体/斜体/下划线工具栏）是否启用：由宿主下发，仅 Windows 端为 true。
let frontendFormatMenuEnabled = false
let documentType: DocumentType = 'markdown'
let readOnly = false

const editorCreationOptions = {
  themedVisualSelection: hostCapabilities.usesThemedVisualSelection,
}
let editor = createEditor(editorMount, '', false, editorCreationOptions)
const formatPainter = new FormatPainterController()
let contextMenuSelection: { from: number; to: number } | null = null
let lastVisualSelection = captureVisualSelection(editor)

const blockHandleButton = document.createElement('button')
blockHandleButton.type = 'button'
blockHandleButton.className = 'ml-block-handle ml-block-handle-overlay'
blockHandleButton.setAttribute(
  'aria-label',
  sharedEditorStrings('zh-Hans', hostCapabilities.primaryActivationModifier).blockHandleAria,
)
blockHandleButton.setAttribute('tabindex', '-1')
blockHandleButton.hidden = true
let blockHandleOverlayPosition: number | null = null

function ensureBlockHandleOverlay(): void {
  if (blockHandleButton.parentElement !== editorMount) {
    editorMount.appendChild(blockHandleButton)
  }
}

function hideBlockHandleOverlay(): void {
  blockHandleButton.hidden = true
  blockHandleButton.style.display = 'none'
  blockHandleButton.style.removeProperty('left')
  blockHandleButton.style.removeProperty('top')
  blockHandleButton.textContent = ''
  blockHandleButton.classList.remove('ml-block-handle-active')
  blockHandleOverlayPosition = null
}

function updateBlockHandleOverlay(): void {
  ensureBlockHandleOverlay()
  if (sourceMode || readOnly) {
    hideBlockHandleOverlay()
    return
  }
  const info = getBlockHandleInfo(editor)
  if (!info) {
    hideBlockHandleOverlay()
    return
  }
  blockHandleOverlayPosition = info.position
  blockHandleButton.hidden = false
  blockHandleButton.style.removeProperty('display')
  blockHandleButton.textContent = info.label
  blockHandleButton.classList.toggle('ml-block-handle-active', info.active)
  const mountRect = editorMount.getBoundingClientRect()
  const documentRect = editor.view.dom.getBoundingClientRect()
  blockHandleButton.style.left = `${documentRect.left - mountRect.left - 36}px`
  blockHandleButton.style.top = `${info.viewportTop - mountRect.top}px`
}

blockHandleButton.addEventListener('mousedown', (event) => {
  event.preventDefault()
  event.stopPropagation()
  if (blockHandleOverlayPosition === null) return
  setBlockHighlight(editor, blockHandleOverlayPosition)
  updateBlockHandleOverlay()
  const rect = blockHandleButton.getBoundingClientRect()
  send('blockMenuRequested', {
    clientX: rect.left,
    clientY: rect.bottom + 10,
    position: blockHandleOverlayPosition,
  })
})

let baseCss = ''
let styleCatalog: { id: string; css: string; dependsOn?: string }[] = []
let scrollbarHideTimer = 0

type VisualVariablePayload = {
  lineHeight: string
  fontSize: string
  maxWidth: string
  sourceFontSize: string
  sourceFontFamily: string
  cjkLanguage: string
  visualCjkAutoSpacing: boolean
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
  }, anchorReader)
}

let findBarLoc: Record<string, string> = {}

function applyFindBarLocalization(loc: Record<string, string>): void {
  findBarLoc = loc
  setBlockTypeLabels(loc)
  findInput.placeholder = loc.find ?? 'Find'
  findInput.ariaLabel = loc.findLabel ?? 'Find'
  replaceInput.placeholder = loc.replaceWith ?? 'Replace with'
  replaceInput.ariaLabel = loc.replaceLabel ?? 'Replace with'
  caseText.textContent = loc.caseSensitive ?? 'Case sensitive'
  wholeText.textContent = loc.wholeWord ?? 'Whole word'
  findPrevious.textContent = loc.previous ?? 'Previous'
  findNext.textContent = loc.next ?? 'Next'
  replaceOne.textContent = loc.replace ?? 'Replace'
  replaceAll.textContent = loc.replaceAll ?? 'Replace all'
  findClose.textContent = loc.close ?? 'Close'
  findClose.ariaLabel = loc.closeLabel ?? 'Close find bar'
  findResult.textContent = loc.noResults ?? '0/0'
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
  const visualState = getEditorCommandState(editor)
  send('commandStateChanged', {
    ...visualState,
    canUndo: sourceEditor?.canUndo() ?? visualState.canUndo,
    canRedo: sourceEditor?.canRedo() ?? visualState.canRedo,
    hasSelection: sourceSelection ? !sourceSelection.empty : visualState.hasSelection,
    sourceMode,
    readOnly,
    canStartFormatPainter: !sourceMode && captureFormat(editor) !== null,
    formatPainterArmed: !sourceMode && formatPainter.isArmed,
  })
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

function updateFormatPainterCursor(): void {
  editorMount.classList.toggle('format-painter-armed', !sourceMode && formatPainter.isArmed)
}

// Listen on the stable editor container: a backward drag that ends exactly at
// a line start can release over the container padding rather than ProseMirror.
editorMount.addEventListener('mouseup', (event) => {
  if (compositionActive || sourceMode) return
  const target = event.target
  if (!(target instanceof Node)
    || (target !== editorMount && !editor.view.dom.contains(target))) {
    return
  }
  window.setTimeout(() => {
    if (compositionActive || sourceMode) return
    const wasArmed = formatPainter.isArmed
    applyFormatPainterFromDomSelection(
      editor,
      formatPainter,
      document.getSelection(),
    )
    if (wasArmed !== formatPainter.isArmed) {
      updateFormatPainterCursor()
      sendEditorState()
    }
  }, 0)
})

// 格式刷拖选时若鼠标最终移到编辑器/窗口外释放，editorMount 收不到 mouseup；
// 只要画刷仍处于武装状态且选区落在编辑器 DOM 内，就在 window 级捕获释放并应用。
window.addEventListener('mouseup', () => {
  if (compositionActive || sourceMode) return
  const sel = document.getSelection()
  const dom = editor.view.dom
  if (!sel || sel.isCollapsed
    || !sel.anchorNode || !sel.focusNode
    || !dom.contains(sel.anchorNode) || !dom.contains(sel.focusNode)) {
    return
  }
  const wasArmed = formatPainter.isArmed
  applyFormatPainterFromDomSelection(editor, formatPainter, sel)
  if (wasArmed !== formatPainter.isArmed) {
    updateFormatPainterCursor()
    sendEditorState()
  }
})

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
    if (!compositionActive) {
      lastVisualSelection = captureVisualSelection(targetEditor)
      send('selectionChanged', {
        from: targetEditor.state.selection.from,
        to: targetEditor.state.selection.to,
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
window.addEventListener('scroll', updateBlockHandleOverlay, true)
window.addEventListener('resize', updateBlockHandleOverlay)
editorMount.addEventListener('mousemove', updateBlockHandleOverlay)
editorMount.addEventListener('mouseenter', updateBlockHandleOverlay)
editorMount.addEventListener('focusin', updateBlockHandleOverlay)
editorMount.addEventListener('click', () => window.setTimeout(updateBlockHandleOverlay, 0))

function markSourceChanged(documentChanged: boolean): void {
  if (documentChanged) {
    revision += 1
    send('dirtyChanged', { dirty: true })
  }
  sendEditorState()
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
  formatPainter.cancel()
  updateFormatPainterCursor()
  if (enabled) {
    visualSelectionBeforeSourceMode = captureVisualSelection(editor)
    const jumpTarget = getSourceModeJumpTarget(editor)
    sourceEditor = new SourceEditor(sourceMount, getMarkdown(editor), markSourceChanged, sourceIndentWidth, readOnly, requestUnsafeEmphasisResolution, documentType === 'markdown')
    editorMount.hidden = true
    sourceMount.hidden = false
    sourceMode = true
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
    editor = replaceEditorDocument(editor, editorMount, markdown, false, editorCreationOptions)
    bindEditorEvents(editor)
    ensureBlockHandleOverlay()
    suppressUpdate = false
    sourceMount.hidden = true
    editorMount.hidden = false
    sourceMode = false
    restoreVisualSelection(editor, visualSelection, true)
    lastVisualSelection = captureVisualSelection(editor)
    scheduleOutline()
    sendOutlineSelectionFromCursor()
  }
  updateBlockHandleOverlay()
  sendEditorState()
}

function syncFindStateFromDom(): void {
  findQuery = findInput.value
  findReplace = replaceInput.value
  findCaseSensitive = caseInput.checked
  findWholeWord = wholeInput.checked
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
  syncFindStateFromDom()
  updateFindResult(false)
})
findInput.addEventListener('input', () => {
  findQuery = findInput.value
  updateFindResult(false)
})
caseInput.addEventListener('change', () => {
  findCaseSensitive = caseInput.checked
  updateFindResult(false)
})
wholeInput.addEventListener('change', () => {
  findWholeWord = wholeInput.checked
  updateFindResult(false)
})
replaceInput.addEventListener('input', () => {
  findReplace = replaceInput.value
})
findPrevious.addEventListener('click', () => updateFindResult(true))
findNext.addEventListener('click', () => updateFindResult(false))
replaceOne.addEventListener('click', replaceCurrent)
replaceAll.addEventListener('click', replaceEveryMatch)
findClose.addEventListener('click', closeFindBar)
sourceToggle.addEventListener('click', () => setSourceMode(!sourceMode))
window.addEventListener('keydown', event => {
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
    return
  }
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

editorMount.addEventListener('mousedown', (event) => {
  if (!(event.target instanceof Element)) {
    return
  }
  if (event.button !== 0 || !hasPrimaryActivationModifier(event, hostCapabilities)) {
    return
  }

  const footnoteRef = event.target.closest<HTMLElement>('sup[data-footnote-ref]')
  const footnoteLabel = footnoteRef?.getAttribute('data-footnote-ref')
  if (footnoteLabel) {
    event.preventDefault()
    event.stopImmediatePropagation()
    if (!scrollToFootnoteDefinition(editor, footnoteLabel)) {
      send('footnoteDefinitionMissing', { label: footnoteLabel })
    }
    sendEditorState()
    return
  }

  const footnoteDef = event.target.closest<HTMLElement>('p.markleaf-footnote-def')
  if (footnoteDef) {
    const label = footnoteDef.getAttribute('data-footnote-label') ?? ''
    event.preventDefault()
    event.stopImmediatePropagation()
    if (!executeEditorCommand(editor, 'goToFootnoteReference', label)) {
      send('footnoteReferenceMissing', { label })
    }
    sendEditorState()
    return
  }

  const anchor = event.target.closest<HTMLAnchorElement>('a[href]')
  const url = anchor?.getAttribute('href')
  if (!url || !isAllowedLink(url)) {
    return
  }

  event.preventDefault()
  event.stopImmediatePropagation()
  send('openLink', { url })
}, true)

// ---- 链接 / 注释角标悬停提示（手形光标 + 提示文本） ----
const editorTooltip = document.createElement('div')
editorTooltip.className = 'editor-tooltip'
editorTooltip.hidden = true
document.body.appendChild(editorTooltip)

let tooltipKind: 'link' | 'footnote' | null = null
let tooltipHideTimer = 0

function editorTooltipTexts(): { link: string; footnote: string; footnoteNotFound: string } {
  const strings = sharedEditorStrings(
    markleafLanguage,
    hostCapabilities.primaryActivationModifier,
  )
  return {
    link: strings.linkTooltip,
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

function buildEditorTooltip(kind: 'link' | 'footnote', detail: string | null): void {
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
  hint.textContent = kind === 'link' ? texts.link : texts.footnote
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
    const detail = kind === 'footnote'
      ? findFootnoteDefinitionBody(editor, footnoteEl!.getAttribute('data-footnote-ref') ?? '')
      : (anchorEl?.getAttribute('href') ?? '')
    if (tooltipKind !== kind) {
      tooltipKind = kind
      buildEditorTooltip(kind, detail)
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

function findMathNodeAt(pos: number): number | null {
  const node = editor.state.doc.nodeAt(pos)
  if (node && (node.type.name === 'mathInline' || node.type.name === 'mathBlock')) {
    return pos
  }
  const before = pos > 0 ? editor.state.doc.nodeAt(pos - 1) : null
  if (before && (before.type.name === 'mathInline' || before.type.name === 'mathBlock')) {
    return pos - 1
  }
  return null
}

function findMermaidNodeAt(pos: number): number | null {
  const node = editor.state.doc.nodeAt(pos)
  if (node?.type.name === 'mermaid') {
    return pos
  }
  const before = pos > 0 ? editor.state.doc.nodeAt(pos - 1) : null
  if (before?.type.name === 'mermaid') {
    return pos - 1
  }
  return null
}

const formatMenu = document.createElement('div')
formatMenu.id = 'format-menu'
formatMenu.className = 'format-menu'
formatMenu.hidden = true
const formatButtons: Array<{ command: string; glyph: string; label: string }> = [
  { command: 'toggleBold', glyph: '', label: 'Bold' },
  { command: 'toggleItalic', glyph: '', label: 'Italic' },
  { command: 'toggleUnderline', glyph: '', label: 'Underline' },
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
    const applyToBlock = command === 'toggleBold' || command === 'toggleItalic' || command === 'toggleUnderline'
    executeEditorCommand(editor, command, undefined, undefined, applyToBlock)
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
  state: ReturnType<typeof getEditorCommandState>,
): void {
  window.clearTimeout(formatMenuHideTimer)
  formatMenu.classList.toggle('format-menu-dark', isDarkTheme())
  formatMenu.style.left = `${clientX}px`
  formatMenu.style.top = `${clientY}px`
  formatMenu.hidden = false
  void formatMenu.offsetWidth
  formatMenu.classList.add('format-menu-visible')

  const activeByCommand: Record<string, boolean> = {
    toggleBold: state.bold,
    toggleItalic: state.italic,
    toggleUnderline: state.underline,
  }
  for (const button of formatButtonElements) {
    button.classList.toggle(
      'format-menu-button-active',
      activeByCommand[button.dataset.command ?? ''] === true,
    )
  }

  const isHeading = state.headingLevel !== null
  formatSeparator.hidden = !isHeading
  for (const button of headingButtonElements) {
    button.hidden = !isHeading
  }
  promoteHeadingButton.disabled = state.headingLevel === 1
  demoteHeadingButton.disabled = state.headingLevel === 6
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

function shouldShowFormatMenu(state: ReturnType<typeof getEditorCommandState>): boolean {
  if (!frontendFormatMenuEnabled) {
    return false
  }
  if (readOnly) {
    return false
  }
  if (sourceMode) {
    return false
  }
  if (state.imageSelected || state.mathInline || state.mathBlock || state.mermaidSelected) {
    return false
  }
  const inFormattableBlock = state.headingLevel !== null || state.paragraph
    || state.bulletList || state.orderedList || state.taskList || state.blockquote || state.inTable
  return state.hasSelection || inFormattableBlock
}

editorMount.addEventListener('contextmenu', (event) => {
  event.preventDefault()
  const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (resolved) {
    const mathPos = findMathNodeAt(resolved.pos)
    const mermaidPos = mathPos === null ? findMermaidNodeAt(resolved.pos) : null
    if (mathPos !== null) {
      editor.commands.setNodeSelection(mathPos)
    } else if (mermaidPos !== null) {
      editor.commands.setNodeSelection(mermaidPos)
    } else {
      const selection = editor.state.selection
      if (selection.empty || resolved.pos < selection.from || resolved.pos > selection.to) {
        editor.commands.setTextSelection(normalizeContextMenuCaretPosition(editor, resolved.pos))
      }
    }
  }
  editor.commands.focus()
  contextMenuSelection = {
    from: editor.state.selection.from,
    to: editor.state.selection.to,
  }
  sendEditorState()
  const state = getEditorCommandState(editor)
  const showFormat = shouldShowFormatMenu(state)
  if (showFormat) {
    showFormatMenu(event.clientX, event.clientY, state)
  } else {
    hideFormatMenu()
  }
  send('contextMenuRequested', {
    clientX: event.clientX,
    clientY: event.clientY,
    menuHeight: showFormat ? formatMenu.offsetHeight : 0,
    canStartFormatPainter: !sourceMode && captureFormat(editor) !== null,
    formatPainterArmed: !sourceMode && formatPainter.isArmed,
    readOnly,
  })
})

editorMount.addEventListener('dblclick', (event) => {
  if (sourceMode) {
    return
  }
  const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (!resolved) {
    return
  }
  const mathPos = findMathNodeAt(resolved.pos)
  if (mathPos !== null) {
    event.preventDefault()
    editor.commands.setNodeSelection(mathPos)
    sendEditorState()
    send('mathEditRequested', {})
    return
  }

  const mermaidPos = findMermaidNodeAt(resolved.pos)
  if (mermaidPos === null) {
    return
  }

  event.preventDefault()
  editor.commands.setNodeSelection(mermaidPos)
  executeEditorCommand(editor, 'editMermaid')
  sendEditorState()
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
      break
    }
    case 'loadDocument': {
      formatPainter.cancel()
      updateFormatPainterCursor()
      const payload = message.payload as { markdown?: unknown; documentType?: unknown; readOnly?: unknown }
      if (typeof payload?.markdown !== 'string') {
        send('error', { message: 'loadDocument requires a markdown string.' }, message.requestId)
        return
      }
      documentId = message.documentId
      documentLoaded = true
      revision = message.revision
      documentType = isPlainTextDocumentType(payload?.documentType) ? 'plainText' : 'markdown'
      readOnly = payload?.readOnly === true
      suppressUpdate = true
      sourceEditor?.destroy()
      sourceEditor = null
      if (documentType === 'plainText') {
        sourceMode = true
        sourceMount.hidden = false
        editorMount.hidden = true
        sourceEditor = new SourceEditor(sourceMount, payload.markdown, markSourceChanged, sourceIndentWidth, readOnly, requestUnsafeEmphasisResolution, false)
      } else {
        sourceMode = false
        sourceMount.hidden = true
        editorMount.hidden = false
        editor = replaceEditorDocument(
          editor,
          editorMount,
          payload.markdown,
          readOnly,
          editorCreationOptions,
        )
        bindEditorEvents(editor)
        ensureBlockHandleOverlay()
        resetEditorViewport(editor, editorMount)
        lastVisualSelection = captureVisualSelection(editor)
      }
      suppressUpdate = false
      send('documentLoaded', undefined, message.requestId)
      updateBlockHandleOverlay()
      sendOutline()
      sendEditorState()
      sendOutlineSelectionFromCursor()
      break
    }
    case 'setDocumentType': {
      const payload = message.payload as { documentType?: unknown }
      const nextType = isPlainTextDocumentType(payload?.documentType) ? 'plainText' : 'markdown'
      if (nextType === documentType) break
      const markdown = getActiveMarkdown()
      formatPainter.cancel()
      updateFormatPainterCursor()
      suppressUpdate = true
      sourceEditor?.destroy()
      sourceEditor = null
      documentType = nextType
      if (nextType === 'plainText') {
        sourceMode = true
        sourceMount.hidden = false
        editorMount.hidden = true
        sourceEditor = new SourceEditor(sourceMount, markdown, markSourceChanged, sourceIndentWidth, readOnly, requestUnsafeEmphasisResolution, false)
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
        ensureBlockHandleOverlay()
        resetEditorViewport(editor, editorMount)
        lastVisualSelection = captureVisualSelection(editor)
      }
      suppressUpdate = false
      updateBlockHandleOverlay()
      sendEditorState()
      sendOutline()
      sendOutlineSelectionFromCursor()
      break
    }
    case 'requestSnapshot':
      send('snapshot', { markdown: getActiveMarkdown() }, message.requestId)
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
        clientX?: unknown
        clientY?: unknown
        applyToCurrentTextBlockWhenEmpty?: unknown
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
            findInput.value = findQuery
            caseInput.checked = findCaseSensitive
            wholeInput.checked = findWholeWord
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
          findInput.value = findQuery
          replaceInput.value = findReplace
          caseInput.checked = findCaseSensitive
          wholeInput.checked = findWholeWord
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
              visualCjkAutoSpacing?: unknown
              colorSchemeCss?: unknown
              title?: unknown
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
            const html = await generateExportHtml(
              style,
              format,
              header,
              footer,
              fontSize,
              lineHeight,
              maxWidth,
              visualCjkAutoSpacing,
              colorSchemeCss,
              title,
            )
            send('exportContent', { html }, message.requestId)
          }
          break
        }
        const coordinates = typeof payload.clientX === 'number' && typeof payload.clientY === 'number'
          ? { left: payload.clientX, top: payload.clientY }
          : undefined
        const commandText = typeof payload.text === 'string' ? payload.text : undefined
        if (!sourceMode
          && (payload.command === 'indentListItem' || payload.command === 'outdentListItem')) {
          restoreVisualSelection(editor, lastVisualSelection)
        }
        const success = sourceMode
          ? payload.command === 'undo'
            ? sourceEditor?.undo() ?? false
            : payload.command === 'redo'
              ? sourceEditor?.redo() ?? false
              : payload.command === 'deleteSelection'
            ? sourceEditor?.deleteSelection() ?? false
            : payload.command === 'pasteText' && commandText !== undefined
              ? sourceEditor?.replaceSelection(commandText) ?? false
            : payload.command === 'insertMermaid'
              ? sourceEditor?.insertMermaidCodeBlock() ?? false
            : payload.command === 'selectAll'
                ? sourceEditor?.selectAll() ?? false
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
  animateScrollbarAlphaTo(1)
  window.clearTimeout(scrollbarHideTimer)
  scrollbarHideTimer = window.setTimeout(() => {
    animateScrollbarAlphaTo(0)
  }, 800)
}

const alphaFrameScheduler = {
  now: () => performance.now(),
  requestFrame: (cb: (time: number) => void) => requestAnimationFrame(cb),
  cancelFrame: (id: number) => cancelAnimationFrame(id),
}

// WKWebView 不支持滚动条透明度的 CSS transition。控制器记录当前动画目标，
// 连续滚动期间重复请求显示时不会取消并重启同一段逐帧动画。
const scrollbarAlphaController = createScrollbarAlphaController(
  0,
  200,
  (alpha) => document.documentElement.style.setProperty('--ml-scrollbar-alpha', String(alpha)),
  alphaFrameScheduler,
)

function reducedMotionActive(): boolean {
  return document.documentElement.classList.contains('markleaf-reduced-motion')
}

function animateScrollbarAlphaTo(target: number): void {
  scrollbarAlphaController.animateTo(target, reducedMotionActive())
}

function applyAutoHideScrollbar(enabled: boolean): void {
  document.documentElement.classList.toggle('markleaf-auto-hide-scrollbar', enabled)
  document.body.classList.toggle('markleaf-auto-hide-scrollbar', enabled)
  if (!enabled) {
    window.clearTimeout(scrollbarHideTimer)
    scrollbarAlphaController.reset(0)
  }
}

// ---- i18n：查找栏文案（跟随宿主语言，zh-Hans 为默认） ----
const FIND_BAR_STRINGS: Record<string, Record<string, string>> = {
  'zh-Hans': { find: '查找', replaceWith: '替换为', prev: '上一个', next: '下一个', replace: '替换', replaceAll: '全部替换', close: '关闭', case: '区分大小写', whole: '全词', closeAria: '关闭查找栏', blockParagraph: '正文', blockHeading1: '标题 1', blockHeading2: '标题 2', blockHeading3: '标题 3', blockHeading4: '标题 4', blockHeading5: '标题 5', blockHeading6: '标题 6', blockBulletList: '无序列表', blockOrderedList: '有序列表', blockTaskList: '任务列表', blockBlockquote: '引用块', blockCodeBlock: '代码块', blockMermaid: '图表', blockTable: '表', blockFootnote: '注' },
  'zh-Hant': { find: '尋找', replaceWith: '取代為', prev: '上一個', next: '下一個', replace: '取代', replaceAll: '全部取代', close: '關閉', case: '區分大小寫', whole: '全詞', closeAria: '關閉搜尋列', blockParagraph: '段落', blockHeading1: '標題 1', blockHeading2: '標題 2', blockHeading3: '標題 3', blockHeading4: '標題 4', blockHeading5: '標題 5', blockHeading6: '標題 6', blockBulletList: '無序清單', blockOrderedList: '有序清單', blockTaskList: '工作清單', blockBlockquote: '引言區塊', blockCodeBlock: '程式碼區塊', blockMermaid: '圖表', blockTable: '表', blockFootnote: '註' },
  en: { find: 'Find', replaceWith: 'Replace with', prev: 'Previous', next: 'Next', replace: 'Replace', replaceAll: 'Replace All', close: 'Close', case: 'Case Sensitive', whole: 'Whole Word', closeAria: 'Close Find Bar', blockParagraph: 'Paragraph', blockHeading1: 'Heading 1', blockHeading2: 'Heading 2', blockHeading3: 'Heading 3', blockHeading4: 'Heading 4', blockHeading5: 'Heading 5', blockHeading6: 'Heading 6', blockBulletList: 'Bullet List', blockOrderedList: 'Numbered List', blockTaskList: 'Task List', blockBlockquote: 'Blockquote', blockCodeBlock: 'Code Block', blockMermaid: 'Diagram', blockTable: '▦', blockFootnote: 'Fn' },
  ja: { find: '検索', replaceWith: '置換後の文字列', prev: '前へ', next: '次へ', replace: '置換', replaceAll: 'すべて置換', close: '閉じる', case: '大文字と小文字を区別', whole: '単語全体', closeAria: '検索バーを閉じる', blockParagraph: '本文', blockHeading1: '見出し 1', blockHeading2: '見出し 2', blockHeading3: '見出し 3', blockHeading4: '見出し 4', blockHeading5: '見出し 5', blockHeading6: '見出し 6', blockBulletList: '箇条書き', blockOrderedList: '番号付きリスト', blockTaskList: 'タスクリスト', blockBlockquote: '引用ブロック', blockCodeBlock: 'コードブロック', blockMermaid: '図表', blockTable: '表', blockFootnote: '注' },
}

function applyFindBarLanguage(lang: string): void {
  const table: Record<string, string> = FIND_BAR_STRINGS[lang] ?? FIND_BAR_STRINGS['zh-Hans'] ?? {}
  setBlockTypeLabels(table)
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
  const setLabelText = (textId: string, inputId: string, text: string) => {
    const textEl = document.getElementById(textId)
    const input = document.getElementById(inputId)
    if (textEl) textEl.textContent = text
    if (input) input.setAttribute('aria-label', text)
  }
  setLabelText('find-case-text', 'find-case', table.case ?? '')
  setLabelText('find-whole-text', 'find-whole', table.whole ?? '')
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
  const strings = sharedEditorStrings(lang, hostCapabilities.primaryActivationModifier)
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

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function renderEditorHtmlForExport(html: string, preserveEmptyParagraphs = false): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html')

  for (const caption of Array.from(parsed.body.querySelectorAll<HTMLElement>('figcaption.markleaf-figcaption'))) {
    caption.innerHTML = renderEscapedCaptionHtml(caption.textContent ?? '')
  }

  for (const paragraph of Array.from(parsed.body.querySelectorAll<HTMLParagraphElement>('p'))) {
    if (preserveEmptyParagraphs && isEmptyExportParagraph(paragraph)) {
      paragraph.innerHTML = '&nbsp;'
      continue
    }

    const match = new RegExp(`^\\s*\\u2060?\\[\\^([^\\]\\n]+)\\]:[ \\t]*(.*)$`, 's').exec(paragraph.textContent ?? '')
    if (!match) continue

    const label = match[1]!.trim()
    const body = match[2] ?? ''
    const prefixLength = match[0].length - body.length
    paragraph.classList.add('markleaf-footnote-def')
    paragraph.classList.add('markleaf-footnote-def-export')
    paragraph.dataset.footnoteLabel = label
    removeTextPrefix(paragraph, prefixLength)
    const labelElement = parsed.createElement('span')
    labelElement.className = 'markleaf-footnote-def-label'
    labelElement.textContent = `[${label}] `
    paragraph.insertBefore(labelElement, paragraph.firstChild)
  }

  return parsed.body.innerHTML.replace(/\u2060/g, '')
}

function isEmptyExportParagraph(paragraph: HTMLParagraphElement): boolean {
  if ((paragraph.textContent ?? '').replace(/\u00a0/g, '').trim().length > 0) {
    return false
  }
  return !Array.from(paragraph.childNodes).some((node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      return ((node.textContent ?? '').replace(/\u00a0/g, '').trim().length > 0)
    }
    if (!(node instanceof HTMLElement)) {
      return false
    }
    return node.tagName.toLowerCase() !== 'br'
  })
}

function removeTextPrefix(element: HTMLElement, length: number): void {
  let remaining = Math.max(0, length)
  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
  const emptyTextNodes: Text[] = []

  while (remaining > 0) {
    const node = walker.nextNode()
    if (!(node instanceof Text)) break

    if (node.data.length <= remaining) {
      remaining -= node.data.length
      emptyTextNodes.push(node)
      continue
    }

    node.data = node.data.slice(remaining)
    remaining = 0
  }

  for (const node of emptyTextNodes) {
    node.remove()
  }
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

async function generateExportHtml(
  style: string,
  format: string,
  header: string,
  footer: string,
  fontSize = 16,
  lineHeight = 1.6,
  maxWidth = 820,
  visualCjkAutoSpacing = true,
  colorSchemeCss = '',
  title = '',
): Promise<string> {
  const isPdf = format === 'pdf'
  const rawBodyHtml = sourceMode
    ? `<pre><code>${escapeHtml(sourceEditor?.getText() ?? '')}</code></pre>`
    : editor.getHTML()
  const bodyHtml = await renderMermaidInHtml(renderEditorHtmlForExport(renderMathInHtml(rawBodyHtml), isPdf).replace(
    /https:\/\/assets\.local\/image\?path=([^"']+)/g,
    (_, encoded: string) => {
      try { return decodeURIComponent(encoded) } catch { return encoded }
    },
  ))
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
<title>${escapeHtml(title || 'MarkLeaf')}</title>
<style>
* { box-sizing: border-box; }
${katexCss}
${baseCss}
.markleaf-document { text-autospace: ${visualCjkAutoSpacing ? 'normal' : 'no-autospace'}; }
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
  max-width: none;
  width: 100%;
  margin-left: 0;
  margin-right: 0;
}
.markleaf-export-pdf.markleaf-style-print .markleaf-document {
  padding-left: 5px;
  padding-right: 5px;
  max-width: none;
  width: 100%;
  margin-left: 0;
  margin-right: 0;
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
  width: auto;
  max-width: 100%;
  table-layout: auto;
  word-wrap: break-word;
  overflow-wrap: break-word;
}
.markleaf-export-pdf .markleaf-document .markleaf-mermaid,
.markleaf-export-pdf .markleaf-document .markleaf-mermaid-view,
.markleaf-export-pdf .markleaf-document .markleaf-mermaid-export {
  display: flex;
  justify-content: center;
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
<script>
(function () {
  function fitMath() {
    var doc = document.querySelector('.markleaf-document');
    if (!doc) return;
    var items = doc.querySelectorAll('.katex-display');
    for (var i = 0; i < items.length; i++) {
      var el = items[i];
      el.style.fontSize = '';
      var available = el.clientWidth;
      if (available <= 0) continue;
      // 让容器收缩包裹到内容宽度后再量，避免居中溢出与内联片段导致的测量失真。
      var display = el.style.display;
      var width = el.style.width;
      el.style.display = 'inline-block';
      el.style.width = 'max-content';
      var content = el.getBoundingClientRect().width;
      el.style.display = display;
      el.style.width = width;
      if (content <= available) continue;
      var base = parseFloat(getComputedStyle(el).fontSize) || 16;
      el.style.fontSize = (base * available / content).toFixed(2) + 'px';
    }
  }
  window.__markleafFitMath = fitMath;
  // 等待 KaTeX 字体加载完成后再测量，避免用回退字体度量导致公式被误缩放。
  if (document.fonts && document.fonts.ready) {
    document.fonts.ready.then(fitMath);
  } else {
    fitMath();
  }
})();
</script>
</body>
</html>`
}
