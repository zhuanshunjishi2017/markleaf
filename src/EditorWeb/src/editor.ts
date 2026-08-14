import { Editor, Extension, ResizableNodeView } from '@tiptap/core'
import { Selection } from '@tiptap/pm/state'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import { DOMSerializer } from '@tiptap/pm/model'
import Image from '@tiptap/extension-image'
import Link from '@tiptap/extension-link'
import { TableKit } from '@tiptap/extension-table'
import TaskItem from '@tiptap/extension-task-item'
import TaskList from '@tiptap/extension-task-list'
import { Markdown } from '@tiptap/markdown'
import StarterKit from '@tiptap/starter-kit'

const imageMetadataPrefix = 'markleaf:'
const imageMetadataSeparator = ' || '
const imageMetadataPattern = /(?:^| \|\| )markleaf:width=(\d+);height=(\d+);rotation=(0|90|180|270)$/

type ImageMetadata = {
  title: string | null
  width: number | null
  height: number | null
  rotation: 0 | 90 | 180 | 270
}

const findHighlightKey = new PluginKey<FindHighlightState>('markleaf-find-highlight')
type TextMatch = { from: number; to: number }
type FindHighlightState = { matches: TextMatch[]; current: number }

const FindHighlight = Extension.create({
  name: 'markleafFindHighlight',
  addProseMirrorPlugins() {
    return [new Plugin({
      key: findHighlightKey,
      state: {
        init: (): FindHighlightState => ({ matches: [], current: -1 }),
        apply(transaction, previous): FindHighlightState {
          const update = transaction.getMeta(findHighlightKey) as FindHighlightState | undefined
          if (update) return update
          if (!transaction.docChanged) return previous
          return { matches: [], current: -1 }
        },
      },
      props: {
        decorations(state) {
          const highlight = findHighlightKey.getState(state) ?? { matches: [], current: -1 }
          return DecorationSet.create(state.doc, highlight.matches.map((match, index) => Decoration.inline(
            match.from,
            match.to,
            { class: index === highlight.current
              ? 'markleaf-find-match markleaf-find-match-current'
              : 'markleaf-find-match' },
          )))
        },
      },
    })]
  },
})

/// WYSIWYG 编辑器选区：用 ProseMirror 装饰绘制主题化选中背景。
/// WKWebView 对 contenteditable 忽略 ::selection，只能用真实 DOM span 才能两平台一致。
const themedSelectionKey = new PluginKey('markleaf-themed-selection')

const ThemedSelection = Extension.create({
  name: 'markleafThemedSelection',
  addProseMirrorPlugins() {
    return [new Plugin({
      key: themedSelectionKey,
      props: {
        decorations(state) {
          const { from, to, empty } = state.selection
          if (empty || from === to) return DecorationSet.empty
          return DecorationSet.create(state.doc, [
            Decoration.inline(from, to, { class: 'markleaf-themed-selection' }),
          ])
        },
      },
    })]
  },
})

const blockHandleKey = new PluginKey('markleaf-block-handle')
type BlockHandleState = { activeBlock: number | null }
let blockHandleVisible = true

let blockTypeLabels: Record<string, string> = {}

export function setBlockTypeLabels(labels: Record<string, string>): void {
  blockTypeLabels = labels
}

function getBlockTypeLabel(state: Editor['state'], from: number): string {
  const $from = state.doc.resolve(from)
  for (let depth = $from.depth; depth >= 1; depth -= 1) {
    const node = $from.node(depth)
    if (node.type.name === 'heading') return blockTypeLabels[`blockHeading${node.attrs.level}`] ?? 'H'
    if (node.type.name === 'bulletList') return blockTypeLabels.blockBulletList ?? '•'
    if (node.type.name === 'orderedList') return blockTypeLabels.blockOrderedList ?? '1.'
    if (node.type.name === 'taskList') return blockTypeLabels.blockTaskList ?? '☑'
    if (node.type.name === 'blockquote') return blockTypeLabels.blockBlockquote ?? '❝'
    if (node.type.name === 'codeBlock') return blockTypeLabels.blockCodeBlock ?? '</>'
  }
  return blockTypeLabels.blockParagraph ?? '¶'
}

const BlockHandle = Extension.create({
  name: 'markleafBlockHandle',
  addProseMirrorPlugins() {
    return [new Plugin({
      key: blockHandleKey,
      state: {
        init: (): BlockHandleState => ({ activeBlock: null }),
        apply(transaction, previous): BlockHandleState {
          return transaction.getMeta(blockHandleKey) as BlockHandleState | undefined ?? previous
        },
      },
      props: {
        decorations(state) {
          if (!blockHandleVisible) return DecorationSet.empty
          const { activeBlock } = blockHandleKey.getState(state) ?? { activeBlock: null }
          const decorations: Decoration[] = []
          const { from, empty } = state.selection
          const $from = state.doc.resolve(from)
          const parentName = $from.parent.type.name
          let insideList = false
          for (let depth = $from.depth; depth >= 1; depth -= 1) {
            if (['bulletList', 'orderedList', 'taskList'].includes($from.node(depth).type.name)) {
              insideList = true
              break
            }
          }
          if (empty && !insideList && (parentName === 'paragraph' || parentName === 'heading' || parentName === 'codeBlock')) {
            const nodePos = $from.before($from.depth)
            decorations.push(Decoration.widget(
              $from.start(),
              () => createBlockHandle(nodePos, getBlockTypeLabel(state, from), activeBlock === nodePos),
              { side: -1 },
            ))
          }
          if (activeBlock !== null) {
            const node = state.doc.nodeAt(activeBlock)
            if (node) decorations.push(Decoration.node(
              activeBlock,
              activeBlock + node.nodeSize,
              { class: 'markleaf-block-active' },
            ))
          }
          return decorations.length > 0
            ? DecorationSet.create(state.doc, decorations)
            : DecorationSet.empty
        },
      },
    })]
  },
})

function createBlockHandle(nodePos: number, label: string, active: boolean): HTMLButtonElement {
  const handle = document.createElement('button')
  handle.type = 'button'
  handle.className = active ? 'ml-block-handle ml-block-handle-active' : 'ml-block-handle'
  handle.contentEditable = 'false'
  handle.setAttribute('aria-label', '段落操作')
  handle.setAttribute('tabindex', '-1')
  handle.textContent = label
  handle.addEventListener('mousedown', event => {
    event.preventDefault()
    event.stopPropagation()
    const rect = handle.getBoundingClientRect()
    handle.dispatchEvent(new CustomEvent('markleaf-block-handle', {
      bubbles: true,
      detail: { clientX: rect.left, clientY: rect.bottom + 6, position: nodePos },
    }))
  })
  requestAnimationFrame(() => {
    if (!handle.isConnected) return
    const documentElement = handle.closest('.markleaf-document')
    if (!documentElement) return
    handle.style.top = `${handle.getBoundingClientRect().top - documentElement.getBoundingClientRect().top}px`
  })
  return handle
}

export function setBlockHighlight(editor: Editor, position: number | null): void {
  editor.view.dispatch(editor.state.tr.setMeta(blockHandleKey, { activeBlock: position }))
}

export function setBlockHandleVisible(editor: Editor, visible: boolean): void {
  blockHandleVisible = visible
  const state = blockHandleKey.getState(editor.state) ?? { activeBlock: null }
  editor.view.dispatch(editor.state.tr.setMeta(blockHandleKey, state))
}

function parseImageMetadata(title: unknown): ImageMetadata {
  if (typeof title !== 'string') {
    return { title: null, width: null, height: null, rotation: 0 }
  }

  const match = imageMetadataPattern.exec(title)
  if (!match) {
    return { title, width: null, height: null, rotation: 0 }
  }

  const metadataStart = match.index + (match[0].startsWith(imageMetadataSeparator) ? imageMetadataSeparator.length : 0)
  const ordinaryTitle = title.slice(0, match.index).trimEnd()
  return {
    title: metadataStart === 0 ? null : ordinaryTitle || null,
    width: Number(match[1]),
    height: Number(match[2]),
    rotation: Number(match[3]) as ImageMetadata['rotation'],
  }
}

function serializeImageTitle(attrs: Record<string, unknown>): string | null {
  const title = typeof attrs.title === 'string' && attrs.title.length > 0 ? attrs.title : null
  const width = typeof attrs.width === 'number' && Number.isFinite(attrs.width) ? Math.round(attrs.width) : null
  const height = typeof attrs.height === 'number' && Number.isFinite(attrs.height) ? Math.round(attrs.height) : null
  const rotation = normalizeImageRotation(attrs.rotation)

  if (width === null || height === null) {
    return title
  }

  const metadata = `${imageMetadataPrefix}width=${width};height=${height};rotation=${rotation}`
  return title ? `${title}${imageMetadataSeparator}${metadata}` : metadata
}

function normalizeImageRotation(value: unknown): ImageMetadata['rotation'] {
  return value === 90 || value === 180 || value === 270 ? value : 0
}

function escapeMarkdownImageText(value: unknown): string {
  return typeof value === 'string' ? value.replace(/([\\\[\]])/g, '\\$1') : ''
}

function escapeMarkdownImageTitle(value: string): string {
  return value.replace(/([\\"])/g, '\\$1')
}

type SelectedImageNode = {
  type: { name: string }
  attrs: Record<string, unknown>
}

function getSelectedImage(editor: Editor): SelectedImageNode | null {
  const selection = editor.state.selection
  if (!('node' in selection)) {
    return null
  }
  const node = selection.node as SelectedImageNode
  return node.type.name === 'image' ? node : null
}

const MarkLeafImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      rotation: {
        default: 0,
        parseHTML: element => normalizeImageRotation(Number(element.getAttribute('data-markleaf-rotation'))),
        renderHTML: attributes => ({
          'data-markleaf-rotation': normalizeImageRotation(attributes.rotation),
        }),
      },
    }
  },

  renderHTML({ HTMLAttributes }) {
    const markdownPath = typeof HTMLAttributes.src === 'string' ? HTMLAttributes.src : ''
    return ['img', {
      ...HTMLAttributes,
      src: toVirtualImageUrl(markdownPath),
      'data-markleaf-path': markdownPath,
    }]
  },

  parseMarkdown(token, helpers) {
    const metadata = parseImageMetadata(token.title)
    return helpers.createNode('image', {
      src: token.href,
      alt: token.text,
      title: metadata.title,
      width: metadata.width,
      height: metadata.height,
      rotation: metadata.rotation,
    })
  },

  renderMarkdown(node) {
    const src = node.attrs?.src ?? ''
    const alt = escapeMarkdownImageText(node.attrs?.alt)
    const title = serializeImageTitle(node.attrs ?? {})
    return title
      ? `![${alt}](${src} "${escapeMarkdownImageTitle(title)}")`
      : `![${alt}](${src})`
  },

  addNodeView() {
    return ({ node, getPos, editor }) => {
      const frame = document.createElement('div')
      frame.className = 'markleaf-image-frame'
      const image = document.createElement('img')
      image.className = 'markleaf-image-content'
      image.draggable = false
      frame.appendChild(image)

      const applyImageLayout = (attrs: Record<string, unknown>, previewWidth?: number, previewHeight?: number) => {
        const width = previewWidth ?? (typeof attrs.width === 'number' ? attrs.width : null)
        const height = previewHeight ?? (typeof attrs.height === 'number' ? attrs.height : null)
        const rotation = normalizeImageRotation(attrs.rotation)
        const hasExplicitSize = width !== null && height !== null

        frame.dataset.markleafRotation = String(rotation)
        if (hasExplicitSize) {
          frame.style.width = `${width}px`
          frame.style.height = `${height}px`
          image.style.position = 'absolute'
          image.style.left = '50%'
          image.style.top = '50%'
          image.style.width = `${rotation === 90 || rotation === 270 ? height : width}px`
          image.style.height = `${rotation === 90 || rotation === 270 ? width : height}px`
          image.style.transform = `translate(-50%, -50%) rotate(${rotation}deg)`
        } else {
          frame.style.removeProperty('width')
          frame.style.removeProperty('height')
          image.style.position = 'static'
          image.style.removeProperty('left')
          image.style.removeProperty('top')
          image.style.width = 'auto'
          image.style.height = 'auto'
          image.style.transform = rotation === 0 ? 'none' : `rotate(${rotation}deg)`
        }
      }

      const syncImage = (attrs: Record<string, unknown>) => {
        const markdownPath = typeof attrs.src === 'string' ? attrs.src : ''
        image.src = toVirtualImageUrl(markdownPath)
        image.setAttribute('data-markleaf-path', markdownPath)
        image.setAttribute('data-markleaf-rotation', String(normalizeImageRotation(attrs.rotation)))
        if (typeof attrs.alt === 'string') image.alt = attrs.alt
        else image.removeAttribute('alt')
        if (typeof attrs.title === 'string') image.title = attrs.title
        else image.removeAttribute('title')
        applyImageLayout(attrs)
      }

      let currentNode = node
      const nodeView = new ResizableNodeView({
        element: frame,
        editor,
        node,
        getPos,
        onResize: (width, height) => applyImageLayout(currentNode.attrs, width, height),
        onCommit: (width, height) => {
          const position = getPos()
          if (position === undefined) return
          editor.view.dispatch(editor.state.tr.setNodeMarkup(position, undefined, {
            ...currentNode.attrs,
            width: Math.round(width),
            height: Math.round(height),
          }))
          editor.commands.setNodeSelection(position)
        },
        onUpdate: updatedNode => {
          if (updatedNode.type !== currentNode.type) return false
          currentNode = updatedNode
          syncImage(updatedNode.attrs)
          return true
        },
        options: {
          directions: ['top-left', 'top-right', 'bottom-left', 'bottom-right'],
          min: { width: 48, height: 48 },
          preserveAspectRatio: true,
          className: {
            container: 'markleaf-image-node',
            wrapper: 'markleaf-image-resize-wrapper',
            handle: 'markleaf-image-resize-handle',
            resizing: 'is-resizing',
          },
        },
      })

      syncImage(node.attrs)
      return nodeView
    }
  },
})

export type EditorCommandState = {
  canUndo: boolean
  canRedo: boolean
  hasSelection: boolean
  paragraph: boolean
  headingLevel: number | null
  bold: boolean
  italic: boolean
  underline: boolean
  strike: boolean
  code: boolean
  link: boolean
  blockquote: boolean
  codeBlock: boolean
  bulletList: boolean
  orderedList: boolean
  taskList: boolean
  inTable: boolean
  tableAlign: 'left' | 'center' | 'right' | null
  imageSelected: boolean
  canStartFormatPainter: boolean
  formatPainterArmed: boolean
}

export type EditorStatus = {
  characterCount: number
  selectedCharacterCount: number
  blockType: 'paragraph' | 'heading1' | 'heading2' | 'heading3' | 'heading4' | 'heading5' | 'heading6'
    | 'blockquote' | 'codeBlock' | 'bulletList' | 'orderedList' | 'taskList' | 'table' | 'image'
  line: number
  column: number
}

export const editorExtensions = [
  StarterKit.configure({
    link: false,
  }),
  Link.configure({
    openOnClick: false,
    autolink: false,
  }),
  MarkLeafImage.configure({
    allowBase64: false,
  }),
  TableKit.configure({
    table: {
      resizable: false,
    },
  }),
  TaskList,
  TaskItem.configure({ nested: true }),
  Markdown.configure({
    markedOptions: {
      gfm: true,
      breaks: false,
    },
  }),
  FindHighlight,
  ThemedSelection,
  BlockHandle,
]

export function createEditor(element: HTMLElement, content = ''): Editor {
  return new Editor({
    element,
    extensions: editorExtensions,
    content,
    contentType: 'markdown',
    autofocus: false,
    editorProps: {
      attributes: {
        class: 'markleaf-document',
        spellcheck: 'true',
      },
      transformPastedHTML: sanitizePastedHtml,
    },
  })
}

export function replaceEditorDocument(editor: Editor, element: HTMLElement, content: string): Editor {
  editor.destroy()
  return createEditor(element, content)
}

export function toVirtualImageUrl(markdownPath: string): string {
  // 远程图片（http/https）原样返回，由浏览器直接加载；仅本地路径走虚拟资源服务。
  if (/^(https?:|mailto:)/i.test(markdownPath)) {
    return markdownPath
  }
  let decodedPath = markdownPath
  try {
    decodedPath = decodeURIComponent(markdownPath)
  } catch {
    // Invalid percent escapes remain literal and are safely encoded below.
  }
  return `https://assets.local/image?path=${encodeURIComponent(decodedPath)}`
}

export function getMarkdown(editor: Editor): string {
  return editor.getMarkdown()
}

export type FindResult = { current: number; total: number }
export type SelectionExport = { text: string; markdown: string; html: string }

export function exportEditorSelection(editor: Editor): SelectionExport {
  const selection = editor.state.selection
  if (selection.empty) return { text: '', markdown: '', html: '' }
  const slice = editor.state.doc.slice(selection.from, selection.to)
  const container = document.createElement('div')
  container.append(DOMSerializer.fromSchema(editor.schema).serializeFragment(slice.content))
  const temporary = document.createElement('div')
  const selectionEditor = createEditor(temporary, container.innerHTML)
  const markdown = getMarkdown(selectionEditor)
  selectionEditor.destroy()
  return {
    text: slice.content.textBetween(0, slice.content.size, '\n', '\n'),
    markdown,
    html: container.innerHTML,
  }
}

export function findInEditor(
  editor: Editor,
  query: string,
  caseSensitive: boolean,
  wholeWord: boolean,
  backwards = false,
): FindResult {
  const matches = findEditorMatches(editor, query, caseSensitive, wholeWord)
  if (matches.length === 0) {
    setFindHighlights(editor, [], -1)
    return { current: 0, total: 0 }
  }
  const previous = findHighlightKey.getState(editor.state)
  const sameMatches = previous?.matches.length === matches.length
    && previous.matches.every((match, index) => match.from === matches[index]?.from && match.to === matches[index]?.to)
  const previousIndex = sameMatches ? previous?.current ?? -1 : -1
  const current = backwards
    ? (previousIndex <= 0 ? matches.length - 1 : previousIndex - 1)
    : (previousIndex + 1) % matches.length
  setFindHighlights(editor, matches, current)
  scrollCurrentMatchIntoView(editor)
  return { current: current + 1, total: matches.length }
}

export function replaceCurrentInEditor(
  editor: Editor,
  query: string,
  replacement: string,
  caseSensitive: boolean,
  wholeWord: boolean,
): FindResult {
  const matches = findEditorMatches(editor, query, caseSensitive, wholeWord)
  const highlight = findHighlightKey.getState(editor.state)
  const selected = highlight?.current === undefined ? undefined : matches[highlight.current]
  if (selected) editor.commands.insertContentAt(selected, replacement)
  return findInEditor(editor, query, caseSensitive, wholeWord)
}

export function replaceAllInEditor(
  editor: Editor,
  query: string,
  replacement: string,
  caseSensitive: boolean,
  wholeWord: boolean,
): number {
  const matches = findEditorMatches(editor, query, caseSensitive, wholeWord)
  if (matches.length === 0) return 0
  const transaction = editor.state.tr
  for (const match of [...matches].reverse()) transaction.insertText(replacement, match.from, match.to)
  editor.view.dispatch(transaction)
  setFindHighlights(editor, [], -1)
  return matches.length
}

export function clearFindHighlights(editor: Editor): void {
  setFindHighlights(editor, [], -1)
}

function setFindHighlights(editor: Editor, matches: TextMatch[], current: number): void {
  editor.view.dispatch(editor.state.tr.setMeta(findHighlightKey, { matches, current }))
}

function scrollCurrentMatchIntoView(editor: Editor): void {
  const current = editor.view.dom.querySelector<HTMLElement>('.markleaf-find-match-current')
  current?.scrollIntoView?.({ block: 'center', behavior: 'smooth' })
}

function findEditorMatches(editor: Editor, query: string, caseSensitive: boolean, wholeWord: boolean) {
  if (!query) return []
  const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const expression = new RegExp(
    wholeWord ? `(?<![\\p{L}\\p{N}_])${escaped}(?![\\p{L}\\p{N}_])` : escaped,
    caseSensitive ? 'gu' : 'giu',
  )
  const matches: Array<{ from: number; to: number }> = []
  editor.state.doc.descendants((node, position) => {
    if (!node.isTextblock) return
    for (const match of node.textContent.matchAll(expression)) {
      matches.push({ from: position + 1 + match.index, to: position + 1 + match.index + match[0].length })
    }
    return false
  })
  return matches
}

export function getEditorCommandState(editor: Editor): EditorCommandState {
  let headingLevel: number | null = null
  for (let level = 1; level <= 6; level += 1) {
    if (editor.isActive('heading', { level })) {
      headingLevel = level
      break
    }
  }

  const inTable = editor.isActive('table')
  const currentCell = editor.getAttributes('tableCell')
  const currentHeader = editor.getAttributes('tableHeader')
  const align = (currentCell.align ?? currentHeader.align) as unknown
  const tableAlign = align === 'left' || align === 'center' || align === 'right' ? align : null

  return {
    canUndo: editor.can().undo(),
    canRedo: editor.can().redo(),
    hasSelection: !editor.state.selection.empty,
    paragraph: editor.isActive('paragraph'),
    headingLevel,
    bold: editor.isActive('bold'),
    italic: editor.isActive('italic'),
    underline: editor.isActive('underline'),
    strike: editor.isActive('strike'),
    code: editor.isActive('code'),
    link: editor.isActive('link'),
    blockquote: editor.isActive('blockquote'),
    codeBlock: editor.isActive('codeBlock'),
    bulletList: editor.isActive('bulletList'),
    orderedList: editor.isActive('orderedList'),
    taskList: editor.isActive('taskList'),
    inTable,
    tableAlign,
    imageSelected: getSelectedImage(editor) !== null,
    canStartFormatPainter: false,
    formatPainterArmed: false,
  }
}

export function getEditorStatus(editor: Editor): EditorStatus {
  const selection = editor.state.selection
  const documentText = editor.state.doc.textBetween(0, editor.state.doc.content.size, '\n', '\n')
  const selectedText = selection.empty
    ? ''
    : editor.state.doc.textBetween(selection.from, selection.to, '\n', '\n')
  const textBeforeCursor = editor.state.doc.textBetween(0, selection.from, '\n', '\n')
  const lines = textBeforeCursor.split('\n')

  return {
    characterCount: countVisibleCharacters(documentText),
    selectedCharacterCount: countVisibleCharacters(selectedText),
    blockType: getCurrentBlockType(editor),
    line: lines.length,
    column: Array.from(lines.at(-1) ?? '').length + 1,
  }
}

function countVisibleCharacters(text: string): number {
  return Array.from(text).filter(character => !/\s/u.test(character)).length
}

function getCurrentBlockType(editor: Editor): EditorStatus['blockType'] {
  if (getSelectedImage(editor)) return 'image'
  if (editor.isActive('table')) return 'table'
  if (editor.isActive('taskList')) return 'taskList'
  if (editor.isActive('bulletList')) return 'bulletList'
  if (editor.isActive('orderedList')) return 'orderedList'
  if (editor.isActive('codeBlock')) return 'codeBlock'
  if (editor.isActive('blockquote')) return 'blockquote'
  for (let level = 1; level <= 6; level += 1) {
    if (editor.isActive('heading', { level })) {
      return `heading${level}` as EditorStatus['blockType']
    }
  }
  return 'paragraph'
}

export function sanitizePastedHtml(html: string): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  parsed.querySelectorAll('script, style, iframe, object, embed, svg, math, img').forEach((node) => node.remove())

  for (const element of Array.from(parsed.body.querySelectorAll('*'))) {
    for (const attribute of Array.from(element.attributes)) {
      const name = attribute.name.toLowerCase()
      if (name.startsWith('on') || name === 'style' || name === 'srcdoc') {
        element.removeAttribute(attribute.name)
      }
    }

    for (const attributeName of ['href', 'src']) {
      const value = element.getAttribute(attributeName)
      if (value && !/^(https?:|mailto:|#|\.\.?\/)/i.test(value.trim())) {
        element.removeAttribute(attributeName)
      }
    }
  }

  return parsed.body.innerHTML
}

export function executeEditorCommand(
  editor: Editor,
  command: string,
  text?: string,
  coordinates?: { left: number; top: number },
  applyToCurrentTextBlockWhenEmpty = false,
): boolean {
  const chain = editor.chain().focus()
  const commands: Record<string, () => boolean> = {
    undo: () => chain.undo().run(),
    redo: () => chain.redo().run(),
    deleteSelection: () => chain.deleteSelection().run(),
    pasteText: () => typeof text === 'string' && editor.view.pasteText(text),
    pasteHtml: () => typeof text === 'string' && editor.view.pasteHTML(text),
    toggleBold: () => toggleInlineMark(editor, 'bold', applyToCurrentTextBlockWhenEmpty),
    toggleItalic: () => toggleInlineMark(editor, 'italic', applyToCurrentTextBlockWhenEmpty),
    setLink: () => {
      if (!text || !isAllowedLink(text)) {
        return false
      }

      if (editor.state.selection.empty) {
        return chain.insertContent({
          type: 'text',
          text,
          marks: [{ type: 'link', attrs: { href: text } }],
        }).run()
      }

      return chain.extendMarkRange('link').setLink({ href: text }).run()
    },
    setParagraph: () => chain.setParagraph().run(),
    insertLineBefore: () => insertLineAroundBlock(editor, 'before'),
    insertLineAfter: () => insertLineAroundBlock(editor, 'after'),
    setHeading1: () => chain.setHeading({ level: 1 }).run(),
    setHeading2: () => chain.setHeading({ level: 2 }).run(),
    setHeading3: () => chain.setHeading({ level: 3 }).run(),
    setHeading4: () => chain.setHeading({ level: 4 }).run(),
    setHeading5: () => chain.setHeading({ level: 5 }).run(),
    setHeading6: () => chain.setHeading({ level: 6 }).run(),
    toggleUnderline: () => chain.toggleUnderline().run(),
    toggleStrike: () => chain.toggleStrike().run(),
    toggleCode: () => chain.toggleCode().run(),
    promoteHeading: () => promoteHeadingLevel(editor),
    demoteHeading: () => demoteHeadingLevel(editor),
    toggleBulletList: () => chain.toggleBulletList().run(),
    toggleOrderedList: () => chain.toggleOrderedList().run(),
    toggleTaskList: () => chain.toggleTaskList().run(),
    toggleBlockquote: () => chain.toggleBlockquote().run(),
    toggleCodeBlock: () => chain.toggleCodeBlock().run(),
    insertHorizontalRule: () => chain.setHorizontalRule().run(),
    insertTable: () => chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
    addRowBefore: () => chain.addRowBefore().run(),
    addRowAfter: () => chain.addRowAfter().run(),
    deleteRow: () => chain.deleteRow().run(),
    addColumnBefore: () => chain.addColumnBefore().run(),
    addColumnAfter: () => chain.addColumnAfter().run(),
    deleteColumn: () => chain.deleteColumn().run(),
    alignTableLeft: () => chain.setCellAttribute('align', 'left').run(),
    alignTableCenter: () => chain.setCellAttribute('align', 'center').run(),
    alignTableRight: () => chain.setCellAttribute('align', 'right').run(),
    deleteTable: () => chain.deleteTable().run(),
    insertImage: () => {
      if (!text) {
        return false
      }
      const [relativePath = '', alt = '图片'] = text.split('\n', 2)
      if (!relativePath) {
        return false
      }
      const imageChain = editor.chain().focus()
      if (coordinates) {
        const resolved = editor.view.posAtCoords(coordinates)
        if (resolved) {
          imageChain.setTextSelection(resolved.pos)
        }
      }
      return imageChain.setImage({
        src: relativePath,
        alt,
      }).run()
    },
    rotateImageClockwise: () => rotateSelectedImageClockwise(editor),
    appendText: () => {
      if (!text) {
        return false
      }
      editor.commands.setTextSelection(editor.state.doc.content.size)
      return editor.commands.insertContent(text)
    },
    scrollToHeading: () => {
      if (!text) {
        return false
      }
      const heading = Array.from(
        document.querySelectorAll<HTMLElement>('.markleaf-document h1, .markleaf-document h2, .markleaf-document h3'),
      ).find((element) => element.textContent?.trim() === text)
      heading?.scrollIntoView({ block: 'start' })
      return heading !== undefined
    },
    setBlockHighlight: () => {
      const position = Number.parseInt(text ?? '', 10)
      if (!Number.isInteger(position) || position < 0 || position > editor.state.doc.content.size) return false
      setBlockHighlight(editor, position)
      return true
    },
    clearBlockHighlight: () => {
      setBlockHighlight(editor, null)
      return true
    },
    scrollToPosition: () => {
      const position = Number.parseInt(text ?? '', 10)
      if (!Number.isInteger(position) || position < 0 || position > editor.state.doc.content.size) {
        return false
      }

      const node = editor.view.nodeDOM(position)
      const heading = node instanceof HTMLElement && /^H[1-6]$/.test(node.tagName)
        ? node
        : null
      if (!heading) {
        return false
      }

      const currentTop = document.scrollingElement?.scrollTop
        ?? document.documentElement.scrollTop
        ?? document.body.scrollTop
      const lineHeight = Number.parseFloat(window.getComputedStyle(heading).lineHeight)
      const topOffset = Number.isFinite(lineHeight) ? lineHeight / 2 : 12
      const top = Math.max(0, currentTop + heading.getBoundingClientRect().top - topOffset)
      scrollPageTo(top)
      highlightOutlineHeading(heading)
      return true
    },
    stage5Smoke: () => {
      editor.commands.setTextSelection({ from: 1, to: editor.state.doc.content.size })
      editor.commands.toggleBold()
      editor.commands.setTextSelection(editor.state.doc.content.size)
      editor.commands.setHorizontalRule()
      editor.commands.insertContent('阶段 5 撤销重做检查')
      editor.commands.undo()
      editor.commands.redo()
      return true
    },
    stage5RegressionSmoke: () => {
      editor.commands.setTextSelection(editor.state.doc.content.size)
      return executeEditorCommand(editor, 'setLink', 'https://example.com/regression')
    },
  }

  return commands[command]?.() ?? false
}

export function resetEditorViewport(editor: Editor, editorMount: HTMLElement): void {
  editor.view.dispatch(editor.state.tr.setSelection(Selection.atStart(editor.state.doc)))
  const reset = () => {
    editorMount.scrollTop = 0
    scrollPageTo(0)
  }

  reset()
  window.requestAnimationFrame(() => window.requestAnimationFrame(reset))
}

function scrollPageTo(top: number): void {
  const scrollingElement = document.scrollingElement ?? document.documentElement
  scrollingElement.scrollTop = top
  document.body.scrollTop = top
}

function highlightOutlineHeading(heading: HTMLElement): void {
  const animate = heading.animate?.bind(heading)
  if (animate) {
    for (const animation of heading.getAnimations()) {
      animation.cancel()
    }
    const hlColor = getComputedStyle(document.documentElement).getPropertyValue('--theme-light').trim() || '#E0E0E0'
    animate([
      { backgroundColor: hlColor, boxShadow: `0 0 0 4px ${hlColor}`, offset: 0 },
      { backgroundColor: hlColor, boxShadow: `0 0 0 4px ${hlColor}`, offset: 0.25 },
      { backgroundColor: 'transparent', boxShadow: '0 0 0 4px transparent', offset: 1 },
    ], { duration: 1800, easing: 'ease-out' })
    return
  }

  heading.classList.remove('markleaf-outline-highlight')
  void heading.offsetWidth
  heading.classList.add('markleaf-outline-highlight')
  window.setTimeout(() => heading.classList.remove('markleaf-outline-highlight'), 1800)
}

function toggleInlineMark(
  editor: Editor,
  mark: 'bold' | 'italic',
  applyToCurrentTextBlockWhenEmpty: boolean,
): boolean {
  const selection = editor.state.selection
  if (!applyToCurrentTextBlockWhenEmpty || !selection.empty || !selection.$from.parent.isTextblock) {
    const chain = editor.chain().focus()
    return mark === 'bold' ? chain.toggleBold().run() : chain.toggleItalic().run()
  }

  const cursor = selection.from
  const block = { from: selection.$from.start(), to: selection.$from.end() }
  const chain = editor.chain().focus().setTextSelection(block)
  if (mark === 'bold') {
    return chain.toggleBold().setTextSelection(cursor).run()
  }
  return chain.toggleItalic().setTextSelection(cursor).run()
}

function promoteHeadingLevel(editor: Editor): boolean {
  const chain = editor.chain().focus()

  // 标题：提升一级（保留行内加粗/斜体等格式）
  const levels = [1, 2, 3, 4, 5, 6] as const
  if (editor.isActive('heading', { level: 1 })) {
    return chain.setParagraph().run()
  }
  for (let i = 1; i < levels.length; i++) {
    if (editor.isActive('heading', { level: levels[i] })) {
      return chain.toggleHeading({ level: levels[i - 1]! }).run()
    }
  }

  // 非标题块：先移出列表/引用，再提升为一级标题，避免破坏列表/引用结构
  const inList = editor.isActive('bulletList') || editor.isActive('orderedList') || editor.isActive('taskList')
  if (inList) {
    // liftListItem 把当前列表项提升出列表；失败则保持原样（安全返回）
    return chain.liftListItem('listItem').toggleHeading({ level: 1 }).run()
  }
  if (editor.isActive('blockquote')) {
    return chain.lift('blockquote').toggleHeading({ level: 1 }).run()
  }
  return chain.toggleHeading({ level: 1 }).run()
}

function demoteHeadingLevel(editor: Editor): boolean {
  const chain = editor.chain().focus()

  // 标题：降低一级（保留行内格式）
  const levels = [1, 2, 3, 4, 5, 6] as const
  for (let i = 0; i < levels.length - 1; i++) {
    if (editor.isActive('heading', { level: levels[i] })) {
      return chain.toggleHeading({ level: levels[i + 1]! }).run()
    }
  }
  if (editor.isActive('heading', { level: 6 })) {
    return chain.setParagraph().run()
  }
  // 非标题（段落/列表/引用等）：“降低标题级别”不适用，保持原样
  return false
}

function insertLineAroundBlock(editor: Editor, position: 'before' | 'after'): boolean {
  const { from } = editor.state.selection
  const $from = editor.state.doc.resolve(from)
  if (!$from.parent.isTextblock) return false
  const blockStart = $from.before($from.depth)
  const blockNode = $from.node($from.depth)
  const insertPos = position === 'before' ? blockStart : blockStart + blockNode.nodeSize
  return editor.chain().focus().insertContentAt(insertPos, { type: 'paragraph' }).run()
}

export function rotateSelectedImageClockwise(editor: Editor): boolean {
  const selection = editor.state.selection
  const selectedImage = getSelectedImage(editor)
  if (!selectedImage) {
    return false
  }

  const nodeDom = editor.view.nodeDOM(selection.from) as HTMLElement | null
  const frame = nodeDom?.matches('.markleaf-image-frame')
    ? nodeDom
    : nodeDom?.querySelector<HTMLElement>('.markleaf-image-frame')
  const image = frame?.querySelector<HTMLImageElement>('img')
  const currentWidth = typeof selectedImage.attrs.width === 'number'
    ? selectedImage.attrs.width
    : frame?.offsetWidth || image?.naturalWidth || 1
  const currentHeight = typeof selectedImage.attrs.height === 'number'
    ? selectedImage.attrs.height
    : frame?.offsetHeight || image?.naturalHeight || 1
  const rotation = normalizeImageRotation(selectedImage.attrs.rotation)
  const nextRotation = ((rotation + 90) % 360) as ImageMetadata['rotation']

  editor.view.dispatch(editor.state.tr.setNodeMarkup(selection.from, undefined, {
    ...selectedImage.attrs,
    width: Math.max(1, Math.round(currentHeight)),
    height: Math.max(1, Math.round(currentWidth)),
    rotation: nextRotation,
  }))
  editor.commands.setNodeSelection(selection.from)
  return true
}

export function isAllowedLink(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === 'http:' || url.protocol === 'https:' || url.protocol === 'mailto:'
  } catch {
    return false
  }
}
