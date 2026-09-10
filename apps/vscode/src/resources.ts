import * as vscode from 'vscode'

export function documentDirectory(document: vscode.TextDocument): vscode.Uri | undefined {
  if (document.isUntitled) return vscode.workspace.workspaceFolders?.[0]?.uri
  return vscode.Uri.joinPath(document.uri, '..')
}

/** Resolve against the document URI, so remote documents retain their authority. */
export function resolveDocumentLink(document: vscode.TextDocument, value: string): vscode.Uri | undefined {
  const href = value.trim()
  if (!href) return undefined
  if (/^https?:\/\//i.test(href) || /^mailto:/i.test(href)) return vscode.Uri.parse(href)
  if (/^file:/i.test(href)) return vscode.Uri.parse(href)
  if (/^[a-z]:[\\/]/i.test(href)) return vscode.Uri.file(href)
  if (/^[a-z][a-z\d+.-]*:/i.test(href) || href.startsWith('//')) return undefined
  const base = documentDirectory(document)
  if (!base) return undefined
  const match = /^([^?#]*)(?:\?([^#]*))?(?:#(.*))?$/.exec(href)
  if (!match) return undefined
  let pathPart = match[1] ?? ''
  try { pathPart = decodeURIComponent(pathPart) } catch { /* Literal invalid percent escapes. */ }
  pathPart = pathPart.replace(/\\/g, '/')
  const uri = pathPart.startsWith('/') ? base.with({ path: pathPart }) : vscode.Uri.joinPath(base, pathPart)
  return uri.with({ query: match[2] ?? '', fragment: match[3] ?? '' })
}

export function localResourceRoots(document: vscode.TextDocument, assets: vscode.Uri): vscode.Uri[] {
  const directory = documentDirectory(document)
  return [assets, ...(vscode.workspace.workspaceFolders ?? []).map(folder => folder.uri), ...(directory ? [directory] : [])]
}
