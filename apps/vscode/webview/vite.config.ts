import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'
import { katexWoff2Only, katexSelfContainedCss, sharedRendererDistribution } from '@markleaf/editor-core/build'

// 产物写入扩展的 dist/webview，由 webviewHtml() 经 .vite/manifest.json 读取。
export default defineConfig(({ mode }) => ({
  base: './',
  plugins: mode === 'test' ? [katexWoff2Only(), katexSelfContainedCss()] : [sharedRendererDistribution()],
  build: {
    outDir: '../dist/webview',
    emptyOutDir: true,
    // 发布包不携带调试映射文件；开发调试可通过临时覆盖此项开启。
    sourcemap: false,
    chunkSizeWarningLimit: 550,
    manifest: true,
    rollupOptions: { input: 'src/vscode.ts' },
  },
  test: {
    alias: [{ find: /^@markleaf\/editor-core$/, replacement: fileURLToPath(new URL('../../../packages/editor-core/src/index.ts', import.meta.url)) }, { find: 'vscode', replacement: fileURLToPath(new URL('./tests/vscode-mock.ts', import.meta.url)) }],
    // 阅读视图测试需要读取排版样式原文及其 @depends 元数据。
    css: { include: [/\/styles\/[^/]+\.css\?raw(?:$|&)/] },
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
  },
}))
