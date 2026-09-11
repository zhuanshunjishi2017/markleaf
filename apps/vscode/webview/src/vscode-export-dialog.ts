import { colorThemes, typographyStyles } from './vscode-settings'
import { exportDefaults, isExportOptions, paperSizes, type ExportOptions } from './vscode-export-options'
import { exportStrings, exportOptionLabel } from './vscode-export-strings'
import type { WebviewMessage } from './vscode-protocol'

export function createExportDialog(post: (message: WebviewMessage) => void, onClose: () => void) {
  const dialog = document.createElement('dialog')
  dialog.id = 'markleaf-export'
  dialog.setAttribute('aria-labelledby', 'export-title')
  document.body.append(dialog)
  let strings = exportStrings('en')
  let displayLanguage = 'en'
  let busy = false
  let options = { ...exportDefaults }
  let previousFocus: HTMLElement | null = null
  const element = <T extends HTMLElement>(query: string): T => dialog.querySelector<T>(query)!
  function render(): void {
    const label = (key: keyof ExportOptions) => strings[key as keyof typeof strings] ?? key
    const select = (key: keyof ExportOptions, values: readonly string[]) => `<label>${label(key)}<select name="${key}">${values.map(value => `<option value="${value}">${exportOptionLabel(key === 'format' && value === 'print' ? 'printDialog' : value, displayLanguage)}</option>`).join('')}</select></label>`
    const number = (key: keyof ExportOptions, min: number, max: number, step = 1) => `<label>${label(key)}<input type="number" name="${key}" min="${min}" max="${max}" step="${step}" required></label>`
    const checkbox = (key: keyof ExportOptions) => `<label class="export-check"><input type="checkbox" name="${key}">${label(key)}</label>`
    dialog.innerHTML = `<form>
      <header><div><h2 id="export-title">${strings.title}</h2><p>${strings.intro}</p></div><button type="button" data-close aria-label="${strings.close}">×</button></header>
      <fieldset class="export-fields">
        <div class="export-grid">${select('format', ['pdf', 'html', 'png', 'jpg', 'print'])}${select('typography', typographyStyles)}${select('colorTheme', colorThemes.filter(name => name !== 'vscode'))}
          ${number('fontSize', 10, 32, .5)}${number('lineHeight', 1, 3, .05)}<span data-width>${number('contentWidth', 320, 2400)}</span></div>
        <section data-paper><div class="export-grid">${select('paperSize', paperSizes)}${checkbox('landscape')}${number('marginTop', 0, 50, .5)}${number('marginRight', 0, 50, .5)}${number('marginBottom', 0, 50, .5)}${number('marginLeft', 0, 50, .5)}</div>
          ${checkbox('pageNumbers')}${checkbox('keepTablesTogether')}${checkbox('keepHeadingsWithNext')}<p class="export-hint">${strings.pageHint}</p></section>
        <div class="export-grid"><label>${strings.header}<input name="header" maxlength="500"></label><label>${strings.footer}<input name="footer" maxlength="500"></label></div>
        <section data-image><div class="export-grid">${number('imageScale', 1, 3)}${number('imageMaxHeight', 1000, 30000)}<span data-jpeg>${number('jpegQuality', 1, 100)}</span></div><p class="export-hint">${strings.imageHint}</p></section>
        <p data-print class="export-hint">${strings.printHint}</p>
      </fieldset>
      <p data-result role="status" aria-live="polite"></p>
      <div class="export-buttons"><button type="button" data-default>${strings.defaults}</button><span></span><button type="button" data-preview>${strings.preview}</button><button type="submit" data-save>${strings.save}</button><button type="button" data-cancel>${strings.cancel}</button></div>
    </form>`
    for (const [key, value] of Object.entries(options)) {
      const input = element<HTMLInputElement | HTMLSelectElement>(`[name="${key}"]`)
      if (input instanceof HTMLInputElement && input.type === 'checkbox') input.checked = value === true
      else input.value = String(value)
    }
    element<HTMLFormElement>('form').addEventListener('submit', event => { event.preventDefault(); submit(false) })
    element('[data-preview]').addEventListener('click', () => submit(true))
    element('[data-default]').addEventListener('click', () => { options = { ...exportDefaults, format: element<HTMLSelectElement>('[name="format"]').value as ExportOptions['format'] }; render() })
    element('[data-cancel]').addEventListener('click', cancel)
    element('[data-close]').addEventListener('click', cancel)
    element('[name="format"]').addEventListener('change', updateFields)
    updateFields()
  }
  function updateFields(): void {
    const format = element<HTMLSelectElement>('[name="format"]').value
    element('[data-paper]').hidden = !['pdf', 'print'].includes(format)
    element('[data-image]').hidden = !['png', 'jpg'].includes(format)
    element('[data-jpeg]').hidden = format !== 'jpg'
    element('[data-width]').hidden = ['pdf', 'print'].includes(format)
    element('[data-print]').hidden = format !== 'print'
    element('[data-save]').textContent = format === 'print' ? strings.print : strings.save
    element('[data-preview]').hidden = format === 'print'
  }
  function submit(preview: boolean): void {
    if (busy || !element<HTMLFormElement>('form').reportValidity()) return
    const data: Record<string, unknown> = {}
    for (const [key, value] of Object.entries(exportDefaults)) {
      const input = element<HTMLInputElement | HTMLSelectElement>(`[name="${key}"]`)
      data[key] = typeof value === 'boolean' ? (input as HTMLInputElement).checked : typeof value === 'number' ? Number(input.value) : input.value
    }
    if (!isExportOptions(data)) { result(strings.invalid, true); return }
    options = data
    setBusy(true)
    result(strings.preparing)
    post({ type: 'export', options, preview })
  }
  function result(message: string, error = false): void {
    element('[data-result]').textContent = message
    element('[data-result]').classList.toggle('export-error', error)
  }
  function setBusy(value: boolean): void {
    busy = value
    element<HTMLFieldSetElement>('fieldset').disabled = value
    for (const selector of ['[data-default]', '[data-save]', '[data-preview]']) element<HTMLButtonElement>(selector).disabled = value
  }
  function cancel(): void {
    if (busy) { post({ type: 'cancelExport' }); return }
    dialog.close()
  }
  dialog.addEventListener('cancel', event => { event.preventDefault(); cancel() })
  dialog.addEventListener('close', () => { previousFocus?.focus(); onClose() })
  return {
    get isOpen() { return dialog.open },
    open(next: ExportOptions, language: string) {
      if (busy) return
      displayLanguage = language
      strings = exportStrings(language)
      options = { ...next }
      previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
      render()
      if (!dialog.open) dialog.showModal()
    },
    finished(message: string, error = false) {
      if (!dialog.open) return
      setBusy(false)
      result(message, error)
    },
    dispose() { dialog.remove() },
  }
}
