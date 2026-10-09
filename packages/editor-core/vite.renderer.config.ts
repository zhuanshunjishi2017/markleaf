import { defineConfig } from 'vite'
import { katexSelfContainedCss, katexWoff2Only } from './build/index'

let mermaidModules: Set<string> | undefined

export default defineConfig({
  plugins: [katexWoff2Only(), katexSelfContainedCss()],
  build: {
    target: 'es2022',
    outDir: 'dist/renderer',
    lib: {
      entry: {
        'editor-core': 'src/renderer-entry.ts',
        'export-html': 'src/export-entry.ts',
      },
      formats: ['es'],
      cssFileName: 'editor-core',
    },
    sourcemap: false,
    rollupOptions: {
      output: {
        onlyExplicitManualChunks: true,
        manualChunks(id, { getModuleInfo, getModuleIds }) {
          if (!mermaidModules) {
            // Keep the editor's eager dependencies out of Mermaid's lazy chunk.
            // Collect the entire Mermaid graph, including dynamically imported
            // diagram/layout implementations, without depending on chunk hashes.
            const eager = new Set<string>()
            const collect = (moduleId: string, target: Set<string>, dynamic: boolean) => {
              if (target.has(moduleId) || (dynamic && eager.has(moduleId))) return
              const info = getModuleInfo(moduleId)
              if (!info || info.isExternal) return
              target.add(moduleId)
              for (const child of info.importedIds) collect(child, target, dynamic)
              if (dynamic) for (const child of info.dynamicallyImportedIds) collect(child, target, true)
            }
            const ids = [...getModuleIds()]
            for (const moduleId of ids) if (getModuleInfo(moduleId)?.isEntry) collect(moduleId, eager, false)
            mermaidModules = new Set<string>()
            for (const moduleId of ids) {
              if (moduleId.replaceAll('\\', '/').includes('/node_modules/mermaid/')) collect(moduleId, mermaidModules, true)
            }
          }
          return mermaidModules.has(id) ? 'mermaid-runtime' : undefined
        },
      },
    },
  },
})
