import * as vscode from 'vscode'
import { randomUUID } from 'node:crypto'
import { posix } from 'node:path'
import { documentDirectory, resolveDocumentLink } from './resources'
import type { ImageUpload } from '../../../packages/editor-web/src/vscode-protocol'
import type { MarkLeafSettings } from '../../../packages/editor-web/src/vscode-settings'

const extensions = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'bmp', 'avif']
const maximumImageBytes = 16 * 1024 * 1024
const filters = { Images: extensions }

export function imageReference(document: vscode.TextDocument, uri: vscode.Uri, settings: MarkLeafSettings): string {
  const base = documentDirectory(document)
  if (settings.useRelativeImagePaths && base && uri.scheme === base.scheme && uri.authority === base.authority) {
    const path = posix.relative(base.path, uri.path).split('/').map(encodeURIComponent).join('/')
    return settings.prefixImagePathsWithDot && !path.startsWith('.') ? `./${path}` : path
  }
  // A remote filesystem absolute path must keep the document's URI authority
  // when resolved, instead of becoming a local file:// URI.
  if (base && uri.scheme === base.scheme && uri.authority === base.authority && uri.scheme !== 'file') {
    return uri.path.split('/').map(encodeURIComponent).join('/')
  }
  if (uri.scheme !== 'file') throw new Error('图片与文档必须位于同一个文件系统。')
  return uri.toString()
}

function assetDirectory(document: vscode.TextDocument, settings: MarkLeafSettings): vscode.Uri {
  const base = documentDirectory(document)
  if (!base) throw new Error('请先保存 Markdown 文件或打开工作区，再保存图片。')
  const directory = settings.imageDirectory.trim()
  if (!directory || /^[a-z][a-z\d+.-]*:/i.test(directory) || directory.startsWith('/') || directory.split(/[\\/]/).includes('..')) {
    throw new Error('markleaf.imageDirectory 必须是文档目录内的相对路径，例如 assets。')
  }
  return vscode.Uri.joinPath(base, directory)
}

async function storeImage(document: vscode.TextDocument, settings: MarkLeafSettings, name: string, bytes: Uint8Array): Promise<string> {
  const extension = posix.extname(name).slice(1).toLowerCase()
  if (!extensions.includes(extension)) throw new Error(`不支持的图片格式：${name}`)
  if (!bytes.byteLength || bytes.byteLength > maximumImageBytes) throw new Error('图片必须非空且不超过 16 MiB。')
  const directory = assetDirectory(document, settings)
  const stem = posix.basename(name, posix.extname(name)).replace(/[^\p{L}\p{N}_.-]/gu, '-').slice(0, 64) || 'image'
  const target = vscode.Uri.joinPath(directory, `${stem}-${randomUUID()}.${extension}`)
  await vscode.workspace.fs.createDirectory(directory)
  await vscode.workspace.fs.writeFile(target, bytes)
  return imageReference(document, target, settings)
}

export async function importImages(document: vscode.TextDocument, settings: MarkLeafSettings, files: ImageUpload[]): Promise<string[]> {
  const paths: string[] = []
  try {
    for (const file of files) paths.push(await storeImage(document, settings, file.name, Buffer.from(file.data, 'base64')))
  } catch (error) {
    // Files already created are user assets. Report them instead of deleting
    // them or hiding a partially completed import behind an empty result.
    throw new Error(`${error instanceof Error ? error.message : error}${paths.length ? ` 已保存：${paths.join('、')}` : ''}`)
  }
  return paths
}

export async function pickImages(document: vscode.TextDocument, settings: MarkLeafSettings, grant: (uri: vscode.Uri) => void, multiple = true): Promise<string[] | undefined> {
  const files = await vscode.window.showOpenDialog({ canSelectMany: multiple, filters, defaultUri: documentDirectory(document), title: 'MarkLeaf 插入图片' })
  if (!files?.length) return
  const paths: string[] = []
  try {
    for (const uri of files) {
      if (settings.fileImageHandling === 'reference') {
        const reference = imageReference(document, uri, settings)
        grant(vscode.Uri.joinPath(uri, '..'))
        paths.push(reference)
      } else {
        paths.push(await storeImage(document, settings, posix.basename(uri.path), await vscode.workspace.fs.readFile(uri)))
      }
    }
  } catch (error) {
    throw new Error(`${error instanceof Error ? error.message : error}${paths.length ? ` 已处理：${paths.join('、')}` : ''}`)
  }
  return paths
}

export async function saveImageAs(document: vscode.TextDocument, source: string): Promise<void> {
  const uri = resolveDocumentLink(document, source)
  if (!uri) throw new Error('无法解析当前图片地址。')
  const target = await vscode.window.showSaveDialog({ title: '图片另存为', defaultUri: documentDirectory(document)
    ? vscode.Uri.joinPath(documentDirectory(document)!, posix.basename(uri.path) || 'image.png') : undefined, filters })
  if (!target) return
  let bytes: Uint8Array
  if (['http', 'https'].includes(uri.scheme)) {
    const response = await fetch(uri.toString(), { signal: AbortSignal.timeout(30_000) })
    if (!response.ok) throw new Error(`图片下载失败：HTTP ${response.status}`)
    bytes = new Uint8Array(await response.arrayBuffer())
  } else bytes = await vscode.workspace.fs.readFile(uri)
  await vscode.workspace.fs.writeFile(target, bytes)
}
