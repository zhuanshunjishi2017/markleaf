import type { Editor } from '@tiptap/core'
import { getBlockHandleInfo, setBlockHighlight } from './editor'

/** Kept outside contenteditable so showing a block menu cannot disturb IME. */
export function createBlockHandle(
  mount: HTMLElement,
  getEditor: () => Editor,
  enabled: () => boolean,
  onMenu: (position: number, rect: DOMRect) => void,
  label: string,
  onSelect?: () => void,
) {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'ml-block-handle ml-block-handle-overlay'
  button.setAttribute('aria-label', label)
  button.tabIndex = -1
  button.hidden = true
  let position: number | null = null
  let longPressTimer: number | undefined
  function ensure(): void {
    if (button.parentElement !== mount) mount.append(button)
  }
  function hide(): void {
    if (longPressTimer !== undefined) {
      window.clearTimeout(longPressTimer)
      longPressTimer = undefined
    }
    button.hidden = true
    button.style.display = 'none'
    button.style.removeProperty('left')
    button.style.removeProperty('top')
    button.textContent = ''
    button.classList.remove('ml-block-handle-active')
    position = null
  }
  function update(): void {
    ensure()
    const editor = getEditor()
    const info = enabled() ? getBlockHandleInfo(editor) : null
    if (!info) { hide(); return }
    position = info.position
    button.hidden = false
    button.style.removeProperty('display')
    button.textContent = info.label
    button.classList.toggle('ml-block-handle-active', info.active)
    const rect = mount.getBoundingClientRect()
    button.style.left = `${Math.max(2, editor.view.dom.getBoundingClientRect().left - rect.left - 36)}px`
    button.style.top = `${info.viewportTop - rect.top}px`
  }
  button.addEventListener('pointerdown', event => {
    event.preventDefault()
    event.stopPropagation()
    if (position === null || !enabled()) return
    setBlockHighlight(getEditor(), position)
    update()
    onSelect?.()
    if (event.pointerType === 'mouse' && event.button !== 0) return
    longPressTimer = window.setTimeout(() => {
      longPressTimer = undefined
      if (position === null || !enabled()) return
      onMenu(position, button.getBoundingClientRect())
    }, 450)
  })
  button.addEventListener('pointerup', event => {
    event.preventDefault()
    event.stopPropagation()
    if (longPressTimer !== undefined) {
      window.clearTimeout(longPressTimer)
      longPressTimer = undefined
    }
  })
  button.addEventListener('pointercancel', () => {
    if (longPressTimer !== undefined) {
      window.clearTimeout(longPressTimer)
      longPressTimer = undefined
    }
  })
  button.addEventListener('contextmenu', event => {
    event.preventDefault()
    event.stopPropagation()
  })
  return { button, update, hide, ensure, dispose: () => {
    hide()
    button.remove()
  } }
}
