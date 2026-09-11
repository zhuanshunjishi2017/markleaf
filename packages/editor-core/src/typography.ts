import type { MermaidThemeName } from './mermaid'

export type TypographyStyle = { id: string; css: string; dependsOn?: string }

export function resolveTypographyStyle(id: string, catalog: readonly TypographyStyle[]): { rootClass: string; css: string } {
  const classes: string[] = []
  const parts: string[] = []
  const seen = new Set<string>()
  function visit(name: string): void {
    if (seen.has(name)) return
    const style = catalog.find(entry => entry.id === name)
    if (!style) return
    seen.add(name)
    const dependency = style.dependsOn ?? /@depends:\s*([\w-]+)/.exec(style.css)?.[1]
    if (dependency) visit(dependency)
    if (style.css.trim()) { classes.push(`markleaf-style-${name}`); parts.push(style.css) }
  }
  visit(id)
  return { rootClass: classes.join(' '), css: parts.join('\n') }
}

export function resolveMermaidTheme(css: string): MermaidThemeName | undefined {
  return Array.from(css.matchAll(/--ml-mermaid-theme\s*:\s*(default|dark|forest|neutral|base)\s*;/gi)).at(-1)?.[1]?.toLowerCase() as MermaidThemeName | undefined
}
