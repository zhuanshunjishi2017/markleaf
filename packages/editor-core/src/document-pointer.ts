import type { Editor } from '@tiptap/core'
import { NodeSelection } from '@tiptap/pm/state'
import { expandSourceEditor, collapseSourceEditor, isSourceEditorExpanded } from './editor'
import { normalizeContextMenuCaretPosition } from './format-painter'

export function selectEditorContextAt(editor: Editor, point: { left: number; top: number }): void {
  const resolved = editor.view.posAtCoords(point)
  if (!resolved) return
  const selection = editor.state.selection
  if (!selection.empty && resolved.pos >= selection.from && resolved.pos <= selection.to) return
  const node = resolved.inside >= 0 ? editor.state.doc.nodeAt(resolved.inside) : null
  if (node?.isAtom && node.type.spec.selectable !== false) editor.commands.setNodeSelection(resolved.inside)
  else if (editor.isEditable) editor.commands.setTextSelection(normalizeContextMenuCaretPosition(editor, resolved.pos))
}

/** A first click selects an atom; a subsequent click toggles its shared source editor. */
export function bindDocumentPointerInteractions(
  editorMount: HTMLElement, getEditor: () => Editor, changed: () => void,
): () => void {
  const events = new AbortController()
  const { signal } = events
  const active = () => !editorMount.hidden && !getEditor().isDestroyed
  function findMathNodeAt(pos: number): number | null {
    const editor = getEditor()
    const node = editor.state.doc.nodeAt(pos)
    if (node && (node.type.name === 'mathInline' || node.type.name === 'mathBlock')) {
      return pos
    }
    const before = pos > 0 ? editor.state.doc.nodeAt(pos - 1) : null
    if (before && (before.type.name === 'mathInline' || before.type.name === 'mathBlock')) {
      return pos - 1
    }
    return null
  }

  function findMathNodeFromTarget(target: EventTarget | null): number | null {
    const editor = getEditor()
    if (!(target instanceof Element)) {
      return null
    }

    const mathElement = target.closest<HTMLElement>('.markleaf-math')
    if (!mathElement || !editorMount.contains(mathElement)) {
      return null
    }

    let mathPosition: number | null = null
    editor.state.doc.descendants((node, position) => {
      if (mathPosition !== null) return false
      if (node.type.name !== 'mathInline' && node.type.name !== 'mathBlock') return true
      if (editor.view.nodeDOM(position) === mathElement) {
        mathPosition = position
        return false
      }
      return true
    })
    return mathPosition
  }

  function findSpecialNodeFromTarget(target: EventTarget | null, nodeName: 'mathInline' | 'mathBlock' | 'mermaid', className: string): number | null {
    const editor = getEditor()
    if (!(target instanceof Element)) return null
    const element = target.closest<HTMLElement>(className)
    if (!element || !editorMount.contains(element)) return null

    let position: number | null = null
    editor.state.doc.descendants((node, nodePosition) => {
      if (position !== null) return false
      if (node.type.name === nodeName && editor.view.nodeDOM(nodePosition) === element) {
        position = nodePosition
        return false
      }
      return true
    })
    return position
  }

  function findMermaidNodeAt(pos: number): number | null {
    const editor = getEditor()
    const node = editor.state.doc.nodeAt(pos)
    if (node?.type.name === 'mermaid') {
      return pos
    }
    const before = pos > 0 ? editor.state.doc.nodeAt(pos - 1) : null
    if (before?.type.name === 'mermaid') {
      return pos - 1
    }
    return null
  }

  function findHorizontalRuleAtY(clientY: number): number | null {
    const editor = getEditor()
    let closestPosition: number | null = null
    let closestDistance = Number.POSITIVE_INFINITY

    editor.state.doc.descendants((node, position) => {
      if (node.type.name !== 'horizontalRule') return true

      const nodeDom = editor.view.nodeDOM(position)
      if (!(nodeDom instanceof HTMLElement)) return false

      const rect = nodeDom.getBoundingClientRect()
      const computed = window.getComputedStyle(nodeDom)
      const marginTop = Number.parseFloat(computed.marginTop)
      const marginBottom = Number.parseFloat(computed.marginBottom)
      const lineHeight = Number.parseFloat(computed.lineHeight)
      const fallbackPadding = Number.isFinite(lineHeight) ? lineHeight / 2 : 8
      const rowTop = rect.top - (Number.isFinite(marginTop) ? marginTop : fallbackPadding)
      const rowBottom = rect.bottom + (Number.isFinite(marginBottom) ? marginBottom : fallbackPadding)
      if (clientY < rowTop || clientY > rowBottom) return false

      const distance = Math.abs(clientY - (rect.top + rect.bottom) / 2)
      if (distance < closestDistance) {
        closestDistance = distance
        closestPosition = position
      }
      return false
    })

    return closestPosition
  }

  let pendingSpecialClick: {
    kind: 'math' | 'mermaid'
    position: number
    wasSelected: boolean
  } | null = null

  editorMount.addEventListener('mousedown', (event) => {
    const editor = getEditor()
    if (!active() || event.button !== 0) {
      pendingSpecialClick = null
      return
    }
    if (event.target instanceof Element && event.target.closest('.markleaf-expanded-source, .ml-block-handle')) {
      pendingSpecialClick = null
      return
    }

    const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
    const mathPosition = findMathNodeFromTarget(event.target)
      ?? (resolved ? findMathNodeAt(resolved.pos) : null)
    const mermaidPosition = mathPosition === null
      ? findSpecialNodeFromTarget(event.target, 'mermaid', '.markleaf-mermaid')
        ?? (resolved ? findMermaidNodeAt(resolved.pos) : null)
      : null
    const position = mathPosition ?? mermaidPosition
    if (position === null) {
      pendingSpecialClick = null
      return
    }

    const selected = editor.state.selection
    pendingSpecialClick = {
      kind: mathPosition !== null ? 'math' : 'mermaid',
      position,
      wasSelected: selected instanceof NodeSelection && selected.from === position,
    }
  }, { signal, capture: true })

  editorMount.addEventListener('click', (event) => {
    const editor = getEditor()
    if (!active()) {
      return
    }
    if (event.target instanceof Element && event.target.closest('.markleaf-expanded-source, .ml-block-handle')) {
      return
    }
    const resolved = editor.view.posAtCoords({ left: event.clientX, top: event.clientY })
    const horizontalRulePosition = findHorizontalRuleAtY(event.clientY)
    if (horizontalRulePosition !== null) {
      event.preventDefault()
      pendingSpecialClick = null
      editor.commands.setNodeSelection(horizontalRulePosition)
      changed()
      return
    }
    const mathPosition = findMathNodeFromTarget(event.target)
      ?? (resolved ? findMathNodeAt(resolved.pos) : null)
    const mermaidPosition = mathPosition === null
      ? findSpecialNodeFromTarget(event.target, 'mermaid', '.markleaf-mermaid')
        ?? (resolved ? findMermaidNodeAt(resolved.pos) : null)
      : null
    const position = mathPosition ?? mermaidPosition
    if (position === null) {
      pendingSpecialClick = null
      return
    }

    const kind = mathPosition !== null ? 'math' : 'mermaid'
    const pending = pendingSpecialClick
    pendingSpecialClick = null
    const wasSelected = pending?.kind === kind
      && pending.position === position
      && pending.wasSelected

    event.preventDefault()
    if (!wasSelected) {
      editor.commands.setNodeSelection(position)
      changed()
      return
    }

    if (kind === 'math') {
      const selectedNode = editor.state.doc.nodeAt(position)
      if (selectedNode?.type.name !== 'mathInline' && selectedNode?.type.name !== 'mathBlock') return
      const selectedKind = selectedNode.type.name === 'mathInline' ? 'mathInline' : 'mathBlock'
      if (isSourceEditorExpanded(editor, position, selectedKind)) collapseSourceEditor(editor)
      else expandSourceEditor(editor, position, selectedKind)
      changed()
      return
    }
    editor.commands.setNodeSelection(position)
    if (isSourceEditorExpanded(editor, position, 'mermaid')) collapseSourceEditor(editor)
    else expandSourceEditor(editor, position, 'mermaid')
    changed()
  }, { signal })
  return () => events.abort()
}
