import { build } from 'esbuild'
import { copyFile, mkdir } from 'node:fs/promises'

await build({
  entryPoints: ['src/extension.ts'],
  outfile: 'dist/extension.js',
  bundle: true,
  platform: 'node',
  format: 'cjs',
  target: 'node20',
  external: ['vscode'],
})
await copyFile('../../LICENSE', 'dist/LICENSE')
await copyFile('../../THIRD-PARTY-NOTICES.md', 'dist/THIRD-PARTY-NOTICES.md')
await mkdir('../../artifacts', { recursive: true })
