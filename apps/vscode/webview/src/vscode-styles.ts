// Keep the original @depends metadata: CSS processing would remove these
// comments in production. These styles contain no external asset URLs.
const STYLES_GLOB = '../../../../packages/styles/'

export const styles = import.meta.glob('../../../../packages/styles/*.css', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

export function resolveTypography(id: string): { classes: string[]; css: string } {
  const classes: string[] = []
  const parts: string[] = []
  const seen = new Set<string>()
  function visit(name: string): void {
    if (seen.has(name)) return
    seen.add(name)
    const css = styles[`${STYLES_GLOB}${name}.css`]
    if (!css) return
    const dependency = /@depends:\s*([\w-]+)/.exec(css)?.[1]
    if (dependency) visit(dependency)
    classes.push(`markleaf-style-${name}`)
    parts.push(css)
  }
  visit(id)
  return { classes, css: parts.join('\n') }
}

/** 颜色主题与排版样式的 glob key 前缀，供调用方按名取用。 */
export const stylesGlobPrefix = STYLES_GLOB
