import type { Editor } from '@tiptap/core'

export type OutlineHeading = { level: number; text: string; position: number }

export function getDocumentOutline(editor: Editor): OutlineHeading[] {
  const headings: OutlineHeading[] = []
  editor.state.doc.descendants((node, position) => {
    if (node.type.name === 'heading') headings.push({ level: node.attrs.level, text: node.textContent, position })
  })
  return headings
}

export function getActiveOutlinePosition(editor: Editor, source: 'cursor' | 'scroll', topInset = 0): number | null {
  const headings = getDocumentOutline(editor)
  let active: number | null = source === 'scroll' ? headings[0]?.position ?? null : null
  const threshold = topInset + Math.max(80, (window.innerHeight - topInset) * .2)
  for (const heading of headings) {
    const node = editor.view.nodeDOM(heading.position)
    if (source === 'cursor' ? heading.position <= editor.state.selection.from
      : node instanceof HTMLElement && node.getBoundingClientRect().top <= threshold) active = heading.position
  }
  return active
}

export function scrollToOutlineHeading(editor: Editor, position: number, topInset = 0): boolean {
  const heading = editor.view.nodeDOM(position)
  if (!(heading instanceof HTMLElement) || !/^H[1-6]$/.test(heading.tagName)) return false
  const lineHeight = Number.parseFloat(window.getComputedStyle(heading).lineHeight)
  const top = Math.max(0, window.scrollY + heading.getBoundingClientRect().top - topInset - (Number.isFinite(lineHeight) ? lineHeight / 2 : 12))
  window.scrollTo({ top, behavior: document.documentElement.classList.contains('markleaf-reduced-motion') ? 'auto' : 'smooth' })
  return true
}

export function assignHeadingAnchors(root: ParentNode): void {
  const used = new Set<string>()
  for (const heading of root.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
    const slug = (heading.textContent ?? '').toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-') || 'heading'
    let id = slug
    for (let suffix = 1; used.has(id); suffix++) id = `${slug}-${suffix}`
    used.add(id)
    heading.id = id
  }
}

export function scrollToHeadingAnchor(editor: Editor, fragment: string, topInset = 0): boolean {
  let slug = fragment.replace(/^#/, '')
  try { slug = decodeURIComponent(slug) } catch { /* Match a literal percent in the fragment. */ }
  assignHeadingAnchors(editor.view.dom)
  const heading = getDocumentOutline(editor).find(item => {
    const node = editor.view.nodeDOM(item.position)
    return node instanceof HTMLElement && node.id === slug
  })
  return heading ? scrollToOutlineHeading(editor, heading.position, topInset) : false
}
