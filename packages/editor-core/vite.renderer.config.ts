import { defineConfig } from 'vite'
import { katexSelfContainedCss, katexWoff2Only } from './build/index'

export default defineConfig({
  plugins: [katexWoff2Only(), katexSelfContainedCss()],
  build: {
    target: 'es2022',
    outDir: 'dist/renderer',
    lib: { entry: 'src/renderer-entry.ts', formats: ['es'], fileName: () => 'editor-core.js', cssFileName: 'editor-core' },
    sourcemap: false,
  },
})
