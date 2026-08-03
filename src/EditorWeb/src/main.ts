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
        if (payload.command === 'exportDocument') {
          if (typeof payload.text === 'string') {
            let options: { format?: unknown; style?: unknown; header?: unknown; footer?: unknown }
            try { options = JSON.parse(payload.text) as Record<string, unknown> } catch { break }
            const style = typeof options.style === 'string'
              && (options.style === 'sans' || options.style === 'print' || options.style === 'retro-print')
              ? options.style : 'serif'
            const format = typeof options.format === 'string' ? options.format : 'html'
            const header = typeof options.header === 'string' ? options.header : ''
            const footer = typeof options.footer === 'string' ? options.footer : ''
            const html = generateExportHtml(style, format, header, footer)
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

send('ready')

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function generateExportHtml(style: string, format: string, header: string, footer: string): string {
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
  const isPrint = style === 'print' || style === 'retro-print'
  const isSans = style === 'sans'
  const rootClass = [
    isPrint ? 'markleaf-style-print' : '',
    style === 'retro-print' ? 'markleaf-style-retro-print' : '',
    isSans ? 'markleaf-style-sans' : '',
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
body { margin: 0; background: #ffffff; }
.markleaf-document {
  width: min(100%, 820px);
  margin: 0 auto;
  padding: 44px 56px 96px;
  outline: none;
  font-family: "Times New Roman", "\\5B8B\\4F53-\\7B80", "\\5B8B\\4F53", "Songti SC", SimSun, serif;
  font-size: 16px;
  line-height: 1.72;
}
.markleaf-document h1, .markleaf-document h2, .markleaf-document h3,
.markleaf-document h4, .markleaf-document h5, .markleaf-document h6 {
  color: #1f2328;
  line-height: 1.3;
  margin: 1.7em 0 0.65em;
}
.markleaf-document h1 {
  font-size: 2rem;
  margin-top: 0;
  padding-bottom: 0.35em;
  border-bottom: 1px solid #d8dee4;
}
.markleaf-document h2 { font-size: 1.5rem; }
.markleaf-document h3 { font-size: 1.25rem; }
.markleaf-document h4 { font-size: 1.1rem; }
.markleaf-document h5 { font-size: 1rem; }
.markleaf-document h6 { font-size: 0.95rem; }
.markleaf-document p, .markleaf-document ul, .markleaf-document ol,
.markleaf-document blockquote, .markleaf-document pre, .markleaf-document table {
  margin: 0.85em 0;
}
.markleaf-document blockquote {
  margin-left: 0;
  padding-left: 1em;
  color: #57606a;
  border-left: 3px solid #FCEFCD;
}
.markleaf-document hr {
  height: 1px;
  margin: 1.6em 0;
  border: 0;
  background: #d8dee4;
}
.markleaf-document code {
  font-family: "Cascadia Mono", Consolas, monospace;
  font-size: 0.9em;
  padding: 0.12em 0.35em;
  border-radius: 3px;
  background: #f3f4f5;
}
.markleaf-document pre {
  overflow-x: auto;
  padding: 16px 18px;
  border: 1px solid #d8dee4;
  border-radius: 4px;
  background: #f6f8fa;
}
.markleaf-document pre code { padding: 0; background: transparent; }
.markleaf-document table {
  width: 100%;
  border-collapse: collapse;
  line-height: 1.42;
}
.markleaf-document th, .markleaf-document td {
  padding: 3px 8px;
  text-align: left;
  vertical-align: middle;
  border: 1px solid #d0d7de;
}
.markleaf-document table p { margin: 0; }
.markleaf-document th { font-weight: 600; background: #f6f8fa; }
.markleaf-document ul[data-type='taskList'] {
  margin: 0.65em 0;
  padding-left: 0;
  list-style: none;
}
.markleaf-document ul[data-type='taskList'] > li {
  display: flex;
  gap: 0.55em;
  align-items: flex-start;
}
.markleaf-document ul[data-type='taskList'] > li > label {
  flex: 0 0 auto;
  margin-top: 0.25em;
  line-height: 1;
  user-select: none;
}
.markleaf-document ul[data-type='taskList'] > li > label input { display: block; margin: 0; }
.markleaf-document ul[data-type='taskList'] > li > div { flex: 1 1 auto; min-width: 0; }
.markleaf-document ul[data-type='taskList'] > li > div > p { margin: 0; }
.markleaf-document ul[data-type='taskList'] > li + li { margin-top: 0.28em; }
.markleaf-document a { color: #E4AE0F; }
.markleaf-document em { font-style: italic; }
.markleaf-image-frame { display: inline-block; max-width: 100%; vertical-align: top; }
.markleaf-image-content { display: block; max-width: 100%; }

/* ---- sans ---- */
.markleaf-style-sans .markleaf-document {
  font-family: "Microsoft YaHei", "Segoe UI", sans-serif;
}

/* ---- print (shared by modern and retro) ---- */
.markleaf-style-print .markleaf-document {
  color: #000000;
  font-family: "Times New Roman", "\\5B8B\\4F53-\\7B80", "\\5B8B\\4F53", serif;
  font-size: 16px;
  line-height: 1.75;
}
.markleaf-style-print .markleaf-document h1,
.markleaf-style-print .markleaf-document h2,
.markleaf-style-print .markleaf-document h3,
.markleaf-style-print .markleaf-document h4,
.markleaf-style-print .markleaf-document h5,
.markleaf-style-print .markleaf-document h6 {
  color: #000000;
  font-family: "Helvetica", "\\6C49\\4EEA\\4E2D\\9ED1\\7B80", "\\9ED1\\4F53", sans-serif;
  font-weight: normal;
  border: 0;
  padding: 0;
  margin: 0;
  line-height: 2;
}
.markleaf-style-print .markleaf-document h1 {
  font-size: 24px;
  text-align: center;
  line-height: 3;
}
.markleaf-style-print .markleaf-document h2 {
  font-size: 20px;
  text-align: center;
  line-height: 2;
}
.markleaf-style-print .markleaf-document h3 {
  font-size: 16px;
  text-align: left;
  line-height: 2;
}
.markleaf-style-print .markleaf-document h4,
.markleaf-style-print .markleaf-document h5,
.markleaf-style-print .markleaf-document h6 {
  font-size: 14px;
  text-align: left;
  line-height: 1.75;
}
.markleaf-style-print .markleaf-document p {
  margin: 0;
  text-indent: 2em;
  text-align: justify;
  font-family: "Times New Roman", "\\5B8B\\4F53-\\7B80", "\\5B8B\\4F53", serif;
}
.markleaf-style-print .markleaf-document strong,
.markleaf-style-print .markleaf-document b {
  font-family: "Helvetica", "\\6C49\\4EEA\\4E2D\\9ED1\\7B80", "\\9ED1\\4F53", sans-serif;
  font-weight: normal;
}
.markleaf-style-print .markleaf-document em,
.markleaf-style-print .markleaf-document i {
  font-family: "Times New Roman", "\\6977\\4F53", "KaiTi", serif;
  font-style: italic;
  font-synthesis: none;
}
.markleaf-style-print .markleaf-document blockquote {
  margin: 0.5em 0;
  padding: 0 1em;
  color: #000000;
  border: 1px solid #000000;
  background: transparent;
  box-decoration-break: clone;
}
.markleaf-style-print .markleaf-document blockquote p {
  font-family: "Times New Roman", "\\4EFF\\5B8B", "FangSong", serif;
  color: #000000;
  text-indent: 2em;
}
.markleaf-style-print .markleaf-document a {
  color: #808080;
  text-decoration: underline;
}
.markleaf-style-print .markleaf-document table {
  width: auto;
  margin: 1em auto;
  color: #000000;
  border: 1px solid #000000;
  background: transparent;
}
.markleaf-style-print .markleaf-document table p { text-indent: 0; }
.markleaf-style-print .markleaf-document th,
.markleaf-style-print .markleaf-document td {
  border: 1px solid #000000;
  background: transparent;
}
.markleaf-style-print .markleaf-document code,
.markleaf-style-print .markleaf-document pre code {
  font-family: "Courier New", monospace;
  line-height: 1.25;
}
.markleaf-style-print .markleaf-document pre {
  padding: 16px 18px;
  border: 0;
  border-radius: 0;
  background: #f0f0f0;
  overflow-x: hidden;
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
  box-decoration-break: clone;
}
.markleaf-style-print .markleaf-document pre code {
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
}
.markleaf-style-print .markleaf-document ul,
.markleaf-style-print .markleaf-document ol {
  margin-left: 0;
  padding-left: 2em;
}
.markleaf-style-print .markleaf-document li { padding-left: 0; }
.markleaf-style-print .markleaf-document li > p { text-indent: 0 !important; }
.markleaf-style-print .markleaf-document li > ul,
.markleaf-style-print .markleaf-document li > ol {
  margin-left: 0;
  padding-left: 1em;
}
.markleaf-style-print .markleaf-document hr {
  width: 7em;
  height: 1px;
  margin: 1.5em auto;
  background: #000000;
}

/* ---- modern-print only (excludes retro) ---- */
.markleaf-style-print:not(.markleaf-style-retro-print) .markleaf-document h1 {
  font-family: "Times New Roman", "\\65B9\\6B63\\5C0F\\6807\\5B8B\\7B80\\4F53", "\\534E\\6587\\4E2D\\5B8B", serif;
  font-weight: bold;
  font-synthesis: none;
}
.markleaf-style-print:not(.markleaf-style-retro-print) .markleaf-document code:not(pre code) {
  background: transparent;
}

/* ---- retro-print (inherits all print layout, overrides only typography) ---- */
.markleaf-style-retro-print .markleaf-document,
.markleaf-style-retro-print .markleaf-document p {
  font-family: KingHwaOldSong, "\\5B8B\\4F53-\\7B80", "\\5B8B\\4F53", serif;
}
.markleaf-style-retro-print .markleaf-document h1,
.markleaf-style-retro-print .markleaf-document h2,
.markleaf-style-retro-print .markleaf-document h3,
.markleaf-style-retro-print .markleaf-document h4,
.markleaf-style-retro-print .markleaf-document h5,
.markleaf-style-retro-print .markleaf-document h6,
.markleaf-style-retro-print .markleaf-document strong,
.markleaf-style-retro-print .markleaf-document b {
  font-family: "\\6C47\\6587\\6E2F\\9ED1", "\\6C49\\4EEA\\4E2D\\9ED1\\7B80", "\\9ED1\\4F53", sans-serif;
  font-weight: normal;
}
.markleaf-style-retro-print .markleaf-document h1 {
  font-family: "\\671D\\534E\\6807\\9898A", "\\6C47\\6587\\6E2F\\9ED1", "\\6C49\\4EEA\\4E2D\\9ED1\\7B80", "\\9ED1\\4F53", sans-serif;
}
.markleaf-style-retro-print .markleaf-document em,
.markleaf-style-retro-print .markleaf-document i {
  font-family: "\\6C47\\6587\\6B63\\6977", "\\6977\\4F53", "KaiTi", serif;
  font-style: normal;
  font-synthesis: none;
}
.markleaf-style-retro-print .markleaf-document blockquote p {
  font-family: "\\6C47\\6587\\4EFF\\5B8B", "\\4EFF\\5B8B", "FangSong", serif;
}
.markleaf-style-retro-print .markleaf-document code,
.markleaf-style-retro-print .markleaf-document pre code {
  font-family: "\\671D\\534E\\6253\\5B57\\673A", "Courier New", monospace;
}
.markleaf-style-retro-print .markleaf-document code:not(pre code) { background: transparent; }
.markleaf-style-retro-print .markleaf-document pre {
  background: transparent;
  border: 1px dashed #000000;
  border-radius: 0;
  box-decoration-break: clone;
}
.markleaf-style-retro-print .markleaf-document ul,
.markleaf-style-retro-print .markleaf-document ol,
.markleaf-style-retro-print .markleaf-document li,
.markleaf-style-retro-print .markleaf-document li > p,
.markleaf-style-retro-print .markleaf-document li::marker {
  font-family: KingHwaOldSong, "\\5B8B\\4F53-\\7B80", "\\5B8B\\4F53", serif !important;
}

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
  width: min(100%, 820px);
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
