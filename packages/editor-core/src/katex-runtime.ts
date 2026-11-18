type KatexLazyModule = typeof import('./katex-lazy')

type PendingMathRender = {
  latex: string
  displayMode: boolean
}

const pendingRenders = new Map<HTMLElement, PendingMathRender>()
let katexLoader: Promise<KatexLazyModule> | null = null
const visible = new Set<HTMLElement>()
let observer: IntersectionObserver | undefined
let frame = 0
let loading = false
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

function renderNext(module: KatexLazyModule, element: HTMLElement): void {
  const request = pendingRenders.get(element)
  pendingRenders.delete(element)
  visible.delete(element)
  observer?.unobserve(element)
  if (!request || !element.isConnected) return
  try {
    module.default.render(request.latex, element, {
      displayMode: request.displayMode, throwOnError: true, strict: false, trust: false,
    })
  } catch {
    element.textContent = request.latex
  }
}

function renderSlice(module: KatexLazyModule): void {
  const deadline = performance.now() + 7
  do {
    const element = visible.values().next().value ?? pendingRenders.keys().next().value
    if (!element) return
    renderNext(module, element)
  } while (performance.now() < deadline)
}

function schedule(): void {
  if (frame || !loadedKatex || !pendingRenders.size) return
  frame = requestAnimationFrame(() => {
    frame = 0
    renderSlice(loadedKatex!)
    schedule()
  })
}

/// Keep the source readable while the lazy renderer prepares visible formulas first.
export function renderMathElement(element: HTMLElement, latex: string, displayMode: boolean): void {
  element.textContent = latex
  pendingRenders.set(element, { latex, displayMode })
  if (typeof IntersectionObserver !== 'undefined') {
    observer ??= new IntersectionObserver(entries => {
      for (const entry of entries) {
        const target = entry.target as HTMLElement
        if (entry.isIntersecting && pendingRenders.has(target)) visible.add(target)
        else visible.delete(target)
      }
    })
    observer.observe(element)
  }
  if (loadedKatex) { schedule(); return }
  if (loading) return
  loading = true
  void loadKatex().then(module => {
    loadedKatex = module
    schedule()
  }, () => {
    observer?.disconnect()
    visible.clear()
    pendingRenders.clear()
  }).finally(() => { loading = false })
}

export async function preloadMathRenderer(): Promise<void> {
  loadedKatex = await loadKatex()
  while (pendingRenders.size) {
    renderSlice(loadedKatex)
    if (pendingRenders.size) await new Promise(resolve => setTimeout(resolve, 0))
  }
}
