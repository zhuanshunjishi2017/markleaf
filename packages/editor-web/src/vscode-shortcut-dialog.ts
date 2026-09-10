import { defaultShortcuts, formatActions, resolveShortcuts, shortcutLabel, shortcutObject, type ShortcutSettings } from './vscode-shortcuts'
import { recordedShortcut } from './vscode-shortcut-keys'
import type { WebviewMessage } from './vscode-protocol'

export function createShortcutDialog(mac: boolean, post: (message: WebviewMessage) => void, onClose: () => void) {
  const dialog = document.createElement('dialog')
  dialog.id = 'markleaf-shortcuts'
  dialog.setAttribute('aria-labelledby', 'shortcut-title')
  dialog.innerHTML = `
    <header class="shortcut-header"><div><h2 id="shortcut-title">快捷键</h2><p>MarkLeaf · VS Code</p></div><button type="button" data-close aria-label="关闭快捷键设置">×</button></header>
    <div class="shortcut-intro"><p>仅在 MarkLeaf 渲染编辑区生效。公式和图表使用现有源码辅助输入。</p>
      <p data-scope></p><p>保存、撤销、查找及源码切换沿用 VS Code。其他扩展或系统占用的键位请在 VS Code 键盘快捷方式中检查。</p>
      <button type="button" data-native>在 VS Code 设置中查看</button>
      <label class="shortcut-search">搜索操作<input type="search" placeholder="公式、标题、粗体…" aria-label="搜索快捷键操作"></label>
      <p data-config-error role="status" hidden></p>
    </div>
    <section class="shortcut-recorder" hidden aria-labelledby="shortcut-edit-title">
      <h3 id="shortcut-edit-title"></h3>
      <label>按下组合键<input data-record readonly aria-label="录入快捷键" placeholder="例如 Ctrl/Cmd + Alt + M" autocomplete="off"></label>
      <p data-validation role="status"></p>
      <div class="shortcut-buttons"><button type="button" data-save>保存键位</button><button type="button" data-clear>清除键位</button><button type="button" data-default>使用默认</button><button type="button" data-cancel>取消</button></div>
    </section>
    <div class="shortcut-list" aria-label="格式操作快捷键"></div>
    <p class="shortcut-result" role="status" data-result></p>`
  document.body.append(dialog)
  const find = <T extends HTMLElement>(selector: string): T => dialog.querySelector<T>(selector)!
  const search = find<HTMLInputElement>('.shortcut-search input')
  const list = find<HTMLDivElement>('.shortcut-list')
  const recorder = find<HTMLElement>('.shortcut-recorder')
  const record = find<HTMLInputElement>('[data-record]')
  const validation = find<HTMLElement>('[data-validation]')
  const result = find<HTMLElement>('[data-result]')
  const save = find<HTMLButtonElement>('[data-save]')
  const cancel = find<HTMLButtonElement>('[data-cancel]')
  let settings: ShortcutSettings = { overrides: {}, scope: 'user' }
  let editing: string | undefined
  let draft = ''
  let recordingError = ''
  let saving = 0
  let nextRequest = 0
  let previousFocus: HTMLElement | null = null

  function validate(): void {
    if (!editing) return
    record.value = shortcutLabel(draft, mac) || '未绑定'
    const next = shortcutObject(settings.overrides) ? { ...settings.overrides, [editing]: draft } : settings.overrides
    const errors = resolveShortcuts(next, mac).errors
    const error = settings.error || recordingError || errors[editing] || errors.settings
    validation.textContent = error ?? (draft ? '保存后立即生效。录键不会执行此操作。' : '保存后不再通过快捷键触发此操作。')
    validation.classList.toggle('shortcut-error', !!error)
    record.setAttribute('aria-invalid', String(!!error))
    save.disabled = !!error || !!saving
  }

  function renderList(): void {
    const resolved = resolveShortcuts(settings.overrides, mac)
    const query = search.value.trim().toLowerCase()
    list.replaceChildren()
    const groups = ['公式与图表', ...new Set(formatActions.map(action => action.group).filter(group => group !== '公式与图表'))]
    for (const group of groups) {
      const actions = formatActions.filter(action => action.group === group && `${action.label} ${action.command} ${group}`.toLowerCase().includes(query))
      if (!actions.length) continue
      const heading = document.createElement('h3')
      heading.textContent = group
      list.append(heading)
      for (const action of actions) {
        const row = document.createElement('div')
        row.className = 'shortcut-row'
        const name = document.createElement('span')
        name.textContent = action.label
        const key = document.createElement('kbd')
        key.textContent = resolved.errors[action.command] ? '配置无效' : shortcutLabel(resolved.bindings[action.command] ?? '', mac) || '未绑定'
        key.title = resolved.errors[action.command] ?? ''
        const edit = document.createElement('button')
        edit.type = 'button'
        edit.textContent = '录入'
        edit.dataset.shortcutCommand = action.command
        edit.setAttribute('aria-label', `录入${action.label}快捷键`)
        edit.disabled = !!saving
        edit.addEventListener('click', () => {
          editing = action.command
          recordingError = ''
          const value = shortcutObject(settings.overrides) ? settings.overrides[editing] : undefined
          draft = typeof value === 'string' ? value : defaultShortcuts[editing] ?? ''
          find<HTMLElement>('#shortcut-edit-title').textContent = action.label
          recorder.hidden = false
          result.textContent = ''
          validate()
          record.focus()
        })
        row.append(name, key, edit)
        list.append(row)
      }
    }
    if (!list.childElementCount) list.textContent = '没有匹配的操作。'
  }

  function update(next: ShortcutSettings): void {
    settings = next
    const scopeNames = { user: '用户', workspace: '工作区', folder: '工作区文件夹' }
    find<HTMLElement>('[data-scope]').textContent = `保存范围：${scopeNames[settings.scope]} · 当前平台：${mac ? 'macOS' : 'Windows / Linux'}`
    const errors = resolveShortcuts(settings.overrides, mac).errors
    const errorText = Object.entries(errors).map(([command, error]) => `${formatActions.find(action => action.command === command)?.label ?? command}：${error}`)
    if (settings.error) errorText.unshift(settings.error)
    const configError = find<HTMLElement>('[data-config-error]')
    configError.hidden = !errorText.length
    configError.textContent = errorText.join('；')
    renderList()
    validate()
  }

  function finishEditing(): void {
    const command = editing
    editing = undefined
    recorder.hidden = true
    list.querySelector<HTMLButtonElement>(`[data-shortcut-command="${command}"]`)?.focus()
  }

  function setBusy(busy: boolean): void {
    for (const button of recorder.querySelectorAll<HTMLButtonElement>('button')) button.disabled = busy
    record.disabled = busy
    renderList()
    validate()
  }

  search.addEventListener('input', renderList)
  find<HTMLButtonElement>('[data-close]').addEventListener('click', () => dialog.close())
  find<HTMLButtonElement>('[data-native]').addEventListener('click', () => { dialog.close(); post({ type: 'openShortcutSettings' }) })
  cancel.addEventListener('click', finishEditing)
  find<HTMLButtonElement>('[data-clear]').addEventListener('click', () => { draft = ''; recordingError = ''; validate(); record.focus() })
  find<HTMLButtonElement>('[data-default]').addEventListener('click', () => { draft = defaultShortcuts[editing!] ?? ''; recordingError = ''; validate(); record.focus() })
  save.addEventListener('click', () => {
    if (!editing || save.disabled || saving) return
    saving = ++nextRequest
    result.textContent = '正在保存…'
    setBusy(true)
    post({ type: 'updateShortcut', requestId: saving, command: editing, binding: draft })
  })
  dialog.addEventListener('keydown', event => {
    // Never forward recorder keystrokes to VS Code's global keybindings.
    event.stopPropagation()
    if (event.isComposing || event.keyCode === 229) return
    if (event.key === 'Escape') {
      event.preventDefault()
      if (saving) dialog.close()
      else if (editing) finishEditing()
      else dialog.close()
      return
    }
    if (event.target !== record || saving) return
    if (event.key === 'Tab' && !event.ctrlKey && !event.metaKey && !event.altKey) return
    event.preventDefault()
    if (['Control', 'Meta', 'Alt', 'Shift', 'AltGraph'].includes(event.key)) return
    const binding = recordedShortcut(event, mac)
    if (binding === undefined) {
      recordingError = '请使用修饰键 + 字母、数字，或 F1–F24；Tab 切换控件，Escape 取消。'
      validate()
      return
    }
    recordingError = ''
    draft = binding
    validate()
  }, true)
  dialog.addEventListener('pointerdown', event => {
    if (event.target !== dialog) return
    const bounds = dialog.getBoundingClientRect()
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close()
  })
  dialog.addEventListener('close', () => {
    if (!saving) { editing = undefined; recorder.hidden = true }
    if (previousFocus?.isConnected) previousFocus.focus()
    onClose()
  })

  update(settings)
  return {
    get isOpen() { return dialog.open },
    contains(target: EventTarget | null) { return target instanceof Node && dialog.contains(target) },
    open() {
      if (dialog.open) { search.focus(); return }
      previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
      search.value = ''
      renderList()
      dialog.showModal()
      search.focus()
    },
    update,
    saved(requestId: number, next: ShortcutSettings, error?: string) {
      update(next)
      if (requestId !== saving) return
      saving = 0
      setBusy(false)
      result.textContent = error ? `保存失败：${error}` : '快捷键已保存。'
      result.classList.toggle('shortcut-error', !!error)
      if (!error) finishEditing()
    },
    dispose() { dialog.remove() },
  }
}
