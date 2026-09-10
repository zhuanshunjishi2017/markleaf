import './styles.css'
import '../../styles/base.css'
import '../../styles/sans.css'
import './vscode.css'
import { Fragment, Slice } from '@tiptap/pm/model'
import {
  collapseSourceEditor, createEditor, executeEditorCommand, expandSourceEditor,
  getMarkdown, getFootnoteLabels, getEditorCommandState, exportEditorSelection, pasteMarkdownText, scrollToFootnoteDefinition, setCodeBlockControlHandlers,
  setHostImageResolver, shouldParsePastedTextAsMarkdown, updateEditorMarkdown,
} from './editor'
import { createFindBar } from './vscode-find'
import { createReadingView } from './vscode-reading'
import { defaultSettings, type MarkLeafSettings } from './vscode-settings'
import { rerenderMermaidElements } from './mermaid'
import { TextDocumentSync } from './vscode-sync'
import { createEditorInteractions } from './vscode-interactions'
import { normalizeContextMenuCaretPosition } from './format-painter'
import { formatActions, formatCommandsWithInput, isFormatCommand, resolveShortcuts, shortcutLabel, type ShortcutSettings } from './vscode-shortcuts'
import { bindFormatShortcuts } from './vscode-shortcut-keys'
import { createShortcutDialog } from './vscode-shortcut-dialog'
import type { ActionContext, ExtensionMessage, HostAction, ImageUpload, WebviewFocus, WebviewMessage } from './vscode-protocol'

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
let interactions: ReturnType<typeof createEditorInteractions> | undefined
let writable = false
let suppressUpdate = false
let editor: ReturnType<typeof createEditor> | undefined
type PendingAction = { action: HostAction; command?: string; state?: ReturnType<typeof createEditor>['state']; context?: ActionContext; files?: ImageUpload[]; loading?: boolean }
const pendingActions: PendingAction[] = []
let reading: ReturnType<typeof createReadingView> | undefined
let findBar: ReturnType<typeof createFindBar> | undefined
let settings: MarkLeafSettings = { ...defaultSettings }
let customCss = ''
let language = 'zh-Hans'
let actionInFlight = false
let actionState: ReturnType<typeof createEditor>['state'] | undefined
let noticeError = ''
let renderingFailed = false
let restoringScroll = true
let disposed = false
let reportedFocus: WebviewFocus | undefined
const mac = /Mac/i.test(navigator.platform)
let shortcuts = resolveShortcuts({}, mac)
let unbindShortcuts: (() => void) | undefined
const shortcutDialog = createShortcutDialog(mac, post, () => { editor?.view.focus(); updateFocus() })
const fileReaders = new Set<FileReader>()
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
  editLanguage: position => {
    if (!editor?.isEditable) return
    editor.commands.setTextSelection(position + 1)
    action('codeLanguage')
  },
})

const sync = new TextDocumentSync({
  post,
  render(document) {
    const scrollTop = window.scrollY
    writable = document.writable
    suppressUpdate = true
    try {
      if (editor) {
        interactions?.cancel()
        collapseSourceEditor(editor)
        updateEditorMarkdown(editor, document.markdown)
      } else {
        editor = createEditor(mount, document.markdown, true, {
          externalHistory: true,
          sourceEditorPlacement: 'below',
          handlePaste(event) {
            if (!editor?.isEditable) return false
            const files = Array.from(event.clipboardData?.files ?? []).filter(file => file.type.startsWith('image/'))
            if (files.length) { void importImageFiles(files); return true }
            const text = event.clipboardData?.getData('text/plain') ?? ''
            const html = event.clipboardData?.getData('text/html') ?? ''
            if (!editor.isActive('codeBlock') && shouldParsePastedTextAsMarkdown(editor, text, html)) {
              return pasteMarkdownText(editor, text)
            }
            return false
          },
        })
        editor.on('update', ({ transaction }) => {
          if (!suppressUpdate && transaction.docChanged) sync.change(getMarkdown(editor!))
        })
        editor.on('selectionUpdate', updateToolbar)
        unbindShortcuts = bindFormatShortcuts(editor.view.dom, {
          mac, enabled: () => !!editor?.isEditable && !sync.conflict && !renderingFailed && !actionInFlight && !shortcutDialog.isOpen,
          shortcuts: () => shortcuts, run: id => runFormatCommand(id, true),
        })
        interactions = createEditorInteractions(editor, mount, () => action('block'))
        findBar = createFindBar(editor, window.document.querySelector<HTMLElement>('#toolbar')!)
        reading = createReadingView(editor, mount, count)
        reading.apply(settings, customCss, language)
      }
      reading?.rebuildOutline()
      findBar?.refresh()
      mount.setAttribute('aria-busy', 'false')
      renderingFailed = false
      noticeError = ''
      const target = restoringScroll ? initialState?.scrollTop ?? 0 : scrollTop
      restoringScroll = false
      requestAnimationFrame(() => window.scrollTo(0, target))
      return getMarkdown(editor)
    } finally { suppressUpdate = false }
  },
  status: updateStatus,
})

function updateStatus(): void {
    if (!editor) return
    const editable = writable && mode === 'edit' && !sync.conflict && !renderingFailed
    if (editor.isEditable !== editable) {
      if (!editable) collapseSourceEditor(editor)
      editor.setEditable(editable, false)
    }
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
    reading?.update()
    findBar?.update()
    updateToolbar()
    interactions?.update()
    runNextAction()
  }

function updateToolbar(): void {
  if (!editor) return
  const imageButton = document.querySelector<HTMLButtonElement>('[data-action="image"]')
  if (imageButton) imageButton.disabled = !getEditorCommandState(editor).imageSelected
  for (const [actionName, value] of [['toggleOutline', settings.showOutline], ['toggleFocus', settings.focusMode], ['toggleTypewriter', settings.typewriterMode]]) {
    document.querySelector(`[data-action="${actionName}"]`)?.setAttribute('aria-pressed', String(value))
  }
  mount.dataset.vscodeContext = JSON.stringify({ markleafCanEdit: editor.isEditable, markleafImage: getEditorCommandState(editor).imageSelected })
  for (const [command, mark] of [['toggleBold', 'bold'], ['toggleItalic', 'italic'], ['toggleUnderline', 'underline'], ['toggleStrike', 'strike'], ['toggleHighlight', 'highlight'], ['toggleCode', 'code']]) {
    document.querySelector(`[data-command="${command}"]`)?.setAttribute('aria-pressed', String(editor.isActive(mark!)))
  }
}

function showError(message: string): void {
  noticeError = message
  noticeText.textContent = message
  notice.hidden = !message
}

function action(action: HostAction, formatCommand?: string): void {
  if (action === 'shortcuts') { closeToolbarMenus(); shortcutDialog.open(); updateFocus(); return }
  if (!editor) return
  if (shortcutDialog.isOpen) return
  if (action === 'find' || action === 'replace') { findBar?.open(action === 'replace'); return }
  if (action === 'toggleRead') { modeButton.click(); return }
  const toggles = { toggleOutline: 'showOutline', toggleFocus: 'focusMode', toggleTypewriter: 'typewriterMode' } as const
  if (action in toggles) {
    const key = toggles[action as keyof typeof toggles]
    post({ type: 'updateSetting', key, value: !settings[key] })
    return
  }
  if (['zoomIn', 'zoomOut', 'zoomReset'].includes(action)) {
    post({ type: 'updateSetting', key: 'zoom', value: action === 'zoomReset' ? 100 : Math.max(50, Math.min(200, settings.zoom + (action === 'zoomIn' ? 10 : -10))) })
    return
  }
  if (['copyMarkdown', 'copyPlainText', 'copyHtml'].includes(action)) {
    const selection = exportEditorSelection(editor)
    if (editor.state.selection.empty) { showError('请先选择要复制的内容。'); return }
    post({ type: 'copy', text: action === 'copyMarkdown' ? selection.markdown : action === 'copyHtml' ? selection.html : selection.text })
    return
  }
  if (sync.conflict) return
  const target = ['format', 'block', 'formatCommand', 'insertLink', 'insertImage', 'insertImageUrl', 'image', 'pastePlainText', 'codeLanguage'].includes(action)
  if (target && !editor.isEditable && action !== 'image') return
  pendingActions.push({ action, ...(formatCommand ? { command: formatCommand } : {}), ...(target ? { state: editor.state, context: actionContext() } : {}) })
  runNextAction()
}

function runNextAction(): void {
  if (disposed || sync.pending || sync.conflict || actionInFlight || pendingActions[0]?.loading) return
  const pending = pendingActions.shift()
  if (!pending) return
  if (pending.state && !editor?.isEditable && pending.action !== 'image') {
    showError('当前文档不可编辑，请切换到编辑模式后重试。')
    runNextAction()
    return
  }
  if (pending.state && pending.state.doc !== editor?.state.doc) {
    showError('选择操作期间文档已改变，请重新选择文本后再执行。')
    runNextAction()
    return
  }
  actionInFlight = true
  actionState = pending.state
  post({ type: 'action', action: pending.action, ...(pending.command ? { command: pending.command } : {}), ...(pending.context ? { context: pending.context } : {}), ...(pending.files ? { files: pending.files } : {}) })
}

function runFormatCommand(id: string, fromShortcut = false): void {
  if (!isFormatCommand(id) || !editor?.isEditable || actionInFlight || shortcutDialog.isOpen) return
  // Keep Tiptap's heading-key behavior: pressing the same heading key again
  // returns to a paragraph. Menu commands continue to set the requested level.
  if (fromShortcut && /^setHeading[1-6]$/.test(id) && editor.isActive('heading', { level: Number(id.at(-1)) })) command('setParagraph')
  else if (formatCommandsWithInput.has(id)) action('formatCommand', id)
  else command(id)
}

function updateShortcuts(next: ShortcutSettings): void {
  shortcuts = resolveShortcuts(next.overrides, mac)
  shortcutDialog.update(next)
  for (const button of document.querySelectorAll<HTMLButtonElement>('#toolbar [data-command]')) {
    const id = button.dataset.command!
    const label = formatActions.find(action => action.command === id)?.label ?? button.textContent ?? id
    button.dataset.shortcutTitle ??= button.title.replace(/\s*\(Ctrl\/Cmd[^)]*\)/g, '') || label
    const key = shortcutLabel(shortcuts.bindings[id] ?? '', mac)
    button.title = `${button.dataset.shortcutTitle}${shortcuts.errors[id] ? '（快捷键配置无效）' : key ? ` (${key})` : '（未绑定快捷键）'}`
  }
}

async function importImageFiles(files: File[]): Promise<void> {
  if (!editor?.isEditable || sync.conflict) return
  const state = editor.state
  // Reserve the operation's place before reading bytes so a later source or
  // save command cannot overtake the paste/drop operation.
  const pending: PendingAction = { action: 'importImages', state, loading: true }
  pendingActions.push(pending)
  try {
    const uploads: ImageUpload[] = []
    for (const file of files) {
      if (file.size > 16 * 1024 * 1024) throw new Error('单张图片不能超过 16 MiB。')
      const data = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader()
        fileReaders.add(reader)
        reader.onloadend = () => fileReaders.delete(reader)
        reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '')
        reader.onerror = () => reject(reader.error ?? new Error('读取图片失败'))
        reader.onabort = () => reject(new Error('图片读取已取消'))
        reader.readAsDataURL(file)
      })
      const extension = file.type.split('/')[1]?.replace('svg+xml', 'svg') || 'png'
      uploads.push({ name: /\.[a-z0-9]+$/i.test(file.name) ? file.name : `${file.name || 'image'}.${extension}`, data })
    }
    pending.files = uploads
    pending.loading = false
    runNextAction()
  } catch (error) {
    const index = pendingActions.indexOf(pending)
    if (index >= 0) pendingActions.splice(index, 1)
    if (!disposed) { showError(error instanceof Error ? error.message : String(error)); runNextAction() }
  }
}

function actionContext(): ActionContext {
  if (!editor) return {}
  return { editable: editor.isEditable, ...getEditorCommandState(editor), ...interactions?.state(),
    imageSource: String(editor.getAttributes('image').src ?? ''),
    linkHref: String(editor.getAttributes('link').href ?? ''), footnoteLabels: getFootnoteLabels(editor) }
}

function command(command: string, text?: string, fromHost = false): void {
  if (!editor?.isEditable || sync.conflict) {
    if (command === 'insertImages') showError(`图片已保存，但当前文档不可编辑。图片路径：${text ?? ''}`)
    return
  }
  if (fromHost && actionState) {
    if (editor.state.doc !== actionState.doc) {
      noticeError = '选择操作期间文档已改变，请重新选择文本后再执行。' + (command === 'insertImages' ? ` 已保存的图片路径：${text ?? ''}` : '')
      noticeText.textContent = noticeError
      notice.hidden = false
      return
    }
    editor.view.dispatch(editor.state.tr.setSelection(actionState.selection))
  }
  if (command === 'copyCodeBlock') {
    post({ type: 'copy', text: getEditorCommandState(editor).codeBlockText ?? '' })
    return
  }
  let success: boolean | undefined
  if (command === 'insertImages') {
    const paths: string[] = JSON.parse(text ?? '[]')
    success = editor.chain().focus().insertContent(paths.map(src => ({ type: 'image', attrs: { src, alt: decodeURIComponent(src.split('/').at(-1) ?? '图片') } }))).run()
  } else if (command === 'pastePlainText') {
    if (!text) { showError('剪贴板中没有文本。'); return }
    // The browser paste pipeline also runs Tiptap's Markdown paste rules.
    // A literal slice bypasses those rules as well as HTML parsing.
    const transaction = editor.state.tr
    if (editor.state.selection.$from.parent.type.spec.code) transaction.insertText(text)
    else {
      const paragraphs = text.split(/\r\n?|\n/).map(line => editor!.schema.nodes.paragraph!.create(null, line ? editor!.schema.text(line) : undefined))
      transaction.replaceSelection(Slice.maxOpen(Fragment.fromArray(paragraphs)))
    }
    editor.view.dispatch(transaction.scrollIntoView())
    success = true
  } else success = command === 'formatPainter' ? interactions?.togglePainter() : executeEditorCommand(editor, command, text)
  if (!success) {
    noticeError = '此操作不适用于当前选区。请将光标放入目标段落或表格后重试。'
    noticeText.textContent = noticeError
    notice.hidden = false
  } else {
    noticeError = ''
    notice.hidden = true
  }
}

const toolbar = document.querySelector<HTMLElement>('#toolbar')!
function closeToolbarMenus(except?: Element | null): void {
  toolbar.querySelectorAll<HTMLDetailsElement>('details[open]').forEach(menu => {
    if (menu !== except) menu.open = false
  })
}
document.addEventListener('pointerdown', event => {
  closeToolbarMenus(event.target instanceof Element ? event.target.closest('#toolbar details') : null)
}, true)
toolbar.addEventListener('toggle', event => {
  if (event.target instanceof HTMLDetailsElement && event.target.open) closeToolbarMenus(event.target)
}, true)
document.addEventListener('keydown', event => {
  if (event.key !== 'Escape' || event.isComposing) return
  const open = toolbar.querySelector<HTMLDetailsElement>('details[open]')
  if (!open) return
  closeToolbarMenus()
  open.querySelector('summary')?.focus()
  event.preventDefault()
  event.stopPropagation()
}, true)
window.addEventListener('blur', () => closeToolbarMenus())
toolbar.addEventListener('mousedown', event => {
  if ((event.target as Element).closest('button')) event.preventDefault()
})
toolbar.addEventListener('click', event => {
  const summary = (event.target as Element).closest('summary')
  if (summary) closeToolbarMenus(summary.parentElement)
  const button = (event.target as Element).closest<HTMLButtonElement>('button')
  if (!button || button.disabled) return
  if (button.dataset.command) command(button.dataset.command)
  if (button.dataset.action) action(button.dataset.action as HostAction)
  button.closest('details')?.removeAttribute('open')
})
modeButton.addEventListener('click', () => {
  if (!editor || sync.pending || sync.conflict) return
  collapseSourceEditor(editor)
  mode = mode === 'read' ? 'edit' : 'read'
  vscode.setState({ mode, scrollTop: window.scrollY })
  interactions?.cancel()
  updateStatus()
})
recover.addEventListener('click', () => {
  recover.disabled = true
  post({ type: 'recoverDraft', markdown: sync.markdown })
})

// Capture before Tiptap and the formula/Mermaid source controls can use their
// local history. Composition is submitted as one committed edit.
window.addEventListener('keydown', event => {
  if (shortcutDialog.contains(event.target)) return
  const primary = /Mac/i.test(navigator.platform) ? event.metaKey : event.ctrlKey
  if (!primary || event.altKey || event.isComposing) return
  const key = event.key.toLowerCase()
  // Find/replace and view switching are dispatched by VS Code, so user
  // keybinding changes apply without hardcoded shortcuts intercepting them.
  // Search inputs keep their own text undo; document history belongs to VS Code.
  if (event.target instanceof Element && event.target.closest('#find-bar')) return
  const requested = key === 's' && !event.shiftKey ? 'save'
    : key === 'z' ? (event.shiftKey ? 'redo' : 'undo') : key === 'y' ? 'redo' : undefined
  if (!requested) return
  event.preventDefault()
  event.stopImmediatePropagation()
  action(requested)
}, true)
document.addEventListener('keydown', event => {
  // Keep shortcuts already handled by Tiptap or a source input inside the
  // webview. VS Code otherwise forwards them even after preventDefault().
  if (event.defaultPrevented) event.stopPropagation()
})

function reportFocus(target: WebviewFocus): void {
  if (disposed || target === reportedFocus) return
  reportedFocus = target
  post({ type: 'focus', target })
}
function updateFocus(): void {
  reportFocus(!document.hasFocus() || shortcutDialog.isOpen ? null
    : document.activeElement?.closest('input, textarea, select, .markleaf-expanded-source') ? 'input' : 'document')
}
window.addEventListener('focus', updateFocus)
window.addEventListener('blur', () => reportFocus(null))
document.addEventListener('focusin', updateFocus)
document.addEventListener('focusout', () => queueMicrotask(updateFocus))
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
mount.addEventListener('dragover', event => {
  if (editor?.isEditable && event.dataTransfer?.types.includes('Files')) event.preventDefault()
})
mount.addEventListener('drop', event => {
  const files = Array.from(event.dataTransfer?.files ?? []).filter(file => file.type.startsWith('image/'))
  if (!files.length) return
  event.preventDefault()
  event.stopPropagation()
  if (!editor?.isEditable) return
  const position = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (position) editor.commands.setTextSelection(normalizeContextMenuCaretPosition(editor, position.pos))
  void importImageFiles(files)
}, true)
mount.addEventListener('copy', event => {
  if (!editor || !(event.target instanceof Element) || event.target.closest('input, textarea, .markleaf-expanded-source') || editor.state.selection.empty || !event.clipboardData) return
  const selection = exportEditorSelection(editor)
  event.clipboardData.setData('text/plain', selection.text)
  event.clipboardData.setData('text/html', selection.html)
  event.preventDefault()
}, true)
let zoomTimer: ReturnType<typeof setTimeout> | undefined
let wheelZoom: number | undefined
mount.addEventListener('wheel', event => {
  if (!event.ctrlKey || !settings.ctrlWheelZoom) return
  event.preventDefault()
  wheelZoom = Math.max(50, Math.min(200, (wheelZoom ?? settings.zoom) + (event.deltaY < 0 ? 10 : -10)))
  clearTimeout(zoomTimer)
  zoomTimer = setTimeout(() => { post({ type: 'updateSetting', key: 'zoom', value: wheelZoom! }); wheelZoom = undefined }, 120)
}, { passive: false })
mount.addEventListener('contextmenu', event => {
  if (!editor || !(event.target instanceof Element)
    || event.target.closest('textarea, input, .markleaf-expanded-source')) return
  const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
  if (!resolved) return
  const selection = editor.state.selection
  if (!selection.empty && resolved.pos >= selection.from && resolved.pos <= selection.to) return
  const node = resolved.inside >= 0 ? editor.state.doc.nodeAt(resolved.inside) : null
  if (node?.isAtom && node.type.spec.selectable !== false) editor.commands.setNodeSelection(resolved.inside)
  else if (editor.isEditable) editor.commands.setTextSelection(normalizeContextMenuCaretPosition(editor, resolved.pos))
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
  if (!editor?.isEditable || !(event.target instanceof Element)) return
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
function updateTheme(): void {
  const dark = settings.colorTheme === 'vscode' ? document.body.classList.contains('vscode-dark') || document.body.classList.contains('vscode-high-contrast')
    : ['apple-dark', 'dark', 'deep-sea', 'espresso', 'high-contrast-dark', 'morandi-dark', 'pure-black'].includes(settings.colorTheme)
  document.body.classList.toggle('markleaf-theme-dark', dark)
  rerenderMermaidElements(mount)
}
const themeObserver = new MutationObserver(updateTheme)
themeObserver.observe(document.body, { attributes: true, attributeFilter: ['class', 'data-vscode-theme-id'] })

window.addEventListener('message', (event: MessageEvent<ExtensionMessage>) => {
  const message = event.data
  if (!message || typeof message.type !== 'string') return
  try {
    switch (message.type) {
      case 'document': sync.receiveDocument(message); break
      case 'recovered': recover.disabled = false; pendingActions.length = 0; sync.reset(message.document); break
      case 'requestAction': action(message.action); break
      case 'requestFormatCommand': runFormatCommand(message.command); break
      case 'shortcutSaved':
        updateShortcuts(message.shortcuts)
        shortcutDialog.saved(message.requestId, message.shortcuts, message.error)
        break
      case 'actionFinished': actionInFlight = false; actionState = undefined; interactions?.clearHighlight(); runNextAction(); break
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
        updateShortcuts(message.shortcuts ?? { overrides: {}, scope: 'user' })
        settings = message.settings
        customCss = message.customCss ?? ''
        language = message.language ?? 'zh-Hans'
        if (!editor && !initialState?.mode) mode = settings.defaultMode
        reading?.apply(settings, customCss, language)
        updateTheme()
        updateStatus()
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
  reportFocus(null)
  disposed = true
  for (const reader of fileReaders) reader.abort()
  fileReaders.clear()
  themeObserver.disconnect()
  interactions?.dispose()
  findBar?.dispose()
  reading?.dispose()
  unbindShortcuts?.()
  shortcutDialog.dispose()
  cancelAnimationFrame(scrollFrame)
  clearTimeout(zoomTimer)
  editor?.destroy()
})
updateShortcuts({ overrides: {}, scope: 'user' })
post({ type: 'ready', mac })
updateFocus()
