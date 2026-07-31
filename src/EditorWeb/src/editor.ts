import { Editor, ResizableNodeView } from '@tiptap/core'
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
  link: boolean
  blockquote: boolean
  codeBlock: boolean
  bulletList: boolean
  orderedList: boolean
  taskList: boolean
  inTable: boolean
  tableAlign: 'left' | 'center' | 'right' | null
  imageSelected: boolean
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
]

export function createEditor(element: HTMLElement, content = ''): Editor {
  return new Editor({
    element,
    extensions: editorExtensions,
    content,
    contentType: 'markdown',
    autofocus: 'end',
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
    link: editor.isActive('link'),
    blockquote: editor.isActive('blockquote'),
    codeBlock: editor.isActive('codeBlock'),
    bulletList: editor.isActive('bulletList'),
    orderedList: editor.isActive('orderedList'),
    taskList: editor.isActive('taskList'),
    inTable,
    tableAlign,
    imageSelected: getSelectedImage(editor) !== null,
  }
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
): boolean {
  const chain = editor.chain().focus()
  const commands: Record<string, () => boolean> = {
    undo: () => chain.undo().run(),
    redo: () => chain.redo().run(),
    deleteSelection: () => chain.deleteSelection().run(),
    pasteText: () => typeof text === 'string' && editor.view.pasteText(text),
    toggleBold: () => chain.toggleBold().run(),
    toggleItalic: () => chain.toggleItalic().run(),
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
    setHeading1: () => chain.setHeading({ level: 1 }).run(),
    setHeading2: () => chain.setHeading({ level: 2 }).run(),
    setHeading3: () => chain.setHeading({ level: 3 }).run(),
    setHeading4: () => chain.setHeading({ level: 4 }).run(),
    setHeading5: () => chain.setHeading({ level: 5 }).run(),
    setHeading6: () => chain.setHeading({ level: 6 }).run(),
    toggleStrike: () => chain.toggleStrike().run(),
    toggleCode: () => chain.toggleCode().run(),
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
