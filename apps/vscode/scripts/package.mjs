import { copyFile, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseArgs } from 'node:util'
import { createVSIX, listFiles, PackageManager } from '@vscode/vsce'

const { values } = parseArgs({
  options: {
    publisher: { type: 'string' },
    out: { type: 'string', short: 'o' },
    help: { type: 'boolean', short: 'h' },
  },
})

if (values.help) {
  console.log('Usage: pnpm package [--publisher <id>] [--out <path>]\n'
    + 'Defaults to package.json publisher; overrides add a publisher suffix to the VSIX filename.\n'
    + '--out is relative to the extension directory, or an absolute path.')
} else {
  const root = fileURLToPath(new URL('../', import.meta.url))
  const manifest = JSON.parse(await readFile(join(root, 'package.json'), 'utf8'))
  const publisher = values.publisher ?? manifest.publisher
  if (!publisher.trim()) throw new Error('--publisher must not be empty')
  const suffix = publisher === manifest.publisher ? '' : `-${publisher}`
  const output = values.out
    ? resolve(root, values.out)
    : resolve(root, '../../artifacts', `markleaf-vscode-${manifest.version}${suffix}.vsix`)
  const files = await listFiles({ cwd: root, packageManager: PackageManager.None })
  const staging = await mkdtemp(join(tmpdir(), 'markleaf-vscode-package-'))
  try {
    // Use VSCE's existing inclusion rules, and never rewrite the source manifest.
    for (const file of files) {
      const target = join(staging, file)
      await mkdir(dirname(target), { recursive: true })
      await copyFile(join(root, file), target)
    }
    await copyFile(join(root, '.vscodeignore'), join(staging, '.vscodeignore'))
    await writeFile(join(staging, 'package.json'), JSON.stringify({ ...manifest, publisher }, null, 2) + '\n')
    const packaged = join(staging, 'extension.vsix')
    await createVSIX({
      cwd: staging,
      packagePath: packaged,
      dependencies: false,
      skipLicense: true,
      baseContentUrl: `${manifest.repository.url.replace(/\.git$/, '')}/blob/HEAD/apps/vscode`,
    })
    await mkdir(dirname(output), { recursive: true })
    await copyFile(packaged, output)
    console.log(`Packaged ${publisher}.${manifest.name}@${manifest.version}: ${output}`)
  } finally {
    await rm(staging, { recursive: true, force: true })
  }
}
