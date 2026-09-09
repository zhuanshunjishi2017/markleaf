import type { Editor } from '@tiptap/core'
import { clearFindHighlights, findInEditor, replaceAllInEditor, replaceCurrentInEditor } from './editor'

export function createFindBar(editor: Editor, toolbar: HTMLElement) {
  const form = document.createElement('form')
  form.id = 'find-bar'
  form.setAttribute('aria-label', '查找与替换')
  form.hidden = true
  form.innerHTML = `<input id="find-input" aria-label="查找文本" placeholder="查找文本" />
    <label><input id="find-case" type="checkbox" />区分大小写</label>
    <label><input id="find-whole" type="checkbox" />全词匹配</label>
    <span id="find-result" role="status"></span>
    <button type="button" id="find-previous" title="上一个 (Shift+Enter)">↑</button>
    <button type="button" id="find-next" title="下一个 (Enter)">↓</button>
    <span id="replace-controls" hidden><input id="replace-input" aria-label="替换为" placeholder="替换为" />
    <button type="button" id="replace-one">替换</button><button type="button" id="replace-all">全部替换</button></span>
    <button type="button" id="find-close" title="关闭 (Escape)">×</button>`
  toolbar.after(form)
  // The toolbar can wrap on narrow editors; keep both sticky rows visible.
  const positionBar = (): void => { form.style.top = `${toolbar.getBoundingClientRect().height}px` }
  const toolbarResize = new ResizeObserver(positionBar)
  toolbarResize.observe(toolbar)
  positionBar()
  const query = form.querySelector<HTMLInputElement>('#find-input')!
  const replacement = form.querySelector<HTMLInputElement>('#replace-input')!
  const sensitive = form.querySelector<HTMLInputElement>('#find-case')!
  const whole = form.querySelector<HTMLInputElement>('#find-whole')!
  const result = form.querySelector<HTMLElement>('#find-result')!
  const replaceControls = form.querySelector<HTMLElement>('#replace-controls')!
  let replacing = false
  function find(backwards = false, reset = false): void {
    if (reset) clearFindHighlights(editor)
    const found = findInEditor(editor, query.value, sensitive.checked, whole.checked, backwards)
    result.textContent = `${found.current} / ${found.total}`
  }
  function close(): void { form.hidden = true; clearFindHighlights(editor); editor.commands.focus() }
  function update(): void {
    for (const input of replaceControls.querySelectorAll<HTMLInputElement | HTMLButtonElement>('input,button')) input.disabled = !editor.isEditable
  }
  function refresh(): void {
    update()
    if (!form.hidden && !replacing) find(false, true)
  }
  form.addEventListener('submit', event => { event.preventDefault(); find() })
  for (const input of [query, sensitive, whole]) input.addEventListener('input', event => {
    if (!(event instanceof InputEvent) || !event.isComposing) find(false, true)
  })
  form.addEventListener('keydown', event => {
    if (event.isComposing) return
    if (event.key === 'Escape') { event.preventDefault(); close() }
    if (event.key === 'Enter') { event.preventDefault(); find(event.shiftKey) }
  })
  form.querySelector('#find-previous')!.addEventListener('click', () => find(true))
  form.querySelector('#find-next')!.addEventListener('click', () => find())
  form.querySelector('#find-close')!.addEventListener('click', close)
  for (const all of [false, true]) form.querySelector(all ? '#replace-all' : '#replace-one')!.addEventListener('click', () => {
    if (!editor.isEditable || editor.view.composing) return
    replacing = true
    try {
      if (all) {
        const count = replaceAllInEditor(editor, query.value, replacement.value, sensitive.checked, whole.checked)
        result.textContent = `已替换 ${count} 处`
      } else {
        const found = replaceCurrentInEditor(editor, query.value, replacement.value, sensitive.checked, whole.checked)
        result.textContent = `${found.current} / ${found.total}`
      }
    } finally { replacing = false }
  })
  editor.on('update', refresh)
  return {
    update, refresh,
    open(replace = false): void {
      form.hidden = false
      replaceControls.hidden = !replace
      const selected = editor.state.doc.textBetween(editor.state.selection.from, editor.state.selection.to)
      if (selected && !selected.includes('\n')) query.value = selected
      refresh()
      query.focus()
      query.select()
    },
    dispose(): void { editor.off('update', refresh); toolbarResize.disconnect(); form.remove() },
  }
}
