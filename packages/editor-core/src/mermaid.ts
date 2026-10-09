import { Node, mergeAttributes } from '@tiptap/core'
import { sharedEditorStrings, type SharedEditorStrings } from './shared-editor-strings'

type MermaidModule = typeof import('mermaid')

let mermaidPromise: Promise<MermaidModule> | null = null
let mermaidInitializationKey: string | null = null
let mermaidSequence = 0
const MERMAID_PREPARATION_TIMEOUT_MS = 10_000
const MERMAID_RENDER_TIMEOUT_MS = 1000
const MERMAID_RELEASE_TIMEOUT_MS = 5000
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

function sourceWithDocumentColors(source: string, themeOverride?: MermaidThemeName): string {
  const root = document.querySelector<HTMLElement>('.markleaf-document')
  if (!root || themeOverride || getActiveMermaidTheme().theme !== 'dark') return source
  const css = window.getComputedStyle(root)
  // Mermaid inherits initialize() variables even when a diagram changes theme.
  // Keep diagram-owned configuration and explicit document themes untouched.
  if (css.getPropertyValue('--ml-mermaid-theme').trim()
    || /^\s*---(?:\r?\n)/.test(source)
    || /%%\{\s*(?:init|initialize)\s*:/i.test(source)) return source

  const surface = css.getPropertyValue('--ml-mermaid-surface').trim()
  if (!surface) return source
  const probe = document.createElement('span')
  probe.hidden = true
  // Never insert measurement nodes into the editable document.
  document.body.append(probe)
  function color(name: string): string {
    probe.style.color = css.getPropertyValue(`--ml-mermaid-${name}`).trim()
    const resolved = window.getComputedStyle(probe).color
    // Chromium preserves color-mix() results as color(srgb …); Mermaid's
    // theme engine expects hex, so resolve the display color at this boundary.
    const srgb = /^color\(srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)/.exec(resolved)
    const rgb = srgb ?? /^rgba?\(\s*([\d.]+)[, ]+\s*([\d.]+)[, ]+\s*([\d.]+)/.exec(resolved)
    if (!rgb) throw new Error(`Cannot resolve Mermaid ${name} color: ${resolved}`)
    return '#' + rgb.slice(1, 4).map(value => Math.round(Number(value) * (srgb ? 255 : 1)).toString(16).padStart(2, '0')).join('')
  }
  try {
    const node = color('node'), border = color('node-border'), line = color('line'), text = color('text'), background = color('surface')
    // Override only final neutral values, not primaryColor/secondaryColor or
    // derived chart series. The per-diagram config is consumed inside Mermaid's
    // render queue, so another diagram or export cannot inherit these values.
    const config = { themeVariables: {
      mainBkg: node, nodeBkg: node, nodeBorder: border, primaryTextColor: text,
      lineColor: line, defaultLinkColor: line, textColor: text,
      edgeLabelBackground: background, clusterBkg: background, clusterBorder: border,
      actorBkg: node, actorBorder: border, actorLineColor: line, actorTextColor: text,
      signalColor: line, signalTextColor: text, labelBoxBkgColor: node,
      labelBoxBorderColor: border, labelTextColor: text, loopTextColor: text,
    } }
    return `---\nconfig: ${JSON.stringify(config)}\n---\n${source}`
  } finally {
    probe.remove()
  }
}

export function setMermaidMarkdownCodeFence(preference: 'backtick' | 'tilde'): void {
  markdownCodeFence = preference
}

export function setMermaidStrings(
  strings: Pick<SharedEditorStrings, 'mermaidEmpty' | 'mermaidError' | 'mermaidTimeout'>
    & Partial<Pick<SharedEditorStrings, 'mermaidLoading' | 'mermaidLoadError' | 'mermaidLoadTimeout' | 'mermaidBlocked' | 'mermaidRetry'>>,
): void {
  mermaidStrings = { ...mermaidStrings, ...strings }
}

type MermaidFailure = 'error' | 'timeout' | 'load-error' | 'load-timeout' | 'blocked'
class MermaidRenderError extends Error {
  constructor(readonly reason: Exclude<MermaidFailure, 'error'>, message: string, options?: ErrorOptions) {
    super(message, options)
    this.name = 'MermaidRenderError'
  }
}
type MermaidRenderResult = 'rendered' | 'empty' | 'cancelled' | MermaidFailure
let mermaidRenderQueue: Promise<void> = Promise.resolve()
let blockedRender: { released: Promise<void>; deadline: number } | undefined

async function loadMermaid(): Promise<MermaidModule> {
  if (!mermaidPromise) {
    // The renderer build keeps Mermaid and its lazy layout dependencies together:
    // a successful import prepares every supported diagram before render starts.
    const pending = import('mermaid')
    const preparation = withTimeout(pending, MERMAID_PREPARATION_TIMEOUT_MS,
      new MermaidRenderError('load-timeout', 'Mermaid module preparation timed out'))
      .catch(error => {
        if (error instanceof MermaidRenderError) throw error
        throw new MermaidRenderError('load-error', 'Could not load Mermaid', { cause: error })
      })
    mermaidPromise = preparation
    // A late load can serve a later explicit retry; it cannot overwrite a failed
    // node. Keep one shared deadline so queued nodes do not each wait 10 seconds.
    void pending.then(module => {
      if (mermaidPromise === preparation) mermaidPromise = Promise.resolve(module)
    }, () => {})
  }
  return mermaidPromise
}

function mermaidFailure(error: unknown): MermaidFailure {
  return error instanceof MermaidRenderError ? error.reason : 'error'
}

function mermaidFailureMessage(reason: MermaidFailure): string {
  switch (reason) {
    case 'timeout': return mermaidStrings.mermaidTimeout
    case 'load-error': return mermaidStrings.mermaidLoadError
    case 'load-timeout': return mermaidStrings.mermaidLoadTimeout
    case 'blocked': return mermaidStrings.mermaidBlocked
    default: return mermaidStrings.mermaidError
  }
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

function renderQueuedMermaid(source: string, settings: MermaidThemeSettings, isCurrent: () => boolean = () => true) {
  const result = mermaidRenderQueue.then(async () => {
    if (!isCurrent()) return null
    // A timed-out Promise still owns Mermaid's global renderer. Wait for that
    // owner to release, using one deadline shared by all queued editor/export
    // jobs. Never overlap it, and never label unstarted diagrams as timed out.
    const blocked = blockedRender
    if (blocked) {
      const remaining = blocked.deadline - Date.now()
      const error = new MermaidRenderError('blocked', 'Previous Mermaid render has not finished; this diagram was not started')
      if (remaining <= 0) throw error
      await withTimeout(blocked.released, remaining, error)
      if (!isCurrent()) return null
    }
    const module = await loadMermaid()
    // Once the runtime is loaded, Mermaid can complete entirely in microtasks.
    // Yield between diagrams so a long document can paint and handle input.
    await new Promise<void>(resolve => window.setTimeout(resolve, 0))
    if (!isCurrent()) return null
    await ensureMermaidInitialized(module, settings)
    const pending = module.default.render(nextMermaidId('markleaf-mermaid'), source)
    try {
      // Only the active diagram spends its rendering budget. Both editor and
      // export jobs enter here, so Mermaid's internal queue stays empty.
      return await withTimeout(pending, MERMAID_RENDER_TIMEOUT_MS,
        new MermaidRenderError('timeout', 'Mermaid diagram rendering timed out'))
    } catch (error) {
      if (error instanceof MermaidRenderError && error.reason === 'timeout') {
        const blocked = {
          released: pending.then(() => {}, () => {}),
          deadline: Date.now() + MERMAID_RELEASE_TIMEOUT_MS,
        }
        blockedRender = blocked
        void blocked.released.then(() => { if (blockedRender === blocked) blockedRender = undefined })
      }
      throw error
    }
  })
  mermaidRenderQueue = result.then(() => {}, () => {})
  return result
}

async function renderMermaidSvgInto(
  host: HTMLElement,
  source: string,
  settings: MermaidThemeSettings,
  isCurrent: () => boolean,
): Promise<MermaidRenderResult> {
  if (!source.trim()) {
    renderMermaidMessage(host, mermaidStrings.mermaidEmpty, 'empty')
    return 'empty'
  }
  return renderQueuedMermaid(source, settings, isCurrent).then((result) => {
    if (!result || !isCurrent()) return 'cancelled' as const
    const { svg } = result
    host.innerHTML = svg
    normalizeMermaidSvg(host, settings.fontFamily)
    return 'rendered' as const
  }).catch((error: unknown) => {
    cleanupMermaidErrorArtifacts()
    if (!isCurrent()) return 'cancelled' as const
    return mermaidFailure(error)
  })
}

function withTimeout<T>(promise: Promise<T>, timeoutMs: number, error: Error): Promise<T> {
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(
      () => reject(error),
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

function renderMermaidMessage(host: HTMLElement, text: string, kind: 'empty' | 'error' | 'loading', retry?: () => void): void {
  const message = document.createElement('div')
  message.className = `markleaf-mermaid-message markleaf-mermaid-message-${kind}`
  message.textContent = text
  if (retry) {
    const button = document.createElement('button')
    button.type = 'button'
    button.className = 'markleaf-mermaid-retry'
    button.textContent = mermaidStrings.mermaidRetry
    button.addEventListener('click', retry)
    message.append(button)
  }
  host.replaceChildren(message)
}

function normalizeMermaidSvg(root: ParentNode, fontFamily = getActiveMermaidFontFamily()): void {
  root.querySelectorAll<HTMLElement | SVGElement>('svg, svg *, foreignObject, foreignObject *')
    .forEach((element) => {
      ;(element as HTMLElement | SVGElement).style.textIndent = '0px'
      // The shared style sheet intentionally uses !important so diagram text
      // does not inherit unrelated node rules. Match that priority here, or
      // the browser can render with a different font than Mermaid measured.
      ;(element as HTMLElement | SVGElement).style.setProperty(
        'font-family',
        fontFamily,
        'important',
      )
    })
}

const MermaidNodeView = ({ node }: { node: { type?: { name: string }; textContent: string } }) => {
  const wrapper = document.createElement('div')
  wrapper.className = 'markleaf-mermaid'
  wrapper.contentEditable = 'false'

  let lastSource = node.textContent
  let scheduled = false
  let destroyed = false
  let generation = 0
  let pendingKey: string | undefined
  const section = document.createElement('div')
  section.className = 'markleaf-mermaid-view'
  wrapper.append(section)

  const renderCurrent = () => {
    if (scheduled || destroyed) return
    scheduled = true
    // Hosts finish applying typography before the initial diagram starts.
    // Same-turn invalidations become one request with the final settings.
    queueMicrotask(() => {
      scheduled = false
      if (destroyed) return
      let settings: MermaidThemeSettings
      let source: string
      try {
        settings = getActiveMermaidTheme()
        source = sourceWithDocumentColors(lastSource)
      } catch {
        // Color conversion errors still belong to this node, even though
        // preparation now runs before the asynchronous render queue.
        generation += 1
        pendingKey = undefined
        renderMermaidMessage(section, mermaidStrings.mermaidError, 'error')
        return
      }
      const key = JSON.stringify([source, settings])
      if (pendingKey === key) return
      const version = ++generation
      pendingKey = key
      const isCurrent = () => !destroyed && version === generation
      renderMermaidMessage(section, mermaidStrings.mermaidLoading, 'loading')
      void renderMermaidSvgInto(section, source, settings, isCurrent).then((result) => {
        if (!isCurrent()) return
        pendingKey = undefined
        if (result !== 'rendered' && result !== 'empty' && result !== 'cancelled') {
          renderMermaidMessage(section, mermaidFailureMessage(result), 'error',
            result === 'load-error' || result === 'error' ? undefined : renderCurrent)
        }
        // Notify only after the current result has entered the document.
        window.requestAnimationFrame(() => {
          if (isCurrent() && section.isConnected) window.dispatchEvent(new Event('markleaf-mermaid-rendered'))
        })
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
      destroyed = true
      generation += 1
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

export async function renderMermaidInHtml(html: string, theme?: MermaidThemeName, throwOnError = false): Promise<string> {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  const placeholders = Array.from(parsed.body.querySelectorAll<HTMLElement>('.markleaf-mermaid[data-mermaid="1"]'))
  if (placeholders.length === 0) return html

  const settings = getActiveMermaidTheme(theme)
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
      const result = await renderQueuedMermaid(sourceWithDocumentColors(source, theme), settings)
      if (!result) return
      const { svg } = result
      const host = parsed.createElement('div')
      host.className = 'markleaf-mermaid markleaf-mermaid-export'
      host.innerHTML = svg
      normalizeMermaidSvg(host, settings.fontFamily)
      placeholder.replaceWith(host)
    } catch (error) {
      cleanupMermaidErrorArtifacts(parsed)
      if (throwOnError) throw error
      const host = parsed.createElement('div')
      host.className = 'markleaf-mermaid markleaf-mermaid-export'
      const message = parsed.createElement('div')
      message.className = 'markleaf-mermaid-message markleaf-mermaid-message-error'
      message.textContent = mermaidFailureMessage(mermaidFailure(error))
      host.append(message)
      placeholder.replaceWith(host)
    }
  }))

  return parsed.body.innerHTML
}
