import { afterEach, describe, expect, it, vi } from 'vitest'
import type * as vscode from 'vscode'
import { Uri, files, window, workspace } from './vscode-mock'
import { applicable, pickFormat } from '../../src/formatting'
import { imageReference, importImages, pickImages, saveImageAs } from '../../src/images'
import { resolveDocumentLink } from '../../src/resources'
import { defaultSettings } from '../src/vscode-settings'
import { isWebviewMessage } from '../src/vscode-protocol'
import { resolveEditorActions } from '@markleaf/editor-core/command-state'

function doc(uri = 'file:///project/readme.md', isUntitled = false): vscode.TextDocument {
  return { uri: Uri.parse(uri), isUntitled } as unknown as vscode.TextDocument
}
afterEach(() => { files.clear(); vi.clearAllMocks(); workspace.workspaceFolders = undefined })

describe('VS Code host inputs and image filesystem boundary', () => {
  it('accepts kernel command projections on toolbar requests without accepting malformed states', () => {
    const actions = resolveEditorActions({ paragraph: true, canStartFormatPainter: true }, { readOnly: false })
    for (const action of ['format', 'block', 'insertLink', 'insertImage', 'insertImageUrl', 'image', 'codeLanguage']) {
      expect(isWebviewMessage({ type: 'action', action, context: { editable: true, actions, footnoteLabels: [] } })).toBe(true)
    }
    for (const actions of [null, [], true, { toggleBold: null }, { toggleBold: { enabled: true } }, { toggleBold: { enabled: 'true', checked: false } }]) {
      expect(isWebviewMessage({ type: 'action', action: 'format', context: { actions } })).toBe(false)
    }
  })

  it('asks for table size and returns the shared command parameter; cancel makes no command', async () => {
    window.showQuickPick.mockImplementationOnce(items => Promise.resolve(items.find((item: { command?: string }) => item.command === 'insertTable')))
    window.showInputBox.mockResolvedValueOnce('4 × 5')
    expect(await pickFormat({ actions: { insertTable: { enabled: true, checked: false } } })).toEqual({ command: 'insertTable', text: '4,5' })
    const input = window.showInputBox.mock.calls[0]![0]
    expect(input.validateInput('101,2')).toBeTruthy()
    expect(input.validateInput('0,5')).toBeTruthy()
    expect(input.validateInput('2,8')).toBeUndefined()
    window.showQuickPick.mockResolvedValueOnce({ command: 'setTableCaption' })
    window.showInputBox.mockResolvedValueOnce(undefined)
    expect(await pickFormat({ actions: { setTableCaption: { enabled: true, checked: false } } })).toBeUndefined()
  })

  it('only offers contextual operations for their target and rejects duplicate footnote labels', async () => {
    expect(applicable('deleteTable', { inTable: true })).toBe(false)
    expect(applicable('deleteTable', { actions: { deleteTable: { enabled: true, checked: false } } })).toBe(true)
    expect(applicable('deleteTable', { inTable: true, actions: { deleteTable: { enabled: false, checked: false } } })).toBe(false)
    window.showQuickPick.mockResolvedValueOnce({ command: 'insertFootnote' })
    window.showInputBox.mockResolvedValueOnce('2').mockResolvedValueOnce('A note')
    expect(await pickFormat({ footnoteLabels: ['1'], actions: { insertFootnote: { enabled: true, checked: false } } })).toEqual({ command: 'insertFootnote', text: JSON.stringify({ label: '2', note: 'A note' }) })
    const input = window.showInputBox.mock.calls[0]![0]
    expect(input.value).toBe('2')
    expect(input.validateInput('1')).toBeTruthy()
    expect(input.validateInput('a b')).toBeTruthy()
  })

  it('imports bytes into unique asset files and keeps references relative to the document', async () => {
    const input = [{ name: '图片.png', data: 'aW1hZ2U=' }, { name: '图片.png', data: 'aW1hZ2U=' }]
    const paths = await importImages(doc(), defaultSettings, input)
    expect(paths).toHaveLength(2)
    expect(paths[0]).toMatch(/^\.\/assets\/%E5%9B%BE%E7%89%87-.*\.png$/)
    expect(paths[0]).not.toBe(paths[1])
    expect(files.size).toBe(2)
    expect([...files.values()].map(bytes => Buffer.from(bytes).toString())).toEqual(['image', 'image'])
  })

  it('keeps remote URI authority for asset creation and reference resolution', async () => {
    const document = doc('vscode-remote://ssh-remote+server/project/doc.md')
    const paths = await importImages(document, defaultSettings, [{ name: 'image.png', data: 'aW1hZ2U=' }])
    const resolved = resolveDocumentLink(document, paths[0]!)!
    expect(resolved.scheme).toBe('vscode-remote')
    expect(resolved.authority).toBe('ssh-remote+server')
    expect(files.has(resolved.toString())).toBe(true)
    const absolute = imageReference(document, resolved, { ...defaultSettings, useRelativeImagePaths: false })
    expect(absolute).toMatch(/^\/project\/assets\//)
    expect(resolveDocumentLink(document, absolute)?.toString()).toBe(resolved.toString())
  })

  it('preserves failure and reports partially saved assets, without deleting user files', async () => {
    workspace.fs.writeFile.mockRejectedValueOnce(new Error('Permission denied'))
    await expect(importImages(doc(), defaultSettings, [{ name: 'image.png', data: 'aW1hZ2U=' }])).rejects.toThrow('Permission denied')
    expect(files.size).toBe(0)
    await expect(importImages(doc(), defaultSettings, [{ name: 'ok.png', data: 'aW1hZ2U=' }, { name: 'bad.txt', data: 'aW1hZ2U=' }])).rejects.toThrow(/已保存.*assets/)
    expect(files.size).toBe(1)
    await expect(importImages(doc('untitled:///Untitled-1', true), defaultSettings, [{ name: 'image.png', data: 'aW1hZ2U=' }])).rejects.toThrow('先保存')
  })

  it('handles file-picker cancellation, original file references and image save-as', async () => {
    window.showOpenDialog.mockResolvedValueOnce(undefined)
    expect(await pickImages(doc(), defaultSettings, vi.fn())).toBeUndefined()
    expect(workspace.fs.writeFile).not.toHaveBeenCalled()
    const source = Uri.file('/pictures/my image.png')
    files.set(source.toString(), new Uint8Array([1, 2, 3]))
    window.showOpenDialog.mockResolvedValueOnce([source])
    const grant = vi.fn()
    expect(await pickImages(doc(), { ...defaultSettings, fileImageHandling: 'reference' }, grant)).toEqual(['../pictures/my%20image.png'])
    expect(grant).toHaveBeenCalledTimes(1)
    const target = Uri.file('/project/copy.png')
    window.showSaveDialog.mockResolvedValueOnce(target)
    await saveImageAs(doc(), '../pictures/my%20image.png')
    expect(files.get(target.toString())).toEqual(new Uint8Array([1, 2, 3]))
  })

  it('validates imported image messages before passing bytes to filesystem operations', () => {
    expect(isWebviewMessage({ type: 'action', action: 'importImages', files: [{ name: 'x.png', data: 'aW1hZ2U=' }] })).toBe(true)
    expect(isWebviewMessage({ type: 'action', action: 'importImages', files: [{ name: 'x.png', data: 12 }] })).toBe(false)
    expect(isWebviewMessage({ type: 'action', action: 'importImages' })).toBe(false)
    expect(isWebviewMessage({ type: 'action', action: 'format', context: { footnoteLabels: [3] } })).toBe(false)
  })
})
