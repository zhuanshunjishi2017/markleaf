import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, executeEditorCommand } from '../src/editor'
import { parseShortcut, resolveShortcuts, shortcutLabel } from '../src/vscode-shortcuts'
import { bindFormatShortcuts, recordedShortcut } from '../src/vscode-shortcut-keys'
import { createShortcutDialog } from '../src/vscode-shortcut-dialog'
import { isWebviewMessage, type WebviewMessage } from '../src/vscode-protocol'

const cleanups: Array<() => void> = []
afterEach(() => { cleanups.splice(0).reverse().forEach(cleanup => cleanup()); document.body.replaceChildren(); vi.restoreAllMocks() })
const press = (target: Element, key: string, options: KeyboardEventInit = {}) => {
  const event = new KeyboardEvent('keydown', { key, code: key.length === 1 ? `Key${key.toUpperCase()}` : key, ctrlKey: true, bubbles: true, cancelable: true, ...options })
  target.dispatchEvent(event)
  return event
}

describe('VS Code custom format shortcuts', () => {
  it('normalizes platform keys and records the physical letter when Option produces a symbol', () => {
    expect(parseShortcut('mod + alt + m', true)).toEqual({ binding: 'Mod+Alt+M', key: 'Cmd+Alt+M' })
    expect(parseShortcut('Mod+Alt+M', false).key).toBe('Ctrl+Alt+M')
    expect(shortcutLabel('Mod+Alt+M', true)).toBe('⌘⌥M')
    expect(shortcutLabel('Mod+Alt+M', false)).toBe('Ctrl+Alt+M')
    expect(recordedShortcut(new KeyboardEvent('keydown', { key: 'µ', code: 'KeyM', metaKey: true, altKey: true }), true)).toBe('Mod+Alt+M')
    expect(parseShortcut('M', false).error).toBeTruthy()
    expect(parseShortcut('Ctrl+K Ctrl+M', false).error).toBeTruthy()
    expect(parseShortcut('Mod+Cmd+M', true).error).toBeTruthy()
  })

  it('reports duplicate or reserved bindings and does not silently activate a conflicting default', () => {
    let resolved = resolveShortcuts({ insertMathInline: 'Mod+B' }, false)
    expect(resolved.errors.insertMathInline).toContain('粗体')
    expect(resolved.errors.toggleBold).toContain('行内公式')
    expect(resolved.byKey.has('Ctrl+B')).toBe(false)
    resolved = resolveShortcuts({ toggleBold: '', insertMathInline: 'Mod+B' }, false)
    expect(resolved.byKey.get('Ctrl+B')).toBe('insertMathInline')
    for (const key of ['Mod+S', 'Mod+Z', 'Mod+Shift+V', 'Mod+F', 'Mod+Alt+V']) {
      expect(resolveShortcuts({ insertMathInline: key }, true).errors.insertMathInline).toContain('VS Code')
    }
    expect(resolveShortcuts({ toggleBold: 3 }, false).byKey.has('Ctrl+B')).toBe(false)
    expect(resolveShortcuts([], false).errors.settings).toBeTruthy()
    expect(isWebviewMessage({ type: 'updateShortcut', requestId: 1, command: 'deleteAllFiles', binding: 'Mod+M' })).toBe(false)
    expect(isWebviewMessage({ type: 'action', action: 'formatCommand', command: 'deleteAllFiles' })).toBe(false)
  })

  it('rebinds and disables default format keys only on the VS Code editor instance', () => {
    const mount = document.createElement('div'); document.body.append(mount)
    const editor = createEditor(mount, 'hello', false)
    editor.view.setProps({ handleScrollToSelection: () => true })
    cleanups.push(() => editor.destroy())
    let shortcuts = resolveShortcuts({ toggleBold: 'Mod+Alt+B' }, false)
    const dispose = bindFormatShortcuts(editor.view.dom, { mac: false, enabled: () => editor.isEditable, shortcuts: () => shortcuts,
      run: command => { executeEditorCommand(editor, command) } })
    cleanups.push(dispose)
    editor.commands.setTextSelection({ from: 1, to: 6 })
    const original = editor.state.doc
    press(editor.view.dom, 'b')
    expect(editor.state.doc).toBe(original)
    const forwarded = vi.fn(); window.addEventListener('keydown', forwarded)
    press(editor.view.dom, 'b', { altKey: true })
    window.removeEventListener('keydown', forwarded)
    expect(forwarded).not.toHaveBeenCalled()
    expect(editor.getHTML()).toContain('<strong>hello</strong>')
    shortcuts = resolveShortcuts({ toggleBold: '' }, false)
    press(editor.view.dom, 'b')
    expect(editor.isActive('bold')).toBe(true)
    editor.setEditable(false)
    press(editor.view.dom, 'i')
    expect(editor.isActive('italic')).toBe(false)
    editor.setEditable(true)
    dispose()
    press(editor.view.dom, 'b')
    expect(editor.isActive('bold')).toBe(false)
  })

  it('ignores input fields, composition and AltGraph, while leaving source toggle to VS Code', () => {
    const root = document.createElement('div'); root.innerHTML = '<input>'; document.body.append(root)
    const run = vi.fn()
    cleanups.push(bindFormatShortcuts(root, { mac: false, enabled: () => true, shortcuts: () => resolveShortcuts({ insertMathInline: 'Mod+Alt+M' }, false), run }))
    expect(press(root.querySelector('input')!, 'm', { altKey: true }).defaultPrevented).toBe(false)
    expect(press(root, 'm', { altKey: true, isComposing: true }).defaultPrevented).toBe(false)
    const altGraph = new KeyboardEvent('keydown', { key: 'm', code: 'KeyM', ctrlKey: true, altKey: true, bubbles: true, cancelable: true })
    vi.spyOn(altGraph, 'getModifierState').mockImplementation(modifier => modifier === 'AltGraph')
    root.dispatchEvent(altGraph)
    expect(altGraph.defaultPrevented).toBe(false)
    expect(run).not.toHaveBeenCalled()
    expect(press(root, 'V', { shiftKey: true }).defaultPrevented).toBe(false)
    press(root, 'm', { altKey: true })
    expect(run).toHaveBeenCalledExactlyOnceWith('insertMathInline')
  })

  it('records without executing, validates collisions, waits for saved settings and preserves errors', () => {
    // jsdom lacks native modal behavior; rendering/focus trapping are checked
    // separately in the browser. The recorder still uses real DOM key events.
    const prototype = HTMLDialogElement.prototype
    const descriptors = ['showModal', 'close'].map(key => Object.getOwnPropertyDescriptor(prototype, key))
    Object.defineProperties(prototype, {
      showModal: { configurable: true, value(this: HTMLDialogElement) { this.open = true } },
      close: { configurable: true, value(this: HTMLDialogElement) { this.open = false; this.dispatchEvent(new Event('close')) } },
    })
    cleanups.push(() => ['showModal', 'close'].forEach((key, index) => {
      if (descriptors[index]) Object.defineProperty(prototype, key, descriptors[index]!)
      else Reflect.deleteProperty(prototype, key)
    }))
    const messages: WebviewMessage[] = []
    const ui = createShortcutDialog(false, message => messages.push(message), vi.fn())
    cleanups.push(() => ui.dispose())
    ui.open()
    const dialog = document.querySelector<HTMLDialogElement>('#markleaf-shortcuts')!
    const get = <T extends HTMLElement>(selector: string) => dialog.querySelector<T>(selector)!
    get<HTMLButtonElement>('[data-shortcut-command="insertMathInline"]').click()
    const input = get<HTMLInputElement>('[data-record]')
    press(input, 'm', { ctrlKey: false })
    ui.update({ scope: 'user', overrides: {} })
    expect(get<HTMLButtonElement>('[data-save]').disabled).toBe(true)
    press(input, 'b')
    expect(get('[data-validation]').textContent).toContain('粗体')
    expect(get<HTMLButtonElement>('[data-save]').disabled).toBe(true)
    press(input, 's')
    expect(get('[data-validation]').textContent).toContain('VS Code')
    press(input, 'm', { altKey: true })
    expect(messages).toHaveLength(0)
    get<HTMLButtonElement>('[data-save]').click()
    expect(messages).toEqual([{ type: 'updateShortcut', requestId: 1, command: 'insertMathInline', binding: 'Mod+Alt+M' }])
    expect(get<HTMLButtonElement>('[data-save]').disabled).toBe(true)
    ui.saved(1, { scope: 'workspace', overrides: {} }, 'Permission denied')
    expect(get('[data-result]').textContent).toContain('Permission denied')
    expect(get<HTMLElement>('.shortcut-recorder').hidden).toBe(false)
    get<HTMLButtonElement>('[data-save]').click()
    ui.saved(2, { scope: 'workspace', overrides: { insertMathInline: 'Mod+Alt+M' } })
    expect(get('[data-scope]').textContent).toContain('工作区')
    expect(get<HTMLButtonElement>('[data-shortcut-command="insertMathInline"]').parentElement?.textContent).toContain('Ctrl+Alt+M')
    expect(get<HTMLElement>('.shortcut-recorder').hidden).toBe(true)
    get<HTMLButtonElement>('[data-shortcut-command="insertMathInline"]').click()
    get<HTMLButtonElement>('[data-clear]').click()
    get<HTMLButtonElement>('[data-save]').click()
    expect(messages.at(-1)).toMatchObject({ type: 'updateShortcut', command: 'insertMathInline', binding: '' })
    const registrationError = '当前窗口尚未注册 markleaf.shortcuts。请运行 Developer: Reload Window。'
    ui.saved(3, { scope: 'user', overrides: {}, error: registrationError }, registrationError)
    expect(get('[data-config-error]').hidden).toBe(false)
    expect(get('[data-config-error]').textContent).toContain('Developer: Reload Window')
    expect(get<HTMLButtonElement>('[data-save]').disabled).toBe(true)
    get<HTMLButtonElement>('[data-save]').click()
    expect(messages).toHaveLength(3)
    ui.update({ scope: 'user', overrides: {} })
    expect(get('[data-config-error]').hidden).toBe(true)
    expect(get<HTMLButtonElement>('[data-save]').disabled).toBe(false)
  })
})
