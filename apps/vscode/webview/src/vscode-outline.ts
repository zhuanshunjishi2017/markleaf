import type { Editor, OutlineHeading } from '@markleaf/editor-core'
import { getDocumentOutline, getActiveOutlinePosition, scrollToOutlineHeading } from '@markleaf/editor-core'

type OutlineItem = {
  heading: OutlineHeading
  parent?: OutlineItem
  children: OutlineItem[]
  depth: number
  expanded: boolean
  row: HTMLDivElement
}

/** Windows OutlineTreeView/MainForm.Outline behavior, presented in a webview. */
export function createOutlineView(editor: Editor, topInset: () => number) {
  const element = document.createElement('nav')
  element.id = 'outline'
  element.hidden = true
  element.setAttribute('aria-label', '文档大纲')
  element.innerHTML = `<div class="outline-header"><strong>大纲</strong>
    <div class="outline-actions" role="group" aria-label="大纲操作">
      <button type="button" data-outline-action="expand" title="全部展开">展开</button>
      <button type="button" data-outline-action="collapse" title="全部折叠">折叠</button>
      <button type="button" data-outline-action="locate" title="定位当前标题">定位</button>
    </div>
    <input type="search" aria-label="搜索标题" placeholder="搜索标题" spellcheck="false">
  </div><div id="outline-list" role="tree" aria-label="文档标题"></div>
  <p class="outline-empty" role="status" hidden></p>`
  const search = element.querySelector<HTMLInputElement>('input')!
  const list = element.querySelector<HTMLDivElement>('#outline-list')!
  const empty = element.querySelector<HTMLParagraphElement>('.outline-empty')!
  const locate = element.querySelector<HTMLButtonElement>('[data-outline-action="locate"]')!
  const events = new AbortController()
  let outlineDocument: Editor['state']['doc'] | undefined
  let items: OutlineItem[] = []
  let byPosition = new Map<number, OutlineItem>()
  let visibleItems: OutlineItem[] = []
  let activePosition: number | null = null
  let focusedItem: OutlineItem | undefined
  let currentItem: OutlineItem | undefined
  let pendingPosition: number | undefined
  let pendingUntil = 0
  let scrollFrame = 0

  function ensureVisible(item: OutlineItem): void {
    if (item.row.hidden) return
    // Only move the sidebar's scroll box, never its ancestors or the document.
    const top = item.row.offsetTop
    const bottom = top + item.row.offsetHeight
    if (top < list.scrollTop) list.scrollTop = top
    else if (bottom > list.scrollTop + list.clientHeight) list.scrollTop = bottom - list.clientHeight
  }

  function setFocus(item: OutlineItem | undefined, focus = false): void {
    if (focusedItem) focusedItem.row.tabIndex = -1
    focusedItem = item
    if (!item) return
    item.row.tabIndex = 0
    if (focus) { item.row.focus({ preventScroll: true }); ensureVisible(item) }
  }

  function paintCurrent(reveal = true): void {
    const next = search.value.trim() ? undefined : byPosition.get(activePosition ?? -1)
    if (next !== currentItem) {
      currentItem?.row.removeAttribute('aria-current')
      currentItem = next
      currentItem?.row.setAttribute('aria-current', 'true')
      if (reveal && currentItem) ensureVisible(currentItem)
    }
    locate.disabled = activePosition === null
  }

  function render(): void {
    const query = search.value.trim().toLocaleLowerCase()
    visibleItems = []
    for (const item of items) {
      item.row.hidden = query
        ? !item.heading.text.toLocaleLowerCase().includes(query)
        : !!item.parent && (item.parent.row.hidden || !item.parent.expanded)
      item.row.style.paddingInlineStart = `${6 + (query ? 0 : item.depth * 12)}px`
      item.row.setAttribute('aria-level', String(query ? 1 : item.depth + 1))
      if (!query && item.children.length) item.row.setAttribute('aria-expanded', String(item.expanded))
      else item.row.removeAttribute('aria-expanded')
      if (!item.row.hidden) visibleItems.push(item)
    }
    empty.hidden = visibleItems.length > 0
    empty.textContent = items.length ? '没有匹配的标题' : '文档中没有标题'
    setFocus(focusedItem && !focusedItem.row.hidden ? focusedItem : visibleItems.find(item => item.heading.position === activePosition) ?? visibleItems[0])
    paintCurrent(false)
  }

  function updateCurrent(source: 'cursor' | 'scroll'): void {
    if (element.hidden) return
    const position = getActiveOutlinePosition(editor, source, source === 'scroll' ? topInset() : 0)
    // Like the Windows host, keep the clicked item selected while the editor's
    // scroll command and the following layout frames settle (at most 750 ms).
    if (pendingPosition !== undefined) {
      if (position !== pendingPosition && performance.now() < pendingUntil) return
      pendingPosition = undefined
    }
    if (activePosition === position) return
    activePosition = position
    paintCurrent()
  }

  function refresh(): void {
    if (element.hidden || outlineDocument === editor.state.doc) return
    outlineDocument = editor.state.doc
    const parents: OutlineItem[] = []
    items = getDocumentOutline(editor).map(heading => {
      while (parents.length && parents.at(-1)!.heading.level >= heading.level) parents.pop()
      const row = window.document.createElement('div')
      row.className = 'outline-item'
      row.setAttribute('role', 'treeitem')
      row.tabIndex = -1
      row.dataset.position = String(heading.position)
      row.dataset.level = String(heading.level)
      row.title = heading.text || '（空标题）'
      const arrow = window.document.createElement('span')
      arrow.className = 'outline-disclosure'
      arrow.setAttribute('aria-hidden', 'true')
      const label = window.document.createElement('span')
      label.className = 'outline-label'
      label.textContent = row.title
      row.append(arrow, label)
      const item: OutlineItem = { heading, parent: parents.at(-1), depth: parents.length, children: [], expanded: true, row }
      item.parent?.children.push(item)
      parents.push(item)
      return item
    })
    byPosition = new Map(items.map(item => [item.heading.position, item]))
    focusedItem = undefined
    currentItem = undefined
    pendingPosition = undefined
    activePosition = getActiveOutlinePosition(editor, 'cursor')
    list.replaceChildren(...items.map(item => item.row))
    render()
  }

  function revealCurrent(): void {
    search.value = ''
    const item = byPosition.get(activePosition ?? -1)
    for (let parent = item?.parent; parent; parent = parent.parent) parent.expanded = true
    render()
    if (item) setFocus(item, true)
  }

  function activate(item: OutlineItem): void {
    if (!scrollToOutlineHeading(editor, item.heading.position, topInset(), item.heading.text)) return
    pendingPosition = activePosition = item.heading.position
    pendingUntil = performance.now() + 750
    // Desktop search results return to the hierarchy after a heading is opened.
    revealCurrent()
    paintCurrent()
  }

  list.addEventListener('click', event => {
    const target = event.target as HTMLElement
    const row = target.closest<HTMLElement>('[data-position]')
    const item = row && byPosition.get(Number(row.dataset.position))
    if (!item) return
    if (target.closest('.outline-disclosure') && item.children.length && !search.value.trim()) {
      item.expanded = !item.expanded; render(); setFocus(item, true)
    } else activate(item)
  }, { signal: events.signal })

  list.addEventListener('keydown', event => {
    const row = (event.target as HTMLElement).closest<HTMLElement>('[data-position]')
    const item = row && byPosition.get(Number(row.dataset.position))
    if (!item) return
    const index = visibleItems.indexOf(item)
    switch (event.key) {
      case 'ArrowUp': setFocus(visibleItems[Math.max(0, index - 1)], true); break
      case 'ArrowDown': setFocus(visibleItems[Math.min(visibleItems.length - 1, index + 1)], true); break
      case 'Home': setFocus(visibleItems[0], true); break
      case 'End': setFocus(visibleItems.at(-1), true); break
      case 'ArrowLeft':
        if (!search.value.trim() && item.children.length && item.expanded) { item.expanded = false; render() }
        else if (!search.value.trim() && item.parent) setFocus(item.parent, true)
        break
      case 'ArrowRight':
        if (!search.value.trim() && item.children.length) {
          if (!item.expanded) { item.expanded = true; render() }
          else setFocus(item.children[0], true)
        }
        break
      case 'Enter': case ' ': activate(item); break
      default: return
    }
    event.preventDefault(); event.stopPropagation()
  }, { signal: events.signal })

  search.addEventListener('input', () => { list.scrollTop = 0; render() }, { signal: events.signal })
  search.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return
    event.preventDefault(); event.stopPropagation(); revealCurrent()
  }, { signal: events.signal })
  element.querySelector('.outline-actions')!.addEventListener('click', event => {
    const action = (event.target as HTMLElement).closest<HTMLElement>('[data-outline-action]')?.dataset.outlineAction
    if (action === 'locate') { revealCurrent(); return }
    if (action !== 'expand' && action !== 'collapse') return
    search.value = ''
    for (const item of items) item.expanded = action === 'expand'
    render()
    if (action === 'expand' && currentItem) ensureVisible(currentItem)
  }, { signal: events.signal })
  window.addEventListener('scroll', () => {
    if (element.hidden || scrollFrame) return
    scrollFrame = requestAnimationFrame(() => { scrollFrame = 0; updateCurrent('scroll') })
  }, { signal: events.signal, passive: true })
  editor.on('update', refresh)

  return {
    element, refresh, updateCurrent,
    setVisible(visible: boolean): void {
      const changed = element.hidden === visible
      element.hidden = !visible
      if (visible) {
        refresh()
        if (changed) updateCurrent(window.scrollY > 0 ? 'scroll' : 'cursor')
      }
    },
    dispose(): void {
      events.abort(); cancelAnimationFrame(scrollFrame)
      editor.off('update', refresh)
    },
  }
}
