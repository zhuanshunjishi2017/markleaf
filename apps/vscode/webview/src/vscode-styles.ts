import { resolveTypographyStyle } from '@markleaf/editor-core'
// Keep the original @depends metadata: CSS processing would remove these
// comments in production. These styles contain no external asset URLs.
const STYLES_GLOB = '../../../../packages/styles/'

export const styles = import.meta.glob('../../../../packages/styles/*.css', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

export function resolveTypography(id: string): { classes: string[]; css: string } {
  const catalog = Object.entries(styles).map(([path, css]) => ({ id: path.slice(STYLES_GLOB.length, -4), css }))
  const resolved = resolveTypographyStyle(id, catalog)
  return { classes: resolved.rootClass.split(' ').filter(Boolean), css: resolved.css }
}

/** 颜色主题与排版样式的 glob key 前缀，供调用方按名取用。 */
export const stylesGlobPrefix = STYLES_GLOB
