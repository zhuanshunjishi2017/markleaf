import { build } from 'esbuild'
import { copyFile, mkdir, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises'
import { dirname, resolve, join } from 'node:path'

const result = await build({
  entryPoints: ['src/extension.ts'],
  outfile: 'dist/extension.js',
  bundle: true,
  metafile: true,
  platform: 'node',
  format: 'cjs',
  target: 'node20',
  external: ['vscode'],
  plugins: [{ name: 'shared-document-kernel', setup(builder) {
    builder.onResolve({ filter: /^@markleaf\/editor-core\/document$/ }, () => ({ path: './DocumentCore/document-kernel.cjs', external: true }))
  } }],
})
await mkdir('dist/DocumentCore', { recursive: true })
await copyFile('../../packages/editor-core/dist/document-kernel.cjs', 'dist/DocumentCore/document-kernel.cjs')
await copyFile('../../packages/editor-core/dist/document-kernel-LICENSES.txt', 'dist/DocumentCore/document-kernel-LICENSES.txt')
await copyFile('../../LICENSE', 'dist/LICENSE')
await copyFile('../../THIRD-PARTY-NOTICES.md', 'dist/THIRD-PARTY-NOTICES.md')
await mkdir('../../artifacts', { recursive: true })

// Carry the licenses of runtime packages actually included in the bundle.
const packages = new Map()
for (const input of Object.keys(result.metafile.inputs).filter(path => path.includes('node_modules/'))) {
  let directory = dirname(resolve(input))
  while (directory !== dirname(directory)) {
    try {
      const manifest = JSON.parse(await readFile(join(directory, 'package.json'), 'utf8'))
      if (manifest.name && manifest.version) { packages.set(directory, manifest); break }
    } catch (error) { if (error.code !== 'ENOENT') throw error }
    directory = dirname(directory)
  }
}
const notices = []
await rm('dist/export-licenses', { recursive: true, force: true })
for (const [directory, manifest] of packages) {
  const files = (await readdir(directory)).filter(name => /^(licen[sc]e|copying|notice)([.-]|$)/i.test(name))
  const target = join('dist/export-licenses', `${manifest.name.replaceAll('/', '__')}-${manifest.version}`)
  await mkdir(target, { recursive: true })
  for (const file of files) if ((await stat(join(directory, file))).isFile()) await copyFile(join(directory, file), join(target, file))
  // These npm distributions omit the monorepo's Apache license file.
  if (manifest.name === 'puppeteer-core' || manifest.name === '@puppeteer/browsers') {
    await copyFile('licenses/puppeteer-LICENSE', join(target, 'LICENSE'))
  }
  notices.push(`${manifest.name}@${manifest.version}: ${manifest.license ?? 'see package license'}`)
}
await writeFile('dist/export-licenses/INDEX.txt', notices.sort().join('\n') + '\n')
