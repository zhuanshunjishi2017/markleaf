export function clearDomSelection(ownerDocument: Document | undefined | null): boolean {
  const selection = ownerDocument?.getSelection()
  if (!selection || selection.isCollapsed) return false
  selection.removeAllRanges()
  return true
}
