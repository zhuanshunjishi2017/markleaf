import { Editor, Extension, Node, ResizableNodeView } from '@tiptap/core'
import { Selection } from '@tiptap/pm/state'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import { DOMSerializer } from '@tiptap/pm/model'
import Image from '@tiptap/extension-image'
import Link from '@tiptap/extension-link'
import { Table, TableRow, TableHeader, TableCell, renderTableToMarkdown } from '@tiptap/extension-table'
import TaskItem from '@tiptap/extension-task-item'
import TaskList from '@tiptap/extension-task-list'
import { Markdown } from '@tiptap/markdown'
import StarterKit from '@tiptap/starter-kit'
import { MathBlock, MathInline } from './math'

const imageMetadataPrefix = 'markleaf:'
const imageMetadataSeparator = ' || '
const imageMetadataPattern = /(?:^| \|\| )markleaf:width=(\d+);height=(\d+);rotation=(0|90|180|270)(?:;caption=([^;]*))?$/
const imagePercentPattern = /(?:^| \|\| )markleaf:widthPct=(\d+);ratio=([\d.]+);rotation=(0|90|180|270)(?:;caption=([^;]*))?$/
const imageCaptionOnlyPattern = /(?:^| \|\| )markleaf:caption=([^;]*)$/

type ImageMetadata = {
  title: string | null
  width: number | null
  height: number | null
  widthPercent: number | null
  aspectRatio: number | null
  rotation: 0 | 90 | 180 | 270
  caption: string | null
}

const findHighlightKey = new PluginKey<FindHighlightState>('markleaf-find-highlight')
type TextMatch = { from: number; to: number }
type FindHighlightState = { matches: TextMatch[]; current: number }
type FootnoteDefinition = { label: string; body: string }
const EMPTY_PARAGRAPH_MARKDOWN = '&nbsp;'
const NBSP_CHAR = '\u00A0'
const FOOTNOTE_DEFINITION_SENTINEL = '\u2060'

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

/// 段落左侧浮动操作按钮：光标进入文本块时在块首渲染一个 "+" 按钮，
/// 点击后通过 DOM 自定义事件把坐标与块位置上报给宿主，由宿主弹出原生菜单。
const blockHandleKey = new PluginKey('markleaf-block-handle')

export type BlockHandleRequest = { clientX: number; clientY: number; position: number }

type BlockHandleState = { activeBlock: number | null }
type BlockHandleMeta = Partial<BlockHandleState>
let blockHandleVisible = true
let blockHandleComposing = false

let blockTypeLabels: Record<string, string> = {}

export function setBlockTypeLabels(labels: Record<string, string>): void {
  blockTypeLabels = labels
}

function getBlockTypeLabel(state: Editor['state'], from: number): string {
  const $from = state.doc.resolve(from)
  // 表格优先：只要位于表格内，无论内部是段落还是标题，都显示“表”。
  for (let depth = 1; depth <= $from.depth; depth += 1) {
    if ($from.node(depth).type.name === 'table') {
      return blockTypeLabels.blockTable ?? '表'
    }
  }
  for (let depth = $from.depth; depth >= 1; depth -= 1) {
    const node = $from.node(depth)
    const name = node.type.name
    if (isFootnoteDefinitionBlock(node)) return blockTypeLabels.blockFootnote ?? '注'
    if (name === 'heading') return blockTypeLabels[`blockHeading${node.attrs.level}`] ?? 'H'
    if (name === 'bulletList') return blockTypeLabels.blockBulletList ?? '•'
    if (name === 'orderedList') return blockTypeLabels.blockOrderedList ?? '1.'
    if (name === 'taskList') return blockTypeLabels.blockTaskList ?? '☑'
    if (name === 'blockquote') return blockTypeLabels.blockBlockquote ?? '❝'
    if (name === 'codeBlock') return blockTypeLabels.blockCodeBlock ?? '</>'
  }
  return blockTypeLabels.blockParagraph ?? '¶'
}

function isFootnoteDefinitionBlock(node: { type: { name: string }; textContent: string }): boolean {
  return node.type.name === 'paragraph' && parseFootnoteDefinitionText(node.textContent) !== null
}

function parseFootnoteDefinitionText(text: string): FootnoteDefinition | null {
  const match = new RegExp(`^\\s*${FOOTNOTE_DEFINITION_SENTINEL}?\\[\\^([^\\]\\n]+)\\]:[ \\t]*(.*)$`, 's').exec(text)
  if (!match) return null
  return {
    label: match[1]!.trim(),
    body: match[2] ?? '',
  }
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function protectFootnoteDefinitionsForVisualMarkdown(markdown: string): string {
  return markdown.replace(
    /(^|\n)( {0,3})(\[\^[^\]\n]+\]:)/g,
    (_match, lineStart: string, indent: string, marker: string) => `${lineStart}${indent}${FOOTNOTE_DEFINITION_SENTINEL}${marker}`,
  )
}

function getNodeText(node: any): string {
  if (typeof node?.textContent === 'string') return node.textContent
  if (typeof node?.text === 'string') return node.text
  const content = Array.isArray(node?.content) ? node.content : []
  return content.map(getNodeText).join('')
}

const BlockHandle = Extension.create({
  name: 'markleafBlockHandle',
  addProseMirrorPlugins() {
    return [new Plugin({
      key: blockHandleKey,
      state: {
        init: (): BlockHandleState => ({ activeBlock: null }),
        apply(transaction, previous): BlockHandleState {
          const update = transaction.getMeta(blockHandleKey) as BlockHandleMeta | undefined
          if (update) return { ...previous, ...update }
          return previous
        },
      },
      appendTransaction(transactions, _oldState, newState) {
        // 光标移出高亮段落时，自动清除段落背景高亮。
        if (!transactions.some((tr) => tr.selectionSet)) return null
        const { activeBlock } = blockHandleKey.getState(newState) ?? { activeBlock: null }
        if (activeBlock === null) return null
        const node = newState.doc.nodeAt(activeBlock)
        if (!node) return null
        const blockEnd = activeBlock + node.nodeSize
        const { from, to } = newState.selection
        if (from >= activeBlock && to <= blockEnd) return null
        return newState.tr.setMeta(blockHandleKey, { activeBlock: null } satisfies BlockHandleMeta)
      },
      props: {
        handleDOMEvents: {
          compositionstart() {
            // 仅记录标志，不在此处 dispatch：立即 dispatch 会干扰 IME 组合输入的 DOM 同步，
            // 导致首个拼音字符被额外保留。手柄会在后续正常输入事务刷新 decoration 时自然隐藏。
            blockHandleComposing = true
            return false
          },
          compositionend(view) {
            window.setTimeout(() => {
              blockHandleComposing = false
              if (!view.isDestroyed) {
                view.dispatch(view.state.tr.setMeta(blockHandleKey, {} satisfies BlockHandleMeta))
              }
            }, 0)
            return false
          },
        },
        decorations(state) {
          if (!blockHandleVisible) return DecorationSet.empty
          const { activeBlock } = blockHandleKey.getState(state) ?? { activeBlock: null }
          if (blockHandleComposing) return DecorationSet.empty
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
          if (empty && (parentName === 'paragraph' || parentName === 'heading' || parentName === 'codeBlock')) {
            // 普通块取自身位置；列表内把句柄挂到最近的列表项（listItem/taskItem）上。
            let widgetPos = $from.start()
            let nodePos = $from.before($from.depth)
            if (insideList) {
              for (let depth = $from.depth; depth >= 1; depth -= 1) {
                const name = $from.node(depth).type.name
                if (name === 'listItem' || name === 'taskItem') {
                  nodePos = $from.before(depth)
                  widgetPos = nodePos + 1
                  break
                }
              }
            }
            decorations.push(Decoration.widget(
              widgetPos,
              () => createBlockHandle(nodePos, getBlockTypeLabel(state, from), activeBlock === nodePos),
              { side: -1, ignoreSelection: true },
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
  handle.addEventListener('mousedown', (event) => {
    event.preventDefault()
    event.stopPropagation()
    const rect = handle.getBoundingClientRect()
    const detail: BlockHandleRequest = {
      clientX: rect.left,
      clientY: rect.bottom + 10,
      position: nodePos,
    }
    handle.dispatchEvent(new CustomEvent<BlockHandleRequest>('markleaf-block-handle', {
      bubbles: true,
      detail,
    }))
  })
  requestAnimationFrame(() => positionBlockHandle(handle))
  return handle
}

function positionBlockHandle(handle: HTMLButtonElement): void {
  if (!handle.isConnected) return
  const documentEl = handle.closest('.markleaf-document')
  if (!documentEl) return
  const docRect = documentEl.getBoundingClientRect()
  const handleRect = handle.getBoundingClientRect()
  handle.style.top = `${handleRect.top - docRect.top}px`
}

export function setBlockHighlight(editor: Editor, position: number | null): void {
  editor.view.dispatch(editor.state.tr.setMeta(blockHandleKey, { activeBlock: position } satisfies BlockHandleMeta))
}

export function setBlockHandleVisible(editor: Editor, visible: boolean): void {
  blockHandleVisible = visible
  editor.view.dispatch(editor.state.tr.setMeta(blockHandleKey, {} satisfies BlockHandleMeta))
}

function decodeImageCaption(value: string | undefined): string | null {
  if (!value) return null
  try { return decodeURIComponent(value) } catch { return value }
}

function parseImageMetadata(title: unknown): ImageMetadata {
  const empty: ImageMetadata = {
    title: null, width: null, height: null, widthPercent: null, aspectRatio: null, rotation: 0, caption: null,
  }
  if (typeof title !== 'string') {
    return empty
  }

  const percentMatch = imagePercentPattern.exec(title)
  if (percentMatch) {
    const metadataStart = percentMatch.index + (percentMatch[0].startsWith(imageMetadataSeparator) ? imageMetadataSeparator.length : 0)
    const ordinaryTitle = title.slice(0, percentMatch.index).trimEnd()
    return {
      title: metadataStart === 0 ? null : ordinaryTitle || null,
      width: null,
      height: null,
      widthPercent: Number(percentMatch[1]),
      aspectRatio: Number(percentMatch[2]),
      rotation: Number(percentMatch[3]) as ImageMetadata['rotation'],
      caption: decodeImageCaption(percentMatch[4]),
    }
  }

  const match = imageMetadataPattern.exec(title)
  if (!match) {
    const captionMatch = imageCaptionOnlyPattern.exec(title)
    if (captionMatch) {
      const captionStart = captionMatch.index + (captionMatch[0].startsWith(imageMetadataSeparator) ? imageMetadataSeparator.length : 0)
      const ordinaryCaptionTitle = title.slice(0, captionMatch.index).trimEnd()
      return {
        ...empty,
        title: captionStart === 0 ? null : ordinaryCaptionTitle || null,
        caption: decodeImageCaption(captionMatch[1]),
      }
    }
    return { ...empty, title }
  }

  const metadataStart = match.index + (match[0].startsWith(imageMetadataSeparator) ? imageMetadataSeparator.length : 0)
  const ordinaryTitle = title.slice(0, match.index).trimEnd()
  return {
    title: metadataStart === 0 ? null : ordinaryTitle || null,
    width: Number(match[1]),
    height: Number(match[2]),
    widthPercent: null,
    aspectRatio: null,
    rotation: Number(match[3]) as ImageMetadata['rotation'],
    caption: decodeImageCaption(match[4]),
  }
}

function serializeImageTitle(attrs: Record<string, unknown>): string | null {
  const title = typeof attrs.title === 'string' && attrs.title.length > 0 ? attrs.title : null
  const caption = typeof attrs.caption === 'string' && attrs.caption.length > 0 ? attrs.caption : null
  const captionSuffix = caption ? `;caption=${encodeURIComponent(caption)}` : ''
  const widthPercent = typeof attrs.widthPercent === 'number' && Number.isFinite(attrs.widthPercent)
    ? Math.round(attrs.widthPercent)
    : null
  const aspectRatio = typeof attrs.aspectRatio === 'number' && Number.isFinite(attrs.aspectRatio)
    ? attrs.aspectRatio
    : null
  const rotation = normalizeImageRotation(attrs.rotation)

  if (widthPercent !== null && aspectRatio !== null) {
    const metadata = `${imageMetadataPrefix}widthPct=${widthPercent};ratio=${aspectRatio.toFixed(4)};rotation=${rotation}${captionSuffix}`
    return title ? `${title}${imageMetadataSeparator}${metadata}` : metadata
  }

  const width = typeof attrs.width === 'number' && Number.isFinite(attrs.width) ? Math.round(attrs.width) : null
  const height = typeof attrs.height === 'number' && Number.isFinite(attrs.height) ? Math.round(attrs.height) : null

  if (width !== null && height !== null) {
    const metadata = `${imageMetadataPrefix}width=${width};height=${height};rotation=${rotation}${captionSuffix}`
    return title ? `${title}${imageMetadataSeparator}${metadata}` : metadata
  }

  if (caption) {
    const metadata = `${imageMetadataPrefix}caption=${encodeURIComponent(caption)}`
    return title ? `${title}${imageMetadataSeparator}${metadata}` : metadata
  }

  return title
}

function normalizeImageRotation(value: unknown): ImageMetadata['rotation'] {
  return value === 90 || value === 180 || value === 270 ? value : 0
}

function parseNullableNumber(value: string | null): number | null {
  if (value === null) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
}

function getMarkLeafImagePath(element: Element): string | null {
  const embeddedPath = element.getAttribute('data-markleaf-path')
  if (embeddedPath) {
    return embeddedPath
  }

  const src = element.getAttribute('src')?.trim()
  if (!src) {
    return null
  }

  try {
    const url = new URL(src)
    if (url.origin === 'https://assets.local' && url.pathname === '/image') {
      return url.searchParams.get('path')
    }
  } catch {
    // Relative and Windows paths are handled by data-markleaf-path, not URL parsing.
  }

  return null
}

function escapeMarkdownImageText(value: unknown): string {
  return typeof value === 'string' ? value.replace(/([\\\[\]])/g, '\\$1') : ''
}

function escapeMarkdownImageTitle(value: string): string {
  return value.replace(/([\\"])/g, '\\$1')
}

/// 动态获取当前正文内容区宽度（.markleaf-document），而非固定的最大宽度，
/// 使图片百分比在窗口缩放时随内容区宽度实时变化。
function getPageWidth(): number {
  const doc = document.querySelector<HTMLElement>('.markleaf-document')
  if (doc) {
    const rect = doc.getBoundingClientRect()
    if (rect.width > 0) return rect.width
  }
  return parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--ml-max-width')) || 820
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

function getSelectedMathMode(editor: Editor): 'inline' | 'block' | null {
  const selection = editor.state.selection
  if (!('node' in selection)) {
    return null
  }
  const node = selection.node as SelectedImageNode
  if (node.type.name === 'mathInline') return 'inline'
  if (node.type.name === 'mathBlock') return 'block'
  return null
}

const MarkLeafImage = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      src: {
        default: null,
        parseHTML: element => getMarkLeafImagePath(element) ?? element.getAttribute('src'),
        renderHTML: attributes => ({
          src: attributes.src,
        }),
      },
      rotation: {
        default: 0,
        parseHTML: element => normalizeImageRotation(Number(element.getAttribute('data-markleaf-rotation'))),
        renderHTML: attributes => ({
          'data-markleaf-rotation': normalizeImageRotation(attributes.rotation),
        }),
      },
      widthPercent: {
        default: null,
        parseHTML: element => parseNullableNumber(element.getAttribute('data-markleaf-width-percent')),
        renderHTML: attributes => ({
          'data-markleaf-width-percent': attributes.widthPercent ?? null,
        }),
      },
      aspectRatio: {
        default: null,
        parseHTML: element => parseNullableNumber(element.getAttribute('data-markleaf-aspect-ratio')),
        renderHTML: attributes => ({
          'data-markleaf-aspect-ratio': attributes.aspectRatio ?? null,
        }),
      },
      caption: {
        default: null,
        parseHTML: element => element.getAttribute('data-markleaf-caption'),
        renderHTML: attributes => ({
          'data-markleaf-caption': attributes.caption ?? null,
        }),
      },
    }
  },

  renderHTML({ node, HTMLAttributes }) {
    const attrs = node.attrs as Record<string, unknown>
    const markdownPath = typeof HTMLAttributes.src === 'string' ? HTMLAttributes.src : ''
    const widthPercent = typeof attrs.widthPercent === 'number' ? attrs.widthPercent : null
    const aspectRatio = typeof attrs.aspectRatio === 'number' ? attrs.aspectRatio : null
    const rotation = normalizeImageRotation(attrs.rotation)

    let width = typeof attrs.width === 'number' ? attrs.width : null
    let height = typeof attrs.height === 'number' ? attrs.height : null

    // 导出/复制 HTML 没有 NodeView，需在此把百分比尺寸换算成像素，
    // 否则图片会按原始尺寸渲染（导出 PDF 时图片巨大）。
    if (width === null && height === null && widthPercent !== null && aspectRatio !== null) {
      width = Math.round(getPageWidth() * widthPercent / 100)
      height = Math.round(width * aspectRatio)
    }

    const styles: string[] = ['display:block', 'margin:0.85em auto', 'max-width:100%']
    if (width !== null && height !== null) {
      const displayWidth = rotation === 90 || rotation === 270 ? height : width
      const displayHeight = rotation === 90 || rotation === 270 ? width : height
      // 用 aspect-ratio 代替固定高度：max-width 收缩时高度同步缩放，保持原始比例不变。
      styles.push(`width:${displayWidth}px`, `aspect-ratio:${displayWidth} / ${displayHeight}`)
    }
    if (rotation !== 0) {
      styles.push(`transform:rotate(${rotation}deg)`)
    }

    const caption = typeof attrs.caption === 'string' && attrs.caption.length > 0 ? attrs.caption : null
    const img = ['img', {
      ...HTMLAttributes,
      src: toVirtualImageUrl(markdownPath),
      'data-markleaf-path': markdownPath,
      style: styles.join(';'),
    }] as [string, Record<string, any>]

    if (caption) {
      return ['figure', { class: 'markleaf-figure' }, img, ['figcaption', { class: 'markleaf-figcaption' }, caption]]
    }

    return img
  },

  parseMarkdown(token, helpers) {
    const metadata = parseImageMetadata(token.title)
    return helpers.createNode('image', {
      src: token.href,
      alt: token.text,
      title: metadata.title,
      width: metadata.width,
      height: metadata.height,
      widthPercent: metadata.widthPercent,
      aspectRatio: metadata.aspectRatio,
      rotation: metadata.rotation,
      caption: metadata.caption,
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
        const widthPercent = typeof attrs.widthPercent === 'number' ? attrs.widthPercent : null
        const aspectRatio = typeof attrs.aspectRatio === 'number' ? attrs.aspectRatio : null
        let width = previewWidth ?? (typeof attrs.width === 'number' ? attrs.width : null)
        let height = previewHeight ?? (typeof attrs.height === 'number' ? attrs.height : null)

        if (previewWidth === undefined && previewHeight === undefined && widthPercent !== null && aspectRatio !== null) {
          const pageWidth = getPageWidth()
          width = Math.round(pageWidth * widthPercent / 100)
          height = Math.round(width * aspectRatio)
        }

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
          // 旋转后内容尺寸可能大于 frame，禁用 max-width:100%，避免被压缩导致上下留白。
          image.style.maxWidth = 'none'
          image.style.transform = `translate(-50%, -50%) rotate(${rotation}deg)`
        } else {
          frame.style.removeProperty('width')
          frame.style.removeProperty('height')
          image.style.position = 'static'
          image.style.removeProperty('left')
          image.style.removeProperty('top')
          image.style.width = 'auto'
          image.style.height = 'auto'
          image.style.removeProperty('max-width')
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
          const pageWidth = getPageWidth()
          const widthPercent = Math.round(width / pageWidth * 100)
          const aspectRatio = height / width
          editor.view.dispatch(editor.state.tr.setNodeMarkup(position, undefined, {
            ...currentNode.attrs,
            widthPercent,
            aspectRatio,
            width: null,
            height: null,
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

      // 监听窗口/编辑器尺寸变化，实时重算百分比尺寸的图片布局。
      // 观察 #editor（随窗口宽度连续变化），而非 max-width 封顶的 .markleaf-document。
      const container = document.getElementById('editor') ?? document.body
      let observer: ResizeObserver | null = null
      if (container) {
        observer = new ResizeObserver(() => {
          if (typeof currentNode.attrs.widthPercent === 'number') {
            applyImageLayout(currentNode.attrs)
          }
        })
        observer.observe(container)
      }

      const originalDestroy = nodeView.destroy?.bind(nodeView)
      nodeView.destroy = () => {
        observer?.disconnect()
        originalDestroy?.()
      }

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
  mathInline: boolean
  mathBlock: boolean
  mathLatex: string | null
  mathNumber: string | null
  caption: string | null
  footnoteDefinitionLabel: string | null
  canStartFormatPainter: boolean
  formatPainterArmed: boolean
}

export type EditorStatus = {
  characterCount: number
  selectedCharacterCount: number
  totalCharacterCount: number
  nonWhitespaceCharacterCount: number
  cjkCharacterCount: number
  westernWordCount: number
  formulaCount: number
  codeLineCount: number
  paragraphCount: number
  blockType: 'paragraph' | 'heading1' | 'heading2' | 'heading3' | 'heading4' | 'heading5' | 'heading6'
    | 'blockquote' | 'codeBlock' | 'bulletList' | 'orderedList' | 'taskList' | 'table' | 'image' | 'footnoteDefinition'
  line: number
  column: number
}

const MarkLeafTable = Table.extend({
  name: 'table',
  addAttributes() {
    return {
      ...this.parent?.(),
      caption: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-markleaf-caption'),
        renderHTML: (attributes: Record<string, unknown>) => ({
          'data-markleaf-caption': attributes.caption ?? null,
        }),
      },
    }
  },
  renderHTML(props: any) {
    const table = (this as any).parent?.(props)
    const caption = typeof props.node?.attrs?.caption === 'string' && props.node.attrs.caption.length > 0 ? props.node.attrs.caption : null
    if (!caption) return table
    return ['figure', { class: 'markleaf-figure' }, ['figcaption', { class: 'markleaf-figcaption' }, caption], table]
  },
  renderMarkdown(node: any, helpers: any) {
    const markdown = renderTableToMarkdown(node, helpers)
    const caption = typeof node.attrs?.caption === 'string' && node.attrs.caption.length > 0 ? node.attrs.caption : null
    return caption ? `> tablecaption: ${caption}\n\n${markdown}` : markdown
  },
})

// 加载文档后，把「> tablecaption: …」引用块合并为紧跟其后的表格的 caption 属性。
// 用 addToHistory:false 避免污染撤销历史。
function normalizeTableCaptions(editor: Editor): void {
  const { doc } = editor.state
  const tr = editor.state.tr
  let changed = false
  // 从后往前处理：删除引用块只会使其后方位置左移，倒序可保证后续（更靠前）标题的坐标不受影响。
  for (let i = doc.childCount - 1; i >= 0; i--) {
    const node = doc.child(i)
    if (node.type.name !== 'blockquote' || !node.textContent.startsWith('tablecaption:')) {
      continue
    }
    const next = doc.child(i + 1)
    if (next?.type.name !== 'table') {
      continue
    }
    let blockquotePos = 0
    for (let j = 0; j < i; j++) {
      blockquotePos += doc.child(j).nodeSize
    }
    // 把引用块段落序列化回 Markdown，再剥离前缀，保留粗体/斜体等行内格式。
    const paragraph = node.firstChild
    const markdown = paragraph
      ? ((editor as any).markdown?.serialize?.(paragraph.content.toJSON()) ?? '')
      : ''
    const caption = markdown.slice('tablecaption: '.length).trim()
    const tablePos = blockquotePos + node.nodeSize
    tr.setNodeMarkup(tablePos, undefined, { ...next.attrs, caption: caption || null })
    tr.delete(blockquotePos, tablePos)
    changed = true
  }
  if (changed) {
    editor.view.dispatch(tr.setMeta('addToHistory', false))
  }
}

function escapeCaptionHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function applyCaptionMarks(escaped: string): string {
  return escaped
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/~~([^~]+)~~/g, '<del>$1</del>')
    .replace(/(^|[^*])\*([^*]+)\*(?!\*)/g, '$1<em>$2</em>')
}

function renderCaptionHtml(caption: string): string {
  return applyCaptionMarks(escapeCaptionHtml(caption))
}

// 供导出后处理使用：getHTML() 已转义 HTML，这里只套用行内 Markdown。
export function renderEscapedCaptionHtml(escapedCaption: string): string {
  return applyCaptionMarks(escapedCaption)
}

function createCaptionElement(caption: string, kind: 'table' | 'image'): HTMLDivElement {
  const el = document.createElement('div')
  el.className = `markleaf-caption markleaf-caption-${kind}`
  el.contentEditable = 'false'
  el.innerHTML = renderCaptionHtml(caption)
  return el
}

const FootnoteReference = Node.create({
  name: 'footnoteReference',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,

  addAttributes() {
    return {
      label: {
        default: '',
        parseHTML: (element: HTMLElement) => element.getAttribute('data-footnote-ref') ?? '',
        renderHTML: (attributes: Record<string, unknown>) => ({
          'data-footnote-ref': attributes.label ?? '',
        }),
      },
    }
  },

  parseHTML() {
    return [{ tag: 'sup[data-footnote-ref]' }]
  },

  renderHTML({ node }) {
    const label = typeof node.attrs.label === 'string' ? node.attrs.label : ''
    return ['sup', { 'data-footnote-ref': label, class: 'markleaf-footnote-ref' }, `[${label}]`]
  },

  renderMarkdown(node) {
    const label = typeof node.attrs?.label === 'string' ? node.attrs.label : ''
    return `[^${label}]`
  },

  parseMarkdown(token, helpers) {
    return helpers.createNode('footnoteReference', { label: token.text ?? '' })
  },

  markdownTokenizer: {
    name: 'footnoteReference',
    level: 'inline',
    start: (src: string) => src.indexOf('[^'),
    tokenize: (src: string) => {
      const match = /^\[\^([^\]\n]+)\](?!:)/.exec(src)
      if (!match) return undefined
      return { type: 'footnoteReference', raw: match[0], text: match[1] }
    },
  },
})

const MarkLeafParagraph = Node.create({
  name: 'paragraph',
  priority: 1000,
  group: 'block',
  content: 'inline*',

  parseHTML() {
    return [{ tag: 'p' }]
  },

  renderHTML({ HTMLAttributes }: any) {
    return ['p', HTMLAttributes, 0]
  },

  parseMarkdown(token: any, helpers: any) {
    const tokens = token.tokens || []
    if (tokens.length === 1 && tokens[0].type === 'image') {
      return helpers.parseChildren([tokens[0]])
    }
    const content = helpers.parseInline(tokens)
    const explicitEmpty = tokens.length === 1
      && tokens[0].type === 'text'
      && (tokens[0].raw === EMPTY_PARAGRAPH_MARKDOWN
        || tokens[0].text === EMPTY_PARAGRAPH_MARKDOWN
        || tokens[0].raw === NBSP_CHAR
        || tokens[0].text === NBSP_CHAR)
    if (explicitEmpty && content.length === 1 && content[0].type === 'text'
      && (content[0].text === EMPTY_PARAGRAPH_MARKDOWN || content[0].text === NBSP_CHAR)) {
      return helpers.createNode('paragraph', undefined, [])
    }
    return helpers.createNode('paragraph', undefined, content)
  },

  renderMarkdown(node: any, helpers: any, context: any) {
    const text = getNodeText(node)
    const footnote = parseFootnoteDefinitionText(text)
    if (!footnote) {
      const content = Array.isArray(node.content) ? node.content : []
      if (content.length === 0) {
        const previousContent = Array.isArray(context?.previousNode?.content) ? context.previousNode.content : []
        const previousNodeIsEmptyParagraph = context?.previousNode?.type === 'paragraph' && previousContent.length === 0
        return previousNodeIsEmptyParagraph ? EMPTY_PARAGRAPH_MARKDOWN : ''
      }
      return helpers.renderChildren(content)
    }
    return `[^${footnote.label}]: ${footnote.body.trim()}`
  },

  addCommands(): any {
    return {
      setParagraph: () => ({ commands }: any) => commands.setNode(this.name),
    }
  },

  addKeyboardShortcuts() {
    return {
      'Mod-Alt-0': () => this.editor.commands.setParagraph(),
    }
  },
})

const FootnoteDefinitionDecorations = Extension.create({
  name: 'markleafFootnoteDefinitionDecorations',
  addProseMirrorPlugins() {
    return [new Plugin({
      props: {
        decorations(state) {
          const decorations: Decoration[] = []
          state.doc.descendants((node, pos) => {
            const footnote = parseFootnoteDefinitionText(node.textContent)
            if (node.type.name !== 'paragraph' || !footnote) return
            const match = new RegExp(`^\\s*${FOOTNOTE_DEFINITION_SENTINEL}?\\[\\^[^\\]\\n]+\\]:[ \\t]*`).exec(node.textContent)
            decorations.push(Decoration.node(pos, pos + node.nodeSize, { class: 'markleaf-footnote-def', 'data-footnote-label': footnote.label }))
            if (match) {
              decorations.push(Decoration.inline(pos + 1, pos + 1 + match[0].length, { class: 'markleaf-footnote-def-prefix' }))
            }
          })
          return decorations.length > 0 ? DecorationSet.create(state.doc, decorations) : DecorationSet.empty
        },
      },
    })]
  },
})

// 表格/图片标题：标题存在节点 caption 属性中，用 widget decoration 渲染。
// 表格标题在表格之上（side:-1）、图片标题在图片之下（side:1），均不参与正文流。
const Caption = Extension.create({
  name: 'markleafCaption',
  addProseMirrorPlugins() {
    return [new Plugin({
      props: {
        decorations(state) {
          const decorations: Decoration[] = []
          state.doc.descendants((node, pos) => {
            const caption = typeof node.attrs.caption === 'string' && node.attrs.caption.length > 0 ? node.attrs.caption : null
            if (!caption) return
            if (node.type.name === 'table') {
              decorations.push(Decoration.widget(pos, () => createCaptionElement(caption, 'table'), { side: -1 }))
            } else if (node.type.name === 'image') {
              decorations.push(Decoration.widget(pos + node.nodeSize, () => createCaptionElement(caption, 'image'), { side: 1 }))
            }
          })
          return decorations.length > 0
            ? DecorationSet.create(state.doc, decorations)
            : DecorationSet.empty
        },
      },
    })]
  },
})

export const editorExtensions = [
  StarterKit.configure({
    link: false,
    paragraph: false,
  }),
  MarkLeafParagraph,
  Link.configure({
    openOnClick: false,
    autolink: false,
  }),
  MarkLeafImage.configure({
    allowBase64: false,
  }),
  MarkLeafTable.configure({
    resizable: false,
  }),
  FootnoteReference,
  TableRow,
  TableHeader,
  TableCell,
  FootnoteDefinitionDecorations,
  Caption,
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
  MathInline,
  MathBlock,
]

export function createEditor(element: HTMLElement, content = '', readOnly = false): Editor {
  const editor = new Editor({
    element,
    extensions: editorExtensions,
    content: protectFootnoteDefinitionsForVisualMarkdown(content),
    contentType: 'markdown',
    autofocus: false,
    editable: !readOnly,
    editorProps: {
      attributes: {
        class: 'markleaf-document',
        spellcheck: 'true',
      },
      transformPastedHTML: sanitizePastedHtml,
    },
  })
  normalizeTableCaptions(editor)
  return editor
}

export function replaceEditorDocument(editor: Editor, element: HTMLElement, content: string, readOnly = false): Editor {
  editor.destroy()
  return createEditor(element, content, readOnly)
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
  return stabilizeUnsafeEmphasisMarkdown(editor.getMarkdown())
}

export function getVisualCursorLineNumber(editor: Editor): number {
  const textBeforeCursor = editor.state.doc.textBetween(0, editor.state.selection.from, '\n', '\n')
  return textBeforeCursor.split('\n').length
}

export type VisualSelectionSnapshot = { from: number; to: number }

export type SourceModeJumpTarget =
  | { type: 'line'; line: number }
  | { type: 'tableEnd'; tableIndex: number }
  | { type: 'afterTable'; tableIndex: number; lineOffset: number }

export function captureVisualSelection(editor: Editor): VisualSelectionSnapshot {
  const { from, to } = editor.state.selection
  return { from, to }
}

export function restoreVisualSelection(editor: Editor, selection: VisualSelectionSnapshot | null, center = false): void {
  if (!selection) {
    editor.commands.focus()
    return
  }

  const from = Math.max(0, Math.min(selection.from, editor.state.doc.content.size))
  const to = Math.max(0, Math.min(selection.to, editor.state.doc.content.size))
  editor.commands.setTextSelection({ from, to })
  editor.commands.focus()
  if (center) {
    scrollEditorPositionIntoCenter(editor, from)
  } else {
    editor.view.dispatch(editor.state.tr.scrollIntoView())
  }
}

export function scrollToFootnoteDefinition(editor: Editor, label: string): boolean {
  const normalized = label.trim()
  if (!normalized) return false

  let targetPosition: number | null = null
  editor.state.doc.descendants((node, position) => {
    const footnote = parseFootnoteDefinitionText(node.textContent)
    if (node.type.name === 'paragraph'
      && footnote
      && footnote.label.trim() === normalized) {
      targetPosition = position
      return false
    }
    return true
  })

  if (targetPosition === null) return false
  const textPosition = Math.min(targetPosition + 1, editor.state.doc.content.size)
  editor.commands.setTextSelection(textPosition)
  editor.commands.focus()
  scrollBlockPositionIntoCenter(editor, targetPosition)
  setBlockHighlight(editor, targetPosition)
  return true
}

/// 查找脚注定义正文（用于悬停提示）；找不到返回 null。
export function findFootnoteDefinitionBody(editor: Editor, label: string): string | null {
  const normalized = label.trim()
  if (!normalized) return null

  let body: string | null = null
  editor.state.doc.descendants((node) => {
    if (node.type.name !== 'paragraph') return true
    const footnote = parseFootnoteDefinitionText(node.textContent)
    if (footnote && footnote.label.trim() === normalized) {
      body = footnote.body
      return false
    }
    return true
  })
  return body
}

export function getSourceModeJumpTarget(editor: Editor): SourceModeJumpTarget {
  const tableIndex = getSelectedTableIndex(editor)
  if (tableIndex !== null) return { type: 'tableEnd', tableIndex }
  const tableAnchor = getLastTableAnchorBeforeSelection(editor)
  if (tableAnchor) return tableAnchor
  return { type: 'line', line: getVisualCursorLineNumber(editor) }
}

function scrollEditorPositionIntoCenter(editor: Editor, position: number): void {
  const coords = editor.view.coordsAtPos(Math.max(0, Math.min(position, editor.state.doc.content.size)))
  const scrollingElement = document.scrollingElement ?? document.documentElement
  const currentTop = scrollingElement.scrollTop
  const viewportCenter = window.innerHeight / 2
  const positionCenter = (coords.top + coords.bottom) / 2
  scrollPageTo(Math.max(0, currentTop + positionCenter - viewportCenter))
}

function scrollBlockPositionIntoCenter(editor: Editor, position: number): void {
  const scroll = () => {
    const node = editor.view.nodeDOM(position)
    if (node instanceof HTMLElement) {
      node.scrollIntoView({ block: 'center', inline: 'nearest' })
      centerElementInAvailableScrollContainers(node)
      return
    }
    scrollEditorPositionIntoCenter(editor, position + 1)
  }
  scroll()
  window.requestAnimationFrame(scroll)
}

function centerElementInAvailableScrollContainers(element: HTMLElement): void {
  const rect = element.getBoundingClientRect()
  const elementCenter = (rect.top + rect.bottom) / 2
  const viewportDelta = elementCenter - window.innerHeight / 2
  if (Math.abs(viewportDelta) > 1) {
    const scrollingElement = document.scrollingElement ?? document.documentElement
    const currentTop = scrollingElement.scrollTop || document.body.scrollTop || document.documentElement.scrollTop
    scrollPageTo(Math.max(0, currentTop + viewportDelta))
  }

  const editorRoot = document.getElementById('editor')
  if (!editorRoot || editorRoot.scrollHeight <= editorRoot.clientHeight) return

  const rootRect = editorRoot.getBoundingClientRect()
  const rootDelta = elementCenter - (rootRect.top + rootRect.height / 2)
  if (Math.abs(rootDelta) > 1) {
    editorRoot.scrollTop = Math.max(0, editorRoot.scrollTop + rootDelta)
  }
}

function getSelectedTableIndex(editor: Editor): number | null {
  const $from = editor.state.doc.resolve(editor.state.selection.from)
  let tablePosition: number | null = null
  for (let depth = $from.depth; depth >= 1; depth -= 1) {
    if ($from.node(depth).type.name === 'table') {
      tablePosition = $from.before(depth)
      break
    }
  }
  if (tablePosition === null) return null

  let index = 0
  let selectedIndex: number | null = null
  editor.state.doc.descendants((node, position) => {
    if (node.type.name !== 'table') return
    if (position === tablePosition) {
      selectedIndex = index
      return false
    }
    index += 1
  })
  return selectedIndex
}

function getLastTableAnchorBeforeSelection(editor: Editor): SourceModeJumpTarget | null {
  const cursor = editor.state.selection.from
  let tableIndex = 0
  let lastTableIndex = -1
  let lastTableEndPosition = 0

  editor.state.doc.forEach((node, offset) => {
    const position = offset
    const end = position + node.nodeSize
    if (cursor <= end) {
      return false
    }

    if (node.type.name === 'table') {
      lastTableIndex = tableIndex
      lastTableEndPosition = end
      tableIndex += 1
    }
    return true
  })

  if (lastTableIndex < 0) return null
  return {
    type: 'afterTable',
    tableIndex: lastTableIndex,
    lineOffset: countVisualLinesBetweenPositions(editor, lastTableEndPosition, cursor),
  }
}

function countVisualLinesBetweenPositions(editor: Editor, from: number, to: number): number {
  const start = Math.max(0, Math.min(from, editor.state.doc.content.size))
  const end = Math.max(start, Math.min(to, editor.state.doc.content.size))
  let count = 0

  editor.state.doc.nodesBetween(start, end, (node, position) => {
    if (node.type.name === 'table') return false
    if (node.type.name === 'horizontalRule') {
      count += 1
      return false
    }
    if (!node.isTextblock) return

    const fromInNode = Math.max(0, start - position - 1)
    const toInNode = Math.min(node.content.size, end - position - 1)
    if (toInNode < fromInNode) return false

    const text = node.textBetween(fromInNode, toInNode, '\n', '\n')
    if (node.type.name === 'codeBlock') {
      count += text.split('\n').length
    } else {
      count += text.split('\n').filter(line => line.trim().length > 0).length
    }
    return false
  })

  return count
}

function stabilizeUnsafeEmphasisMarkdown(markdown: string): string {
  return markdown
    .split(/(```[\s\S]*?```|~~~[\s\S]*?~~~)/g)
    .map(part => isFencedCodeBlock(part) ? part : stabilizeUnsafeEmphasisInInlineMarkdown(part))
    .join('')
}

function stabilizeUnsafeEmphasisInInlineMarkdown(markdown: string): string {
  let result = ''
  let index = 0
  while (index < markdown.length) {
    const codeSpan = readCodeSpan(markdown, index)
    if (codeSpan) {
      result += codeSpan
      index += codeSpan.length
      continue
    }

    const linkDestination = readLinkDestination(markdown, index)
    if (linkDestination) {
      result += linkDestination
      index += linkDestination.length
      continue
    }

    const strong = readPotentialEmphasis(markdown, index, '**', 'strong')
    if (strong) {
      result += strong.text
      index = strong.end
      continue
    }

    const italic = readPotentialEmphasis(markdown, index, '*', 'em')
    if (italic) {
      result += italic.text
      index = italic.end
      continue
    }

    result += markdown[index]
    index += 1
  }
  return result
}

function readLinkDestination(markdown: string, start: number): string | null {
  if (markdown[start] !== '(' || isEscaped(markdown, start)) return null
  const previous = previousCodePoint(markdown, start)
  if (previous !== ']') return null
  const end = findClosingLinkDestination(markdown, start + 1)
  return end >= 0 ? markdown.slice(start, end + 1) : null
}

function findClosingLinkDestination(markdown: string, start: number): number {
  let quote: '"' | '\'' | null = null
  let parenDepth = 0
  for (let index = start; index < markdown.length; index += 1) {
    const character = markdown[index]
    if (isEscaped(markdown, index)) continue
    if (quote) {
      if (character === quote) quote = null
      continue
    }
    if (character === '"' || character === '\'') {
      quote = character
      continue
    }
    if (character === '(') {
      parenDepth += 1
      continue
    }
    if (character === ')') {
      if (parenDepth === 0) return index
      parenDepth -= 1
    }
  }
  return -1
}

function isFencedCodeBlock(markdown: string): boolean {
  return /^(```|~~~)/.test(markdown)
}

function readCodeSpan(markdown: string, start: number): string | null {
  if (markdown[start] !== '`' || isEscaped(markdown, start)) return null
  let tickCount = 1
  while (markdown[start + tickCount] === '`') tickCount += 1
  const fence = '`'.repeat(tickCount)
  const end = markdown.indexOf(fence, start + tickCount)
  return end >= 0 ? markdown.slice(start, end + tickCount) : null
}

function readPotentialEmphasis(
  markdown: string,
  start: number,
  marker: '*' | '**',
  tag: 'em' | 'strong',
): { text: string; end: number } | null {
  if (!markdown.startsWith(marker, start) || isEscaped(markdown, start)) return null
  if (marker === '*' && markdown.startsWith('**', start)) return null
  const contentStart = start + marker.length
  const close = findClosingEmphasisMarker(markdown, contentStart, marker)
  if (close < 0) return null

  const end = close + marker.length
  const content = markdown.slice(contentStart, close)
  const opening = getDelimiterRun(markdown, start, marker.length)
  const closing = getDelimiterRun(markdown, close, marker.length)
  if (canOpenEmphasis(opening) && canCloseEmphasis(closing)) {
    return { text: markdown.slice(start, end), end }
  }

  return { text: `<${tag}>${markdownInlineToHtmlText(content)}</${tag}>`, end }
}

function findClosingEmphasisMarker(markdown: string, start: number, marker: '*' | '**'): number {
  let index = start
  while (index < markdown.length) {
    const codeSpan = readCodeSpan(markdown, index)
    if (codeSpan) {
      index += codeSpan.length
      continue
    }
    if (markdown.startsWith(marker, index) && !isEscaped(markdown, index)) {
      if (marker === '*' && markdown.startsWith('**', index)) {
        index += 2
        continue
      }
      return index
    }
    index += 1
  }
  return -1
}

type DelimiterRun = {
  before: string | null
  after: string | null
  leftFlanking: boolean
  rightFlanking: boolean
}

function getDelimiterRun(markdown: string, markerStart: number, markerLength: number): DelimiterRun {
  const before = previousCodePoint(markdown, markerStart)
  const after = nextCodePoint(markdown, markerStart + markerLength)
  const beforeWhitespace = before === null || /\s/u.test(before)
  const afterWhitespace = after === null || /\s/u.test(after)
  const beforePunctuation = before !== null && isUnicodePunctuation(before)
  const afterPunctuation = after !== null && isUnicodePunctuation(after)
  const leftFlanking = !afterWhitespace && (!afterPunctuation || beforeWhitespace || beforePunctuation)
  const rightFlanking = !beforeWhitespace && (!beforePunctuation || afterWhitespace || afterPunctuation)
  return { before, after, leftFlanking, rightFlanking }
}

function canOpenEmphasis(run: DelimiterRun): boolean {
  return run.leftFlanking && (!run.rightFlanking || !isUnicodePunctuation(run.before))
}

function canCloseEmphasis(run: DelimiterRun): boolean {
  return run.rightFlanking && (!run.leftFlanking || !isUnicodePunctuation(run.after))
}

function isUnicodePunctuation(character: string | null): boolean {
  return character !== null && /\p{P}/u.test(character)
}

function previousCodePoint(text: string, index: number): string | null {
  if (index <= 0) return null
  return Array.from(text.slice(0, index)).at(-1) ?? null
}

function nextCodePoint(text: string, index: number): string | null {
  if (index >= text.length) return null
  return Array.from(text.slice(index))[0] ?? null
}

function isEscaped(text: string, index: number): boolean {
  let slashCount = 0
  for (let cursor = index - 1; cursor >= 0 && text[cursor] === '\\'; cursor -= 1) {
    slashCount += 1
  }
  return slashCount % 2 === 1
}

function markdownInlineToHtmlText(markdown: string): string {
  return markdown
    .replace(/\\([\\`*_[\]{}()#+\-.!<>|])/g, '$1')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
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
  const mathMode = getSelectedMathMode(editor)
  const selectedMath = getSelectedMath(editor)
  const mathNumber = selectedMath && selectedMath.node.type.name === 'mathBlock'
    ? (typeof selectedMath.node.attrs.number === 'string' && selectedMath.node.attrs.number.length > 0 ? selectedMath.node.attrs.number : null)
    : null
  const selectedImage = getSelectedImage(editor)
  const footnoteDefinition = parseFootnoteDefinitionText(editor.state.selection.$from.parent.textContent)
  const caption = selectedImage
    ? (typeof selectedImage.attrs.caption === 'string' && selectedImage.attrs.caption.length > 0 ? selectedImage.attrs.caption : null)
    : (() => {
        const table = getTableAtSelection(editor)
        return table && typeof table.node.attrs.caption === 'string' && table.node.attrs.caption.length > 0 ? table.node.attrs.caption : null
      })()

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
    mathInline: mathMode === 'inline',
    mathBlock: mathMode === 'block',
    mathLatex: selectedMath?.node.textContent ?? null,
    mathNumber,
    caption,
    footnoteDefinitionLabel: footnoteDefinition?.label ?? null,
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
    ...getDocumentStatistics(editor, documentText),
    blockType: getCurrentBlockType(editor),
    line: lines.length,
    column: Array.from(lines.at(-1) ?? '').length + 1,
  }
}

function countVisibleCharacters(text: string): number {
  return Array.from(text).filter(character => !/\s/u.test(character)).length
}

function getDocumentStatistics(editor: Editor, documentText: string) {
  let formulaCount = 0
  let codeLineCount = 0
  let paragraphCount = 0

  editor.state.doc.descendants((node) => {
    if (node.type.name === 'mathInline' || node.type.name === 'mathBlock') {
      formulaCount += 1
    }
    if (node.type.name === 'codeBlock') {
      codeLineCount += Math.max(1, node.textContent.split('\n').length)
    }
    if (node.type.name === 'paragraph') {
      paragraphCount += 1
    }
  })

  return {
    totalCharacterCount: Array.from(documentText).length,
    nonWhitespaceCharacterCount: countVisibleCharacters(documentText),
    cjkCharacterCount: countCjkCharacters(documentText),
    westernWordCount: countWesternWords(documentText),
    formulaCount,
    codeLineCount,
    paragraphCount,
  }
}

function countCjkCharacters(text: string): number {
  return Array.from(text.matchAll(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]/gu)).length
}

function countWesternWords(text: string): number {
  return Array.from(text.matchAll(/[\p{Script=Latin}][\p{Script=Latin}\p{Mark}'’-]*/gu)).length
}

function parseTableSize(text?: string): { rows: number; cols: number } {
  const parts = text?.split(',') ?? []
  if (parts.length !== 2) return { rows: 3, cols: 3 }
  const rows = Number(parts[0])
  const cols = Number(parts[1])
  if (!Number.isInteger(rows) || !Number.isInteger(cols) || rows < 1 || cols < 1 || rows > 100 || cols > 100) {
    return { rows: 3, cols: 3 }
  }
  return { rows, cols }
}

function getCurrentBlockType(editor: Editor): EditorStatus['blockType'] {
  if (getSelectedImage(editor)) return 'image'
  if (isFootnoteDefinitionBlock(editor.state.selection.$from.parent)) return 'footnoteDefinition'
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
  parsed.querySelectorAll('script, style, iframe, object, embed, svg, math').forEach((node) => node.remove())

  for (const figure of Array.from(parsed.body.querySelectorAll('figure.markleaf-figure'))) {
    const image = figure.querySelector('img')
    if (!image) continue

    const caption = figure.querySelector('figcaption')?.textContent?.trim()
    if (caption && !image.getAttribute('data-markleaf-caption')) {
      image.setAttribute('data-markleaf-caption', caption)
    }
    figure.replaceWith(image)
  }

  for (const image of Array.from(parsed.body.querySelectorAll('img'))) {
    const markdownPath = getMarkLeafImagePath(image)
    if (!markdownPath) {
      image.remove()
      continue
    }

    image.setAttribute('src', markdownPath)
    image.setAttribute('data-markleaf-path', markdownPath)
  }

  for (const element of Array.from(parsed.body.querySelectorAll('*'))) {
    for (const attribute of Array.from(element.attributes)) {
      const name = attribute.name.toLowerCase()
      if (name.startsWith('on') || name === 'style' || name === 'srcdoc') {
        element.removeAttribute(attribute.name)
      }
    }

    for (const attributeName of ['href', 'src']) {
      const value = element.getAttribute(attributeName)
      if (element.tagName.toLowerCase() === 'img' && attributeName === 'src' && getMarkLeafImagePath(element)) {
        continue
      }
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
    clearFormat: () => clearParagraphFormat(editor),
    insertLineBefore: () => insertLineAroundBlock(editor, 'before'),
    insertLineAfter: () => insertLineAroundBlock(editor, 'after'),
    insertMathInline: () => insertMath(editor, 'inline', text),
    insertMathBlock: () => insertMath(editor, 'block', text),
    insertFootnote: () => insertFootnote(editor, text),
    resetFootnoteLabel: () => resetFootnoteLabel(editor, text),
    updateMath: () => updateMath(editor, text),
    setMathNumber: () => changeMathNumber(editor, text),
    convertMath: () => convertMath(editor),
    deleteMath: () => deleteMath(editor),
    selectAll: () => editor.commands.selectAll(),
    exitCode: () => exitCodeBlock(editor),
    setHeading1: () => chain.setHeading({ level: 1 }).run(),
    setHeading2: () => chain.setHeading({ level: 2 }).run(),
    setHeading3: () => chain.setHeading({ level: 3 }).run(),
    setHeading4: () => chain.setHeading({ level: 4 }).run(),
    setHeading5: () => chain.setHeading({ level: 5 }).run(),
    setHeading6: () => chain.setHeading({ level: 6 }).run(),
    toggleUnderline: () => toggleInlineMark(editor, 'underline', applyToCurrentTextBlockWhenEmpty),
    toggleStrike: () => toggleInlineMark(editor, 'strike', applyToCurrentTextBlockWhenEmpty),
    toggleCode: () => chain.toggleCode().run(),
    promoteHeading: () => promoteHeadingLevel(editor),
    demoteHeading: () => demoteHeadingLevel(editor),
    toggleBulletList: () => chain.toggleBulletList().run(),
    toggleOrderedList: () => chain.toggleOrderedList().run(),
    toggleTaskList: () => chain.toggleTaskList().run(),
    toggleBlockquote: () => chain.toggleBlockquote().run(),
    toggleCodeBlock: () => chain.toggleCodeBlock().run(),
    insertHorizontalRule: () => chain.setHorizontalRule().run(),
    insertTable: () => {
      const size = parseTableSize(text)
      return chain.insertTable({ rows: size.rows, cols: size.cols, withHeaderRow: true }).run()
    },
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
    resizeImage: () => resizeImageToPercent(editor, Number(text)),
    changeImage: () => changeImageSource(editor, text),
    setImageCaption: () => changeImageCaption(editor, text),
    setTableCaption: () => changeTableCaption(editor, text),
    appendText: () => {
      if (!text) {
        return false
      }
      editor.commands.setTextSelection(editor.state.doc.content.size)
      return editor.commands.insertContent(text)
    },
    clearBlockHighlight: () => {
      setBlockHighlight(editor, null)
      return true
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
  mark: 'bold' | 'italic' | 'underline' | 'strike',
  applyToCurrentTextBlockWhenEmpty: boolean,
): boolean {
  const selection = editor.state.selection
  if (!applyToCurrentTextBlockWhenEmpty || !selection.empty || !selection.$from.parent.isTextblock) {
    const chain = editor.chain().focus()
    if (mark === 'bold') return chain.toggleBold().run()
    if (mark === 'italic') return chain.toggleItalic().run()
    if (mark === 'underline') return chain.toggleUnderline().run()
    return chain.toggleStrike().run()
  }

  const cursor = selection.from
  const block = { from: selection.$from.start(), to: selection.$from.end() }
  const chain = editor.chain().focus().setTextSelection(block)
  if (mark === 'bold') return chain.toggleBold().setTextSelection(cursor).run()
  if (mark === 'italic') return chain.toggleItalic().setTextSelection(cursor).run()
  if (mark === 'underline') return chain.toggleUnderline().setTextSelection(cursor).run()
  return chain.toggleStrike().setTextSelection(cursor).run()
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

function insertMath(editor: Editor, mode: 'inline' | 'block', text?: string): boolean {
  const nodeType = mode === 'inline' ? 'mathInline' : 'mathBlock'
  const { from, to, empty } = editor.state.selection
  const chain = editor.chain().focus()

  // 有选区：直接用选区文本套 $...$ / $$...$$
  if (!empty) {
    const selected = editor.state.doc.textBetween(from, to)
    return chain.insertContentAt({ from, to }, {
      type: nodeType,
      content: [{ type: 'text', text: selected }],
    }).run()
  }

  // 无选区：插入传入的 LaTeX 文本
  const latex = (text ?? '').trim()
  if (!latex) return false
  return chain.insertContent({
    type: nodeType,
    content: [{ type: 'text', text: latex }],
  }).run()
}

function insertFootnote(editor: Editor, text?: string): boolean {
  let label = ''
  let note = ''
  try {
    const payload = JSON.parse(text ?? '{}') as { label?: unknown; note?: unknown }
    label = typeof payload.label === 'string' ? payload.label.trim() : ''
    note = typeof payload.note === 'string' ? payload.note.trim() : ''
  } catch {
    return false
  }
  if (!label || !note) return false

  const insertReference = editor.chain().focus().insertContent({
    type: 'footnoteReference',
    attrs: { label },
  }).run()
  if (!insertReference) return false

  const docEnd = editor.state.doc.content.size
  return editor.commands.insertContentAt(docEnd, [
    {
      type: 'paragraph',
      content: [{ type: 'text', text: `${FOOTNOTE_DEFINITION_SENTINEL}[^${label}]: ${note}` }],
    },
  ])
}

function resetFootnoteLabel(editor: Editor, text?: string): boolean {
  let oldLabel = ''
  let newLabel = ''
  try {
    const payload = JSON.parse(text ?? '{}') as { oldLabel?: unknown; newLabel?: unknown }
    oldLabel = typeof payload.oldLabel === 'string' ? payload.oldLabel.trim() : ''
    newLabel = typeof payload.newLabel === 'string' ? payload.newLabel.trim() : ''
  } catch {
    return false
  }
  if (!oldLabel || !newLabel || oldLabel === newLabel) return false

  const escaped = escapeRegExp(oldLabel)
  const marker = new RegExp(`\\[\\^${escaped}\\](?=:)`, 'g')
  const reference = new RegExp(`\\[\\^${escaped}\\](?!:)`, 'g')
  const transactions: Array<{ from: number; to: number; text: string }> = []

  editor.state.doc.descendants((node, position) => {
    if (node.type.name === 'footnoteReference') {
      if (node.attrs.label === oldLabel) {
        transactions.push({ from: position, to: position + node.nodeSize, text: '' })
      }
      return false
    }
    if (!node.isText || !node.text) return

    for (const match of node.text.matchAll(marker)) {
      transactions.push({
        from: position + (match.index ?? 0),
        to: position + (match.index ?? 0) + match[0].length,
        text: `[^${newLabel}]`,
      })
    }
    for (const match of node.text.matchAll(reference)) {
      transactions.push({
        from: position + (match.index ?? 0),
        to: position + (match.index ?? 0) + match[0].length,
        text: `[^${newLabel}]`,
      })
    }
  })

  let tr = editor.state.tr
  for (const change of transactions.sort((a, b) => b.from - a.from)) {
    if (change.text) {
      tr = tr.insertText(change.text, change.from, change.to)
    } else {
      tr = tr.setNodeMarkup(change.from, undefined, { label: newLabel })
    }
  }
  if (!tr.docChanged) return false
  editor.view.dispatch(tr.scrollIntoView())
  return true
}

type SelectedMathNode = { type: { name: string }; textContent: string; attrs: Record<string, unknown> }

function getSelectedMath(editor: Editor): { node: SelectedMathNode; from: number; to: number } | null {
  const selection = editor.state.selection
  if (!('node' in selection)) return null
  const node = selection.node as unknown as SelectedMathNode
  if (node.type.name !== 'mathInline' && node.type.name !== 'mathBlock') return null
  return { node, from: selection.from, to: selection.to }
}

function changeMathNumber(editor: Editor, number?: string): boolean {
  const selected = getSelectedMath(editor)
  if (!selected || selected.node.type.name !== 'mathBlock') return false
  const value = (number ?? '').trim()
  editor.view.dispatch(editor.state.tr.setNodeMarkup(selected.from, undefined, {
    ...selected.node.attrs,
    number: value.length > 0 ? value : null,
  }))
  return true
}

function updateMath(editor: Editor, text?: string): boolean {
  const latex = (text ?? '').trim()
  const selected = getSelectedMath(editor)
  if (!selected || !latex) return false
  return editor.chain().focus().insertContentAt(
    { from: selected.from, to: selected.to },
    { type: selected.node.type.name, content: [{ type: 'text', text: latex }] },
  ).run()
}

function convertMath(editor: Editor): boolean {
  const selected = getSelectedMath(editor)
  if (!selected) return false
  const latex = selected.node.textContent
  const targetType = selected.node.type.name === 'mathInline' ? 'mathBlock' : 'mathInline'
  return editor.chain().focus().insertContentAt(
    { from: selected.from, to: selected.to },
    { type: targetType, content: [{ type: 'text', text: latex }] },
  ).run()
}

function deleteMath(editor: Editor): boolean {
  if (!getSelectedMath(editor)) return false
  return editor.chain().focus().deleteSelection().run()
}

function exitCodeBlock(editor: Editor): boolean {
  if (!editor.isActive('codeBlock')) return false
  return editor.chain().focus().toggleCodeBlock().run()
}

function clearParagraphFormat(editor: Editor): boolean {
  const { state } = editor
  const $from = state.doc.resolve(state.selection.from)
  const blockFrom = $from.start()
  const blockTo = $from.end()

  // 行内公式是内联节点而非标记，unsetAllMarks 无法清除，需先替换为纯文本。
  const tr = state.tr
  state.doc.nodesBetween(blockFrom, blockTo, (node, pos) => {
    if (node.type.name === 'mathInline') {
      tr.replaceWith(pos, pos + node.nodeSize, state.schema.text(node.textContent))
    }
  })
  if (tr.docChanged) {
    editor.view.dispatch(tr)
  }

  // unsetAllMarks 在空选区下是 no-op，因此先选中整段，再清除块结构与所有标记。
  const chain = editor.chain().focus()
  const $current = editor.state.doc.resolve(editor.state.selection.from)
  chain.setTextSelection({ from: $current.start(), to: $current.end() })
  return chain.clearNodes().unsetAllMarks({ ignoreClearable: true }).run()
}

export function rotateSelectedImageClockwise(editor: Editor): boolean {
  const selection = editor.state.selection
  const selectedImage = getSelectedImage(editor)
  if (!selectedImage) {
    return false
  }

  const rotation = normalizeImageRotation(selectedImage.attrs.rotation)
  const nextRotation = ((rotation + 90) % 360) as ImageMetadata['rotation']

  // 统一为「百分比宽度 + 宽高比」：widthPercent 是设定宽度，旋转不改变它，
  // 仅宽高比取倒数（宽高互换），保证旋转后图片宽度仍为设定宽度。
  let widthPercent = typeof selectedImage.attrs.widthPercent === 'number' ? selectedImage.attrs.widthPercent : null
  let aspectRatio = typeof selectedImage.attrs.aspectRatio === 'number' ? selectedImage.attrs.aspectRatio : null

  if (widthPercent === null || aspectRatio === null) {
    // 像素尺寸（或尚未有尺寸信息）：换算成百分比，统一后续处理。
    const nodeDom = editor.view.nodeDOM(selection.from) as HTMLElement | null
    const frame = nodeDom?.matches('.markleaf-image-frame')
      ? nodeDom
      : nodeDom?.querySelector<HTMLElement>('.markleaf-image-frame')
    const image = frame?.querySelector<HTMLImageElement>('img')
    const currentWidth = typeof selectedImage.attrs.width === 'number'
      ? selectedImage.attrs.width
      : frame?.offsetWidth || image?.naturalWidth || null
    const currentHeight = typeof selectedImage.attrs.height === 'number'
      ? selectedImage.attrs.height
      : frame?.offsetHeight || image?.naturalHeight || null
    if (currentWidth !== null && currentHeight !== null && currentWidth > 0) {
      widthPercent = Math.round(currentWidth / getPageWidth() * 100)
      aspectRatio = currentHeight / currentWidth
    }
  }

  if (widthPercent === null || aspectRatio === null) {
    return false
  }

  editor.view.dispatch(editor.state.tr.setNodeMarkup(selection.from, undefined, {
    ...selectedImage.attrs,
    widthPercent,
    aspectRatio: 1 / aspectRatio,
    width: null,
    height: null,
    rotation: nextRotation,
  }))

  editor.commands.setNodeSelection(selection.from)
  return true
}

function resizeImageToPercent(editor: Editor, percent: number): boolean {
  const selection = editor.state.selection
  const selectedImage = getSelectedImage(editor)
  if (!selectedImage || !Number.isFinite(percent) || percent <= 0) {
    return false
  }

  const nodeDom = editor.view.nodeDOM(selection.from) as HTMLElement | null
  const frame = nodeDom?.matches('.markleaf-image-frame')
    ? nodeDom
    : nodeDom?.querySelector<HTMLElement>('.markleaf-image-frame')
  const image = frame?.querySelector<HTMLImageElement>('img')

  let aspectRatio = typeof selectedImage.attrs.aspectRatio === 'number'
    ? selectedImage.attrs.aspectRatio
    : null
  if (aspectRatio === null) {
    const currentWidth = typeof selectedImage.attrs.width === 'number'
      ? selectedImage.attrs.width
      : image?.naturalWidth ?? null
    const currentHeight = typeof selectedImage.attrs.height === 'number'
      ? selectedImage.attrs.height
      : image?.naturalHeight ?? null
    aspectRatio = currentWidth !== null && currentHeight !== null && currentWidth > 0
      ? currentHeight / currentWidth
      : null
  }
  if (aspectRatio === null) {
    return false
  }

  editor.view.dispatch(editor.state.tr.setNodeMarkup(selection.from, undefined, {
    ...selectedImage.attrs,
    widthPercent: Math.round(percent),
    aspectRatio,
    width: null,
    height: null,
  }))
  editor.commands.setNodeSelection(selection.from)
  return true
}

function changeImageSource(editor: Editor, src?: string): boolean {
  const selection = editor.state.selection
  const selectedImage = getSelectedImage(editor)
  if (!selectedImage || !src) {
    return false
  }
  editor.view.dispatch(editor.state.tr.setNodeMarkup(selection.from, undefined, {
    ...selectedImage.attrs,
    src,
  }))
  return true
}

function changeImageCaption(editor: Editor, caption?: string): boolean {
  const selection = editor.state.selection
  const selectedImage = getSelectedImage(editor)
  if (!selectedImage) {
    return false
  }
  const value = (caption ?? '').trim()
  editor.view.dispatch(editor.state.tr.setNodeMarkup(selection.from, undefined, {
    ...selectedImage.attrs,
    caption: value.length > 0 ? value : null,
  }))
  return true
}

type SelectedTableNode = { type: { name: string }; attrs: Record<string, unknown> }

function getTableAtSelection(editor: Editor): { node: SelectedTableNode; pos: number } | null {
  const { $from } = editor.state.selection
  for (let depth = $from.depth; depth >= 1; depth--) {
    if ($from.node(depth).type.name === 'table') {
      return { node: $from.node(depth) as SelectedTableNode, pos: $from.before(depth) }
    }
  }
  return null
}

function changeTableCaption(editor: Editor, caption?: string): boolean {
  const table = getTableAtSelection(editor)
  if (!table) {
    return false
  }
  const value = (caption ?? '').trim()
  editor.view.dispatch(editor.state.tr.setNodeMarkup(table.pos, undefined, {
    ...table.node.attrs,
    caption: value.length > 0 ? value : null,
  }))
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
