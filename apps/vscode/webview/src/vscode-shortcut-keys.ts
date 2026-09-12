import { defaultShortcuts, parseShortcut, type ResolvedShortcuts } from './vscode-shortcuts'

export function keyFromEvent(event: KeyboardEvent, mac: boolean): string | undefined {
  if (event.isComposing || event.keyCode === 229 || event.getModifierState('AltGraph')) return
  // Option can turn a letter into a symbol on macOS; code still identifies the
  // key the user recorded. No composition or printable text is consumed.
  const key = /^(?:Key[A-Z]|Digit\d)$/.test(event.code) ? event.code.replace(/^(Key|Digit)/, '') : event.key.toUpperCase()
  const parsed = parseShortcut([...(event.ctrlKey ? ['Ctrl'] : []), ...(event.metaKey ? ['Cmd'] : []),
    ...(event.altKey ? ['Alt'] : []), ...(event.shiftKey ? ['Shift'] : []), key].join('+'), mac)
  return parsed.key
}

export function recordedShortcut(event: KeyboardEvent, mac: boolean): string | undefined {
  const key = keyFromEvent(event, mac)
  if (!key) return
  const parts = key.split('+').map(part => part === (mac ? 'Cmd' : 'Ctrl') ? 'Mod' : part)
  return parseShortcut(parts.join('+'), mac).binding
}

export function bindFormatShortcuts(root: HTMLElement, options: {
  mac: boolean
  enabled(): boolean
  shortcuts(): ResolvedShortcuts
  run(command: string): void
}): () => void {
  const original = new Set(Object.values(defaultShortcuts).map(key => parseShortcut(key, options.mac).key!))
  // Tiptap accepts shifted I/U as aliases for italic/underline as well.
  for (const key of ['Mod+Shift+I', 'Mod+Shift+U']) original.add(parseShortcut(key, options.mac).key!)
  const keydown = (event: KeyboardEvent): void => {
    if (!(event.target instanceof Element) || event.target.closest('input, textarea, select')) return
    const nonEditable = event.target.closest('[contenteditable="false"]')
    if (nonEditable && nonEditable !== root) return
    const key = keyFromEvent(event, options.mac)
    if (!key) return
    const resolved = options.shortcuts()
    let command = resolved.byKey.get(key)
    // Preserve existing shifted aliases only while their default is effective.
    if (!command) for (const [alias, id] of [['Mod+Shift+I', 'toggleItalic'], ['Mod+Shift+U', 'toggleUnderline']] as const) {
      if (key === parseShortcut(alias, options.mac).key && resolved.byKey.get(parseShortcut(defaultShortcuts[id]!, options.mac).key!) === id) command = id
    }
    if (!command && !original.has(key)) return
    event.preventDefault()
    event.stopImmediatePropagation()
    if (command && options.enabled()) options.run(command)
  }
  root.addEventListener('keydown', keydown, true)
  return () => root.removeEventListener('keydown', keydown, true)
}
