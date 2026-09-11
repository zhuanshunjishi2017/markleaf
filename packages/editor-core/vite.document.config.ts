import { defineConfig } from 'vite'
import { readFileSync } from 'node:fs'

export default defineConfig({
  plugins: [{
    name: 'document-kernel-licenses',
    generateBundle() {
      const packages = ['marked/LICENSE.md', 'entities/LICENSE', 'parse5/LICENSE']
      const source = packages.map(path => `${path}\n\n${readFileSync(new URL(`node_modules/${path}`, import.meta.url), 'utf8')}`).join('\n\n')
      this.emitFile({ type: 'asset', fileName: 'document-kernel-LICENSES.txt', source })
    },
  }],
  build: {
  target: 'es2022',
  lib: { entry: 'src/document/native-entry.ts', name: 'MarkLeafDocumentCore', formats: ['umd'], fileName: () => 'document-kernel.cjs' },
  outDir: 'dist',
  },
})
