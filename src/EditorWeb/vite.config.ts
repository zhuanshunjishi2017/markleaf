import { defineConfig } from 'vitest/config'

export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: true,
    chunkSizeWarningLimit: 550,
  },
  test: {
    environment: 'jsdom',
  },
})
