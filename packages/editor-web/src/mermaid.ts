import { Node, mergeAttributes } from '@tiptap/core'
import { sharedEditorStrings, type SharedEditorStrings } from './shared-editor-strings'

type MermaidModule = typeof import('mermaid')

let mermaidPromise: Promise<MermaidModule> | null = null
let mermaidInitializationKey: string | null = null
let mermaidSequence = 0
const MERMAID_RENDER_TIMEOUT_MS = 1000
let mermaidStrings = sharedEditorStrings('zh-Hans', 'ctrl')
let markdownCodeFence: 'backtick' | 'tilde' = 'backtick'
// Mermaid measures labels with this font. Keep measurement and display stable
// instead of inheriting the document's wider serif typography.
function getActiveMermaidFontFamily(): string {
  const documentRoot = document.querySelector<HTMLElement>('.markleaf-document')
  if (documentRoot) {
    const fontFamily = window.getComputedStyle(documentRoot).fontFamily.trim()
    if (fontFamily) return fontFamily
  }
  return 'sans-serif'
}

type MermaidThemeSettings = {
  fontFamily: string
  theme: MermaidThemeName
}

export type MermaidThemeName = 'default' | 'dark' | 'forest' | 'neutral' | 'base'

function getCssThemeColor(name: string, fallback: string): string {
  const value = window.getComputedStyle(document.documentElement).getPropertyValue(`--${name}`).trim()
  return value || fallback
}

function isDarkCssColor(value: string): boolean {
  const probe = document.createElement('span')
  probe.style.color = value
  probe.style.position = 'fixed'
  probe.style.visibility = 'hidden'
  document.body.append(probe)
  const resolved = window.getComputedStyle(probe).color
  probe.remove()
  const match = resolved.match(/rgba?\(\s*([\d.]+)[, ]+\s*([\d.]+)[, ]+\s*([\d.]+)/i)
  if (!match) return false
  const red = Number(match[1])
  const green = Number(match[2])
  const blue = Number(match[3])
  return (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255 < 0.5
}

function getActiveMermaidTheme(themeOverride?: MermaidThemeName): MermaidThemeSettings {
  const background = getCssThemeColor('bg-primary', '#ffffff')
  const documentTheme = document.querySelector<HTMLElement>('.markleaf-document')
    ? window.getComputedStyle(document.querySelector<HTMLElement>('.markleaf-document')!)
      .getPropertyValue('--ml-mermaid-theme').trim()
    : ''
  const requestedTheme = isMermaidThemeName(documentTheme) ? documentTheme : undefined
  return {
    fontFamily: getActiveMermaidFontFamily(),
    theme: themeOverride ?? requestedTheme ?? (isDarkCssColor(background) ? 'dark' : 'default'),
  }
}

function isMermaidThemeName(value: string): value is MermaidThemeName {
  return value === 'default'
    || value === 'dark'
    || value === 'forest'
    || value === 'neutral'
    || value === 'base'
}

export function setMermaidMarkdownCodeFence(preference: 'backtick' | 'tilde'): void {
  markdownCodeFence = preference
}

export function setMermaidStrings(
  strings: Pick<SharedEditorStrings, 'mermaidEmpty' | 'mermaidError' | 'mermaidTimeout'>,
): void {
  mermaidStrings = { ...mermaidStrings, ...strings }
}

class MermaidRenderTimeoutError extends Error {}
type MermaidRenderResult = 'rendered' | 'empty' | 'error' | 'timeout'

async function loadMermaid(): Promise<MermaidModule> {
  mermaidPromise ??= import('mermaid')
  return mermaidPromise
}

async function ensureMermaidInitialized(module: MermaidModule, settings: MermaidThemeSettings): Promise<void> {
  const initializationKey = JSON.stringify(settings)
  if (mermaidInitializationKey === initializationKey) return
  module.default.initialize({
    startOnLoad: false,
    securityLevel: 'strict',
    suppressErrorRendering: true,
    // Mermaid's built-in themes define a much broader palette than MarkLeaf's
    // UI variables (pie, git, gantt, sequence, quadrant, architecture, etc.).
    // Only select the matching complete palette here; do not collapse it into
    // the handful of colors exposed by a MarkLeaf color theme.
    theme: settings.theme,
    themeVariables: {
      fontFamily: settings.fontFamily,
    },
  })
  mermaidInitializationKey = initializationKey
}

function nextMermaidId(prefix: string): string {
  mermaidSequence += 1
  return `${prefix}-${mermaidSequence}`
}

async function renderMermaidSvgInto(
  host: HTMLElement,
  source: string,
): Promise<MermaidRenderResult> {
  if (!source.trim()) {
    renderMermaidMessage(host, mermaidStrings.mermaidEmpty, 'empty')
    return 'empty'
  }
  const module = await loadMermaid()
  await ensureMermaidInitialized(module, getActiveMermaidTheme())
  const id = nextMermaidId('markleaf-mermaid')
  return withTimeout(module.default.render(id, source), MERMAID_RENDER_TIMEOUT_MS).then(({ svg }) => {
    host.innerHTML = svg
    normalizeMermaidSvg(host)
    return 'rendered' as const
  }).catch((error: unknown) => {
    cleanupMermaidErrorArtifacts()
    return error instanceof MermaidRenderTimeoutError ? 'timeout' : 'error'
  })
}

function withTimeout<T>(promise: Promise<T>, timeoutMs: number): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(
      () => reject(new MermaidRenderTimeoutError('Mermaid render timed out')),
      timeoutMs,
    )
    promise.then(
      value => {
        window.clearTimeout(timer)
        resolve(value)
      },
      error => {
        window.clearTimeout(timer)
        reject(error)
      },
    )
  })
}

function cleanupMermaidErrorArtifacts(root: ParentNode = document): void {
  root.querySelectorAll<HTMLElement | SVGElement>('[id^="dmermaid-"], [id^="mermaid-"][aria-roledescription="error"], .mermaidError')
    .forEach((element) => {
      if (!element.closest('.markleaf-mermaid')) {
        element.remove()
      }
    })
}

function renderMermaidMessage(host: HTMLElement, text: string, kind: 'empty' | 'error'): void {
  const message = document.createElement('div')
  message.className = `markleaf-mermaid-message markleaf-mermaid-message-${kind}`
  message.textContent = text
  host.replaceChildren(message)
}

function normalizeMermaidSvg(root: ParentNode): void {
  root.querySelectorAll<HTMLElement | SVGElement>('svg, svg *, foreignObject, foreignObject *')
    .forEach((element) => {
      ;(element as HTMLElement | SVGElement).style.textIndent = '0px'
      // The shared style sheet intentionally uses !important so diagram text
      // does not inherit unrelated node rules. Match that priority here, or
      // the browser can render with a different font than Mermaid measured.
      ;(element as HTMLElement | SVGElement).style.setProperty(
        'font-family',
        getActiveMermaidFontFamily(),
        'important',
      )
    })
}

const MermaidNodeView = ({ node }: { node: { type?: { name: string }; textContent: string } }) => {
  const wrapper = document.createElement('div')
  wrapper.className = 'markleaf-mermaid'
  wrapper.contentEditable = 'false'

  let lastSource = node.textContent
  const section = document.createElement('div')
  section.className = 'markleaf-mermaid-view'
  wrapper.append(section)

  const renderCurrent = () => {
    section.replaceChildren()
    void renderMermaidSvgInto(section, lastSource).then((result) => {
      if ((result === 'error' || result === 'timeout') && section.isConnected) {
        renderMermaidMessage(
          section,
          result === 'timeout' ? mermaidStrings.mermaidTimeout : mermaidStrings.mermaidError,
          'error',
        )
      }
      // 等最终 SVG 或错误提示插入 DOM 后再通知浮层重新测量锚点高度。
      window.requestAnimationFrame(() => {
        if (section.isConnected) window.dispatchEvent(new Event('markleaf-mermaid-rendered'))
      })
    })
  }
  wrapper.addEventListener('markleaf-rerender-mermaid', renderCurrent)
  renderCurrent()

  return {
    dom: wrapper,
    update: (updated: { type?: { name: string }; textContent: string }) => {
      if (updated.type?.name !== 'mermaid') return false
      const source = updated.textContent
      if (source === lastSource) return true
      lastSource = source
      renderCurrent()
      return true
    },
    destroy: () => {
      wrapper.removeEventListener('markleaf-rerender-mermaid', renderCurrent)
    },
  }
}

export function rerenderMermaidElements(root: ParentNode = document): void {
  root.querySelectorAll<HTMLElement>('.markleaf-mermaid')
    .forEach((element) => element.dispatchEvent(new Event('markleaf-rerender-mermaid')))
}

export function rerenderMermaidElement(element: Element | null): boolean {
  const target = element?.closest<HTMLElement>('.markleaf-mermaid') ?? null
  if (!target) return false
  target.dispatchEvent(new Event('markleaf-rerender-mermaid'))
  return true
}

export const Mermaid = Node.create({
  name: 'mermaid',
  priority: 1000,
  group: 'block',
  code: true,
  atom: true,
  selectable: true,
  content: 'text*',

  parseHTML() {
    return [{ tag: 'div[data-mermaid]' }]
  },

  renderHTML({ node }: any) {
    return [
      'div',
      mergeAttributes({ class: 'markleaf-mermaid', 'data-mermaid': '1' }),
      node.textContent,
    ]
  },

  markdownTokenName: 'code',

  parseMarkdown(token: any, helpers: any) {
    if (token.lang !== 'mermaid') return null
    if (!token.raw?.startsWith('```') && !token.raw?.startsWith('~~~')) return null
    return helpers.createNode(
      'mermaid',
      null,
      token.text ? [helpers.createTextNode(token.text)] : [],
    )
  },

  renderMarkdown(node: any, helpers: any) {
    const hasContent = Array.isArray(node.content) && node.content.length > 0
    const source = hasContent ? helpers.renderChildren(node.content) : ''
    const markerCharacter = markdownCodeFence === 'tilde' ? '~' : '`'
    const runs = source.match(markerCharacter === '`' ? /`+/g : /~+/g) ?? []
    const fenceLength = Math.max(3, ...runs.map((run: string) => run.length + 1))
    const fence = markerCharacter.repeat(fenceLength)
    return `${fence}mermaid\n${source}\n${fence}`
  },

  addNodeView() {
    return MermaidNodeView
  },
})

export async function renderMermaidInHtml(html: string, theme?: MermaidThemeName): Promise<string> {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  const placeholders = Array.from(parsed.body.querySelectorAll<HTMLElement>('.markleaf-mermaid[data-mermaid="1"]'))
  if (placeholders.length === 0) return html

  const module = await loadMermaid()
  await ensureMermaidInitialized(module, getActiveMermaidTheme(theme))
  await Promise.all(placeholders.map(async (placeholder) => {
    const source = placeholder.textContent ?? ''
      if (!source.trim()) {
      const host = parsed.createElement('div')
      host.className = 'markleaf-mermaid markleaf-mermaid-export'
      const message = parsed.createElement('div')
      message.className = 'markleaf-mermaid-message markleaf-mermaid-message-empty'
      message.textContent = mermaidStrings.mermaidEmpty
      host.append(message)
      placeholder.replaceWith(host)
      return
    }

    try {
      const id = nextMermaidId('markleaf-export-mermaid')
      const { svg } = await withTimeout(module.default.render(id, source), MERMAID_RENDER_TIMEOUT_MS)
      const host = parsed.createElement('div')
      host.className = 'markleaf-mermaid markleaf-mermaid-export'
      host.innerHTML = svg
      normalizeMermaidSvg(host)
      placeholder.replaceWith(host)
    } catch (error) {
      cleanupMermaidErrorArtifacts(parsed)
      const host = parsed.createElement('div')
      host.className = 'markleaf-mermaid markleaf-mermaid-export'
      const message = parsed.createElement('div')
      message.className = 'markleaf-mermaid-message markleaf-mermaid-message-error'
      message.textContent = error instanceof MermaidRenderTimeoutError
        ? mermaidStrings.mermaidTimeout
        : mermaidStrings.mermaidError
      host.append(message)
      placeholder.replaceWith(host)
    }
  }))

  return parsed.body.innerHTML
}
