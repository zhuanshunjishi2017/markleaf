import { defineConfig } from 'vitest/config'
import type { Plugin } from 'vite'
import { createRequire } from 'node:module'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

const require = createRequire(import.meta.url)
const katexCssPath = require.resolve('katex/dist/katex.min.css')
const katexFontsDir = join(dirname(katexCssPath), 'fonts')

// 仅保留 KaTeX 的 woff2 字体，剔除 woff/ttf，避免字体文件膨胀。
// WebView2 基于 Chromium，原生支持 woff2，无需其他格式回退。
function katexWoff2Only(): Plugin {
  return {
    name: 'katex-woff2-only',
    enforce: 'pre',
    transform(code, id) {
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
// 供导出 HTML/PDF 使用（导出文档由独立 WebView2 加载，无法访问编辑器资源）。
function katexSelfContainedCss(): Plugin {
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
    resolveId(id) {
      if (id === 'virtual:katex-css') return '\0virtual:katex-css'
      return null
    },
    load(id) {
      if (id === '\0virtual:katex-css') {
        css ??= generate()
        return `export default ${JSON.stringify(css)}`
      }
      return null
    },
  }
}

export default defineConfig({
  base: './',
  plugins: [katexWoff2Only(), katexSelfContainedCss()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: true,
    chunkSizeWarningLimit: 550,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
  },
})
