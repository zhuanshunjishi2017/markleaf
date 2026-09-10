import type { Editor } from '@tiptap/core'
import { createBlockHandle } from './block-handle'
import { setBlockHighlight } from './editor'
import { FormatPainterController, captureFormat } from './format-painter'
import { applyFormatPainterFromDomSelection } from './format-painter-dom-events'

export function createEditorInteractions(editor: Editor, mount: HTMLElement, menu: () => void) {
  const painter = new FormatPainterController()
  let composing = false
  let timer: ReturnType<typeof setTimeout> | undefined
  const events = new AbortController()
  const handle = createBlockHandle(mount, () => editor, () => editor.isEditable && !composing,
    menu, '当前段落操作')
  function update(): void {
    if (!editor.isEditable) painter.cancel()
    mount.classList.toggle('format-painter-armed', painter.isArmed)
    document.querySelector('[data-command="formatPainter"]')?.setAttribute('aria-pressed', String(painter.isArmed))
    handle.update()
  }
  function cancel(): void { painter.cancel(); update() }
  function paint(): void {
    if (!editor.isEditable || composing || !painter.isArmed) return
    applyFormatPainterFromDomSelection(editor, painter, window.getSelection())
    update()
  }
  // ProseMirror reads the final browser selection after mouseup. Also handle
  // drags released over editor padding, outside the contenteditable element.
  window.addEventListener('mouseup', () => {
    clearTimeout(timer)
    timer = setTimeout(paint, 0)
  }, { signal: events.signal })
  window.addEventListener('keydown', event => {
    if (event.key === 'Escape' && painter.isArmed) { event.preventDefault(); cancel() }
  }, { signal: events.signal })
  mount.addEventListener('compositionstart', () => { composing = true; cancel() }, { signal: events.signal })
  mount.addEventListener('compositionend', () => { composing = false; update() }, { signal: events.signal })
  window.addEventListener('resize', update, { signal: events.signal })
  window.addEventListener('scroll', update, { signal: events.signal, capture: true, passive: true })
  editor.on('transaction', update)
  update()
  return {
    update, cancel,
    clearHighlight: () => setBlockHighlight(editor, null),
    state: () => ({ canStartFormatPainter: captureFormat(editor) !== null, formatPainterArmed: painter.isArmed }),
    togglePainter(): boolean {
      if (!editor.isEditable) return false
      if (painter.isArmed) cancel()
      else { if (!painter.arm(editor)) return false; update() }
      return true
    },
    dispose(): void {
      clearTimeout(timer)
      events.abort()
      editor.off('transaction', update)
      handle.dispose()
    },
  }
}
