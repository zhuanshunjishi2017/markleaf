import './styles.css'
import '../../styles/base.css'
import '../../styles/sans.css'
import './vscode.css'
import {
  collapseSourceEditor, createEditor, executeEditorCommand, expandSourceEditor,
  getMarkdown, pasteMarkdownText, scrollToFootnoteDefinition, setCodeBlockControlHandlers,
  setCodeHighlightVisible, setHostImageResolver, shouldParsePastedTextAsMarkdown, updateEditorMarkdown,
} from './editor'
import { rerenderMermaidElements } from './mermaid'
import { TextDocumentSync } from './vscode-sync'
import { normalizeContextMenuCaretPosition } from './format-painter'
import type { ExtensionMessage, HostAction, WebviewMessage } from './vscode-protocol'

declare function acquireVsCodeApi(): {
  postMessage(message: WebviewMessage): void
  getState(): { mode?: 'read' | 'edit'; scrollTop?: number } | undefined
  setState(state: { mode: 'read' | 'edit'; scrollTop: number }): void
}

const vscode = acquireVsCodeApi()
const post = (message: WebviewMessage): void => vscode.postMessage(message)
const mount = document.querySelector<HTMLElement>('#editor')!
const modeButton = document.querySelector<HTMLButtonElement>('#mode')!
const status = document.querySelector<HTMLElement>('#sync-status')!
const count = document.querySelector<HTMLElement>('#word-count')!
const notice = document.querySelector<HTMLElement>('#notice')!
const noticeText = document.querySelector<HTMLElement>('#notice-text')!
const recover = document.querySelector<HTMLButtonElement>('#recover')!
const initialState = vscode.getState()
let mode = initialState?.mode ?? 'edit'
let writable = false
let suppressUpdate = false
let editor: ReturnType<typeof createEditor> | undefined
const pendingActions: HostAction[] = []
let actionInFlight = false
let actionState: ReturnType<typeof createEditor>['state'] | undefined
let noticeError = ''
let renderingFailed = false
let restoringScroll = true
const imageUrls = new Map<string, string>()
const requestedImages = new Set<string>()

function imageUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path
  const cached = imageUrls.get(path)
  if (cached) return cached
  if (path && !requestedImages.has(path)) {
    requestedImages.add(path)
    post({ type: 'resolveImages', paths: [path] })
  }
  return ''
}
setHostImageResolver(imageUrl)
setCodeBlockControlHandlers({
  copyCode: text => post({ type: 'copy', text }),
  editLanguage: (position, language) => { if (editor?.isEditable) post({ type: 'codeLanguage', position, language }) },
})

const sync = new TextDocumentSync({
  post,
  render(document) {
    const scrollTop = window.scrollY
    writable = document.writable
    suppressUpdate = true
    try {
      if (editor) {
        collapseSourceEditor(editor)
        updateEditorMarkdown(editor, document.markdown)
      } else {
        editor = createEditor(mount, document.markdown, true, {
          externalHistory: true,
          handlePaste(event) {
            if (!editor?.isEditable) return false
            const text = event.clipboardData?.getData('text/plain') ?? ''
            const html = event.clipboardData?.getData('text/html') ?? ''
            if (!editor.isActive('codeBlock') && shouldParsePastedTextAsMarkdown(editor, text, html)) {
              return pasteMarkdownText(editor, text)
            }
            return false
          },
        })
        setCodeHighlightVisible(editor, true)
        editor.on('update', ({ transaction }) => {
          if (!suppressUpdate && transaction.docChanged) sync.change(getMarkdown(editor!))
        })
        editor.on('selectionUpdate', updateToolbar)
      }
      mount.setAttribute('aria-busy', 'false')
      renderingFailed = false
      noticeError = ''
      const target = restoringScroll ? initialState?.scrollTop ?? 0 : scrollTop
      restoringScroll = false
      requestAnimationFrame(() => window.scrollTo(0, target))
      return getMarkdown(editor)
    } finally { suppressUpdate = false }
  },
  status() {
    if (!editor) return
    const editable = writable && mode === 'edit' && !sync.conflict && !renderingFailed
    if (editor.isEditable !== editable) editor.setEditable(editable, false)
    mount.dataset.readOnly = String(!editable)
    mount.dataset.vscodeContext = JSON.stringify({ markleafCanEdit: editable })
    modeButton.textContent = mode === 'read' ? '编辑' : '阅读'
    modeButton.setAttribute('aria-pressed', String(mode === 'read'))
    modeButton.disabled = !writable || !!sync.conflict || renderingFailed
    for (const button of document.querySelectorAll<HTMLButtonElement>('[data-edit]')) button.disabled = !editable
    noticeText.textContent = sync.conflict ?? noticeError
    notice.hidden = !noticeText.textContent
    recover.hidden = !sync.conflict
    status.textContent = renderingFailed ? '文档加载失败' : sync.conflict ? '存在未同步编辑' : sync.pending ? '正在同步…' : !writable ? '文件只读' : mode === 'read' ? '阅读模式' : '已同步到 VS Code'
    count.textContent = `${editor.state.doc.textContent.length.toLocaleString()} 字符`
    updateToolbar()
    runNextAction()
  },
})

function updateToolbar(): void {
  if (!editor) return
  for (const [command, mark] of [['toggleBold', 'bold'], ['toggleItalic', 'italic'], ['toggleUnderline', 'underline'], ['toggleStrike', 'strike'], ['toggleHighlight', 'highlight'], ['toggleCode', 'code']]) {
    document.querySelector(`[data-command="${command}"]`)?.setAttribute('aria-pressed', String(editor.isActive(mark!)))
  }
}

function action(action: HostAction): void {
  if (sync.conflict) return
  if (['format', 'insertLink', 'insertImage'].includes(action) && !editor?.isEditable) return
  pendingActions.push(action)
  runNextAction()
}

function runNextAction(): void {
  if (sync.pending || sync.conflict || actionInFlight) return
  const action = pendingActions.shift()
  if (action) {
    actionInFlight = true
    // Native menus can move focus and selection. Keep the original target,
    // but never reuse its positions after the document itself has changed.
    actionState = ['format', 'insertLink', 'insertImage'].includes(action) ? editor?.state : undefined
    post({ type: 'action', action })
  }
}

function command(command: string, text?: string, fromHost = false): void {
  if (!editor?.isEditable || sync.conflict) return
  if (fromHost && actionState) {
    if (editor.state.doc !== actionState.doc) {
      noticeError = '选择操作期间文档已改变，请重新选择文本后再执行。'
      noticeText.textContent = noticeError
      notice.hidden = false
      return
    }
    editor.view.dispatch(editor.state.tr.setSelection(actionState.selection))
  }
  if (!executeEditorCommand(editor, command, text)) {
    noticeError = '此操作不适用于当前选区。请将光标放入目标段落或表格后重试。'
    noticeText.textContent = noticeError
    notice.hidden = false
  } else {
    noticeError = ''
    notice.hidden = true
  }
}

document.querySelector('#toolbar')!.addEventListener('mousedown', event => {
  if ((event.target as Element).closest('button')) event.preventDefault()
})
document.querySelector('#toolbar')!.addEventListener('click', event => {
  const button = (event.target as Element).closest<HTMLButtonElement>('button')
  if (!button || button.disabled) return
  if (button.dataset.command) command(button.dataset.command)
  if (button.dataset.action) action(button.dataset.action as HostAction)
})
modeButton.addEventListener('click', () => {
  if (!editor || sync.pending || sync.conflict) return
  collapseSourceEditor(editor)
  mode = mode === 'read' ? 'edit' : 'read'
  vscode.setState({ mode, scrollTop: window.scrollY })
  sync.change(getMarkdown(editor))
})
recover.addEventListener('click', () => {
  recover.disabled = true
  post({ type: 'recoverDraft', markdown: sync.markdown })
})

// Capture before Tiptap and the formula/Mermaid source controls can use their
// local history. Composition is submitted as one committed edit.
window.addEventListener('keydown', event => {
  const primary = /Mac/i.test(navigator.platform) ? event.metaKey : event.ctrlKey
  if (!primary || event.altKey || event.isComposing) return
  const key = event.key.toLowerCase()
  const requested = key === 's' && !event.shiftKey ? 'save'
    : key === 'z' ? (event.shiftKey ? 'redo' : 'undo') : key === 'y' ? 'redo' : undefined
  if (!requested) return
  event.preventDefault()
  event.stopImmediatePropagation()
  action(requested)
}, true)
function editorInputTarget(target: EventTarget | null): boolean {
  return target instanceof Element && (mount.contains(target) || !!target.closest('.markleaf-expanded-source'))
}
document.addEventListener('compositionstart', event => {
  if (editorInputTarget(event.target)) sync.setComposing(true)
}, true)
document.addEventListener('compositionend', event => {
  if (!editorInputTarget(event.target)) return
  // Let ProseMirror and expanded formula source inputs commit their final DOM.
  setTimeout(() => {
    if (editor && !suppressUpdate) sync.change(getMarkdown(editor))
    sync.setComposing(false)
  }, 0)
}, true)
mount.addEventListener('beforeinput', event => {
  if (!editor?.isEditable) event.preventDefault()
}, true)
mount.addEventListener('contextmenu', event => {
  if (!editor?.isEditable || !(event.target instanceof Element)
    || event.target.closest('textarea, input, .markleaf-expanded-source')) return
  const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (!resolved) return
  const selection = editor.state.selection
  if (!selection.empty && resolved.pos >= selection.from && resolved.pos <= selection.to) return
  const node = resolved.inside >= 0 ? editor.state.doc.nodeAt(resolved.inside) : null
  if (node?.isAtom && node.type.spec.selectable !== false) editor.commands.setNodeSelection(resolved.inside)
  else editor.commands.setTextSelection(normalizeContextMenuCaretPosition(editor, resolved.pos))
})
mount.addEventListener('click', event => {
  const target = (event.target as Element)
  const footnote = target.closest<HTMLElement>('sup[data-footnote-ref]')
  if (footnote && editor) { event.preventDefault(); scrollToFootnoteDefinition(editor, footnote.dataset.footnoteRef ?? ''); return }
  const link = target.closest<HTMLAnchorElement>('a[href]')
  if (!link) return
  event.preventDefault()
  const primary = /Mac/i.test(navigator.platform) ? event.metaKey : event.ctrlKey
  if (mode !== 'read' && !primary) return
  const href = link.getAttribute('href') ?? ''
  if (href.startsWith('#')) {
    let slug = href.slice(1)
    try { slug = decodeURIComponent(slug) } catch { /* Match the literal fragment. */ }
    const headings = Array.from(mount.querySelectorAll<HTMLElement>('h1,h2,h3,h4,h5,h6'))
    const occurrences = new Map<string, number>()
    const heading = headings.find(element => {
      const base = (element.textContent ?? '').toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-')
      const index = occurrences.get(base) ?? 0
      occurrences.set(base, index + 1)
      return slug === (index === 0 ? base : `${base}-${index}`)
    })
    heading?.scrollIntoView({ block: 'start', behavior: 'smooth' })
  } else post({ type: 'openLink', href })
})
mount.addEventListener('dblclick', event => {
  if (!editor || !(event.target instanceof Element)) return
  const node = event.target.closest('.markleaf-math, .markleaf-mermaid')
  if (!node) return
  const kind = node.classList.contains('markleaf-mermaid') ? 'mermaid'
    : node.classList.contains('markleaf-math-inline') ? 'mathInline' : 'mathBlock'
  editor.state.doc.descendants((documentNode, position) => {
    if (documentNode.type.name === kind && editor!.view.nodeDOM(position) === node) {
      expandSourceEditor(editor!, position, kind)
      return false
    }
    return true
  })
})

let scrollFrame = 0
window.addEventListener('scroll', () => {
  if (scrollFrame) return
  scrollFrame = requestAnimationFrame(() => {
    scrollFrame = 0
    vscode.setState({ mode, scrollTop: window.scrollY })
  })
}, { passive: true })
const themeObserver = new MutationObserver(() => rerenderMermaidElements(mount))
themeObserver.observe(document.body, { attributes: true, attributeFilter: ['class', 'data-vscode-theme-id'] })

window.addEventListener('message', (event: MessageEvent<ExtensionMessage>) => {
  const message = event.data
  if (!message || typeof message.type !== 'string') return
  try {
    switch (message.type) {
      case 'document': sync.receiveDocument(message); break
      case 'recovered': recover.disabled = false; pendingActions.length = 0; sync.reset(message.document); break
      case 'requestAction': action(message.action); break
      case 'actionFinished': actionInFlight = false; actionState = undefined; runNextAction(); break
      case 'accepted': sync.accept(message.sequence, message.version); break
      case 'rejected': sync.reject(message.sequence, message.error); break
      case 'flush': sync.flush(message.requestId); break
      case 'command': command(message.command, message.text, true); break
      case 'images':
        for (const [path, url] of Object.entries(message.urls)) imageUrls.set(path, url)
        for (const image of mount.querySelectorAll<HTMLImageElement>('img[data-markleaf-path]')) {
          const path = image.getAttribute('data-markleaf-path')!
          const url = imageUrls.get(path)
          if (url && image.getAttribute('src') !== url) image.src = url
        }
        break
      case 'settings':
        document.documentElement.style.setProperty('--ml-font-size', `${Math.max(10, Math.min(32, message.fontSize))}px`)
        document.documentElement.style.setProperty('--ml-max-width', `${Math.max(320, Math.min(1600, message.maxWidth))}px`)
        if (!editor && !initialState?.mode) mode = message.defaultMode
        break
      case 'error':
        recover.disabled = false
        noticeError = message.message
        noticeText.textContent = message.message
        notice.hidden = false
        break
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    renderingFailed = true
    editor?.setEditable(false, false)
    mount.dataset.readOnly = 'true'
    modeButton.disabled = true
    noticeError = `加载文档失败：${message}`
    noticeText.textContent = noticeError
    status.textContent = '文档加载失败'
    notice.hidden = false
    post({ type: 'error', message })
  }
})
window.addEventListener('pagehide', () => {
  themeObserver.disconnect()
  editor?.destroy()
})
post({ type: 'ready' })
