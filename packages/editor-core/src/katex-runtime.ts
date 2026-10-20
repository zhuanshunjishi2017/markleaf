type KatexLazyModule = typeof import('./katex-lazy')

type PendingMathRender = {
  latex: string
  displayMode: boolean
}

const pendingRenders = new Map<HTMLElement, PendingMathRender>()
let katexLoader: Promise<KatexLazyModule> | null = null
let loadedKatex: KatexLazyModule | null = null

function loadKatex(): Promise<KatexLazyModule> {
  if (!katexLoader) {
    katexLoader = import('./katex-lazy').catch(error => {
      katexLoader = null
      throw error
    })
  }
  return katexLoader
}

export function renderMathToString(latex: string, displayMode = false): string | null {
  if (!loadedKatex) return null
  try {
    return loadedKatex.default.renderToString(latex, {
      displayMode,
      throwOnError: true,
      strict: false,
      trust: false,
    })
  } catch {
    return null
  }
}

function flush(module: KatexLazyModule): void {
  for (const [element, request] of pendingRenders) {
    pendingRenders.delete(element)
    if (!element.isConnected) continue
    try {
      module.default.render(request.latex, element, {
        displayMode: request.displayMode,
        throwOnError: true,
        strict: false,
        trust: false,
      })
    } catch {
      element.textContent = request.latex
    }
  }
}

/// Math is not required to make the editor usable. Show the LaTeX source first,
/// then replace it when the lazy renderer has finished loading.
export function renderMathElement(element: HTMLElement, latex: string, displayMode: boolean): void {
  element.textContent = latex
  pendingRenders.set(element, { latex, displayMode })
  loadKatex().then(module => {
    loadedKatex = module
    flush(module)
  }, () => {
    if (pendingRenders.get(element)?.latex === latex) {
      pendingRenders.delete(element)
      element.textContent = latex
    }
  })
}

export async function preloadMathRenderer(): Promise<void> {
  const katex = await loadKatex()
  loadedKatex = katex
  flush(katex)
}
