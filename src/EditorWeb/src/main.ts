import './styles.css'
import {
  createEditor,
  executeEditorCommand,
  getEditorCommandState,
  getEditorStatus,
  getMarkdown,
  isAllowedLink,
  replaceEditorDocument,
} from './editor'
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

let documentId: string = crypto.randomUUID()
let revision = 0
let compositionActive = false
let compositionChanged = false
let suppressUpdate = false

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
  const headings: Array<{ level: number; text: string }> = []
  editor.state.doc.descendants((node) => {
    if (node.type.name === 'heading') {
      headings.push({ level: node.attrs.level as number, text: node.textContent })
    }
  })
  send('outlineChanged', { headings })
}

function sendCommandState(): void {
  send('commandStateChanged', getEditorCommandState(editor))
}

function sendEditorStatus(): void {
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
    sendOutline()
    sendEditorState()
  })

  targetEditor.on('selectionUpdate', () => {
    if (!compositionActive) {
      send('selectionChanged', {
        from: targetEditor.state.selection.from,
        to: targetEditor.state.selection.to,
      })
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
      sendOutline()
      sendEditorState()
    }
    compositionChanged = false
  })
}

bindEditorEvents(editor)

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
      editor = replaceEditorDocument(editor, editorMount, payload.markdown)
      bindEditorEvents(editor)
      suppressUpdate = false
      send('documentLoaded', undefined, message.requestId)
      sendOutline()
      sendEditorState()
      break
    }
    case 'requestSnapshot':
      send('snapshot', { markdown: getMarkdown(editor) }, message.requestId)
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
        const coordinates = typeof payload.clientX === 'number' && typeof payload.clientY === 'number'
          ? { left: payload.clientX, top: payload.clientY }
          : undefined
        const success = executeEditorCommand(
          editor,
          payload.command,
          typeof payload.text === 'string' ? payload.text : undefined,
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
