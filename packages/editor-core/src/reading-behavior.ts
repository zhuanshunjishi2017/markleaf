import type { Editor } from '@tiptap/core'
import { createScrollbarAlphaController, bindReducedMotionPreference } from './scrollbar-motion'

/** Document scrolling policy. Hosts supply only their occupied viewport inset. */
export function createReadingBehavior(getEditor: () => Editor | undefined, topInset: () => number = () => 0) {
  let typewriter = false
  let autoHide = false
  let frame = 0
  let hideTimer: ReturnType<typeof setTimeout> | undefined
  const events = new AbortController()
  const unbindMotion = bindReducedMotionPreference(window.matchMedia('(prefers-reduced-motion: reduce)'), document.documentElement, document.body)
  // Animate only the scrollbar surfaces. Updating an inherited property on
  // <html> invalidates styles throughout a large document on every frame.
  // A scoped rule also covers source overlays created after this controller.
  const scrollbarStyle = document.createElement('style')
  scrollbarStyle.textContent = `
    html.markleaf-auto-hide-scrollbar::-webkit-scrollbar-thumb,
    body.markleaf-auto-hide-scrollbar::-webkit-scrollbar-thumb,
    html.markleaf-auto-hide-scrollbar .cm-scroller::-webkit-scrollbar-thumb,
    .markleaf-source-scrollbar-thumb { background-color: transparent; }
  `
  document.head.append(scrollbarStyle)
  const thumbStyle = (scrollbarStyle.sheet!.cssRules[0] as CSSStyleRule).style
  const alpha = createScrollbarAlphaController(0, 200,
    value => thumbStyle.setProperty('background-color', `color-mix(in srgb, var(--scrollbar-active, rgba(0, 0, 0, 0.35)) ${value * 100}%, transparent)`), {
      now: () => performance.now(), requestFrame: callback => requestAnimationFrame(callback), cancelFrame: id => cancelAnimationFrame(id),
    })
  const reduced = () => document.documentElement.classList.contains('markleaf-reduced-motion')
  const show = () => {
    if (!autoHide) return
    alpha.animateTo(1, reduced())
    clearTimeout(hideTimer)
    hideTimer = setTimeout(() => alpha.animateTo(0, reduced()), 800)
  }
  window.addEventListener('scroll', show, { signal: events.signal, passive: true, capture: true })
  window.addEventListener('mousemove', event => { if (event.clientX >= window.innerWidth - 20) show() }, { signal: events.signal, passive: true })
  function cursorMoved(): void {
    cancelAnimationFrame(frame)
    const editor = getEditor()
    if (!typewriter || !editor?.isEditable || !editor.view.hasFocus() || editor.view.composing) return
    frame = requestAnimationFrame(() => {
      if (editor !== getEditor() || editor.isDestroyed || !typewriter || !editor.isEditable) return
      const inset = topInset()
      const top = editor.view.coordsAtPos(editor.state.selection.head).top
      window.scrollTo({ top: Math.max(0, window.scrollY + top - inset - (window.innerHeight - inset) * .4), behavior: 'auto' })
    })
  }
  return {
    cursorMoved,
    setTypewriter(enabled: boolean, scroll = true): void {
      typewriter = enabled
      getEditor()?.view.dom.parentElement?.classList.toggle('markleaf-editor-typewriter', enabled)
      if (scroll) cursorMoved()
      else cancelAnimationFrame(frame)
    },
    setAutoHideScrollbar(enabled: boolean): void {
      autoHide = enabled
      document.documentElement.classList.toggle('markleaf-auto-hide-scrollbar', enabled)
      document.body.classList.toggle('markleaf-auto-hide-scrollbar', enabled)
      if (!enabled) { clearTimeout(hideTimer); alpha.reset(1) }
    },
    dispose(): void {
      events.abort(); unbindMotion(); cancelAnimationFrame(frame); clearTimeout(hideTimer); alpha.reset(0)
      scrollbarStyle.remove()
      document.documentElement.classList.remove('markleaf-auto-hide-scrollbar')
      document.body.classList.remove('markleaf-auto-hide-scrollbar')
    },
  }
}
