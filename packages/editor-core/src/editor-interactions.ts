import { bindDocumentPointerInteractions } from './document-pointer'
import { bindDocumentLinks, type DocumentLinkActions } from './document-links'
import type { Editor } from '@tiptap/core'
import { createBlockHandle } from './block-handle'
import { captureFormat, FormatPainterController, executeFormatPainterApply } from './format-painter'
import { applyFormatPainterFromDomSelection } from './format-painter-dom-events'
import { setBlockHighlight, hasExpandedSourceEditor } from './editor'

export function createEditorInteractions(options: {
  mount: HTMLElement
  getEditor(): Editor
  enabled(): boolean
  onMenu(position: number, rect: DOMRect): void
  label: string
  onStateChanged?(): void
  links?: DocumentLinkActions
}) {
  const { mount, getEditor } = options
  const painter = new FormatPainterController()
  const events = new AbortController()
  let composing = false
  let timer: ReturnType<typeof setTimeout> | undefined
  const enabled = () => options.enabled() && getEditor().isEditable && !composing
  const handle = createBlockHandle(mount, getEditor, enabled, options.onMenu, options.label)
  function update(): void {
    if (!enabled()) painter.cancel()
    mount.classList.toggle('format-painter-armed', painter.isArmed)
    handle.update()
  }
  function changed(): void { update(); options.onStateChanged?.() }
  const unbindPointer = bindDocumentPointerInteractions(mount, getEditor, changed)
  const unbindLinks = options.links ? bindDocumentLinks(mount, getEditor, options.links) : undefined
  function cancel(): void { painter.cancel(); changed() }
  function arm(): boolean { if (!enabled()) return false; const result = painter.arm(getEditor()); changed(); return result }
  function apply(): boolean { const result = enabled() && executeFormatPainterApply(getEditor(), painter, false); changed(); return result }
  window.addEventListener('mouseup', () => {
    clearTimeout(timer)
    timer = setTimeout(() => {
      if (!enabled() || !painter.isArmed) return
      applyFormatPainterFromDomSelection(getEditor(), painter, window.getSelection())
      changed()
    }, 0)
  }, { signal: events.signal })
  window.addEventListener('keydown', event => {
    if (event.key === 'Escape' && painter.isArmed) { event.preventDefault(); cancel() }
  }, { signal: events.signal })
  mount.addEventListener('compositionstart', () => { composing = true; cancel() }, { signal: events.signal })
  mount.addEventListener('compositionend', () => { composing = false; update() }, { signal: events.signal })
  document.addEventListener('selectionchange', () => { if (hasExpandedSourceEditor(getEditor())) options.onStateChanged?.() }, { signal: events.signal })
  window.addEventListener('resize', update, { signal: events.signal })
  window.addEventListener('scroll', update, { signal: events.signal, capture: true, passive: true })
  for (const event of ['mousemove', 'mouseenter', 'focusin', 'click']) mount.addEventListener(event, update, { signal: events.signal })
  return {
    update, cancel, arm, apply,
    button: handle.button, ensure: handle.ensure, hide: handle.hide,
    clearHighlight: () => setBlockHighlight(getEditor(), null),
    state: () => ({ canStartFormatPainter: enabled() && captureFormat(getEditor()) !== null, formatPainterArmed: enabled() && painter.isArmed }),
    togglePainter(): boolean { if (!enabled()) return false; if (painter.isArmed) { cancel(); return true }; return arm() },
    dispose(): void { unbindPointer(); unbindLinks?.(); clearTimeout(timer); events.abort(); painter.cancel(); mount.classList.remove('format-painter-armed'); handle.dispose() },
  }
}
