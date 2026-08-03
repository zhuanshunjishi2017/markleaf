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
let revision = 0
let compositionActive = false
let compositionChanged = false
let suppressUpdate = false
let lastOutlinePosition: number | null | undefined
let outlineTimer = 0
let sourceEditor: SourceEditor | null = null
let sourceMode = false
let replaceMode = false

let editor = createEditor(editorMount)

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
  if (enabled === sourceMode) return
  if (enabled) {
    sourceEditor = new SourceEditor(sourceMount, getMarkdown(editor), markSourceChanged)
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
  const activeInput = document.activeElement instanceof HTMLElement ? document.activeElement : findInput
  const result = sourceMode
    ? sourceEditor?.find(findInput.value, caseInput.checked, wholeInput.checked, backwards) ?? { current: 0, total: 0 }
    : findInEditor(editor, findInput.value, caseInput.checked, wholeInput.checked, backwards)
  findResult.textContent = `${result.current}/${result.total}`
  send('findResult', result)
  activeInput.focus()
}

function replaceCurrent(): void {
  const activeInput = document.activeElement instanceof HTMLElement ? document.activeElement : replaceInput
  const result = sourceMode
    ? sourceEditor?.replaceCurrent(findInput.value, replaceInput.value, caseInput.checked, wholeInput.checked)
      ?? { current: 0, total: 0 }
    : replaceCurrentInEditor(editor, findInput.value, replaceInput.value, caseInput.checked, wholeInput.checked)
  findResult.textContent = `${result.current}/${result.total}`
  send('findResult', result)
  activeInput.focus()
}

function replaceEveryMatch(): void {
  const activeInput = document.activeElement instanceof HTMLElement ? document.activeElement : replaceInput
  const count = sourceMode
    ? sourceEditor?.replaceAll(findInput.value, replaceInput.value, caseInput.checked, wholeInput.checked) ?? 0
    : replaceAllInEditor(editor, findInput.value, replaceInput.value, caseInput.checked, wholeInput.checked)
  findResult.textContent = count === 0 ? '0/0' : `已替换 ${count} 处`
  send('findResult', { current: count, total: count, replaced: count })
  activeInput.focus()
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
      editor.commands.setTextSelection(resolved.pos)
    }
  }
  editor.commands.focus()
  sendEditorState()
  send('contextMenuRequested', { clientX: event.clientX, clientY: event.clientY })
})

sourceMount.addEventListener('contextmenu', (event) => {
  event.preventDefault()
  sourceEditor?.focus()
  sendEditorState()
  send('contextMenuRequested', { clientX: event.clientX, clientY: event.clientY })
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

  if (message.type !== 'loadDocument' && message.documentId !== documentId) {
    return
  }

  switch (message.type) {
    case 'loadDocument': {
      const payload = message.payload as { markdown?: unknown }
      if (typeof payload?.markdown !== 'string') {
        send('error', { message: 'loadDocument requires a markdown string.' }, message.requestId)
        return
      }
      documentId = message.documentId
      revision = message.revision
      suppressUpdate = true
      sourceEditor?.destroy()
      sourceEditor = null
      sourceMode = false
      sourceMount.hidden = true
      editorMount.hidden = false
      editor = replaceEditorDocument(editor, editorMount, payload.markdown)
      bindEditorEvents(editor)
      resetEditorViewport(editor, editorMount)
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
          const style = payload.text === 'sans' || payload.text === 'print' || payload.text === 'retro-print'
            ? payload.text
            : 'serif'
          editorMount.classList.remove('markleaf-style-sans', 'markleaf-style-print', 'markleaf-style-retro-print')
          if (style === 'retro-print') {
            editorMount.classList.add('markleaf-style-print', 'markleaf-style-retro-print')
          } else if (style !== 'serif') {
            editorMount.classList.add(`markleaf-style-${style}`)
          }
          if (message.requestId) send('commandResult', { success: true }, message.requestId)
          break
        }
        if (payload.command === 'exportSelection') {
          send('selectionExport', getSelectionExport(), message.requestId)
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

send('ready')
