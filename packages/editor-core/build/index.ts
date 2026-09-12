import { createRequire } from 'node:module'
import { cpSync, existsSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const katexCssPath = require.resolve('katex/dist/katex.min.css')
const katexFontsDir = join(dirname(katexCssPath), 'fonts')

// 仅保留 KaTeX 的 woff2 字体，剔除 woff/ttf，避免字体文件膨胀。
// 支持的 WKWebView、WebView2 和 VS Code Webview 均支持 woff2。
export function katexWoff2Only() {
  return {
    name: 'katex-woff2-only',
    enforce: 'pre' as const,
    transform(code: string, id: string) {
      if (!id.includes('katex') || !id.includes('.css')) {
        return null
      }

      const stripped = code
        .replace(/,\s*url\([^)]*\.woff\)\s*format\(["']woff["']\)/g, '')
        .replace(/,\s*url\([^)]*\.ttf\)\s*format\(["']truetype["']\)/g, '')

      return { code: stripped, map: null }
    },
  }
}

// 生成自包含的 KaTeX CSS：剔除 woff/ttf，将 woff2 字体内联为 base64，
// 供导出 HTML/PDF 使用（导出文档独立加载，无法访问编辑器资源）。
export function katexSelfContainedCss() {
  let css: string | null = null

  function generate(): string {
    let raw = readFileSync(katexCssPath, 'utf8')
    raw = raw
      .replace(/,\s*url\([^)]*\.woff\)\s*format\(["']woff["']\)/g, '')
      .replace(/,\s*url\([^)]*\.ttf\)\s*format\(["']truetype["']\)/g, '')
    raw = raw.replace(/url\(fonts\/([^)]*\.woff2)\)/g, (_, filename: string) => {
      const base64 = readFileSync(join(katexFontsDir, filename)).toString('base64')
      return `url(data:font/woff2;base64,${base64})`
    })
    return raw
  }

  return {
    name: 'katex-self-contained-css',
    resolveId(id: string) {
      if (id === 'virtual:katex-css') return '\0virtual:katex-css'
      return null
    },
    load(id: string) {
      if (id === '\0virtual:katex-css') {
        css ??= generate()
        return `export default ${JSON.stringify(css)}`
      }
      return null
    },
  }
}
/** Products copy the completed renderer distribution; they never compile its source. */
export function sharedRendererDistribution() {
  const source = fileURLToPath(new URL('../dist/renderer', import.meta.url))
  let destination = ''
  return {
    name: 'markleaf-shared-renderer-distribution',
    apply: 'build' as const,
    config() {
      return { build: { rollupOptions: {
        external: ['@markleaf/editor-core'],
        output: { paths: { '@markleaf/editor-core': '../kernel/editor-core.js' } },
      } } }
    },
    configResolved(config: { root: string; build: { outDir: string } }) {
      destination = resolve(config.root, config.build.outDir, 'kernel')
    },
    buildStart() {
      if (!existsSync(resolve(source, 'editor-core.js'))) throw new Error('Shared renderer is missing. Run build:editor-web or packages/editor-core build:renderer first.')
    },
    resolveId(id: string) {
      return id === '@markleaf/editor-core/styles.css' ? '\0markleaf-prebuilt-style' : null
    },
    load(id: string) {
      return id === '\0markleaf-prebuilt-style' ? '' : null
    },
    transformIndexHtml() {
      return [{ tag: 'link', attrs: { rel: 'stylesheet', href: './kernel/editor-core.css' }, injectTo: 'head' as const }]
    },
    closeBundle() { cpSync(source, destination, { recursive: true }) },
  }
}
