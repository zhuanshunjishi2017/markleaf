import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

function styleRules(fileName: string): CSSStyleRule[] {
  const css = readFileSync(resolve(import.meta.dirname, `../../styles/${fileName}`), 'utf8')
  const style = document.createElement('style')
  style.textContent = css
  document.head.append(style)
  return Array.from(style.sheet?.cssRules ?? []).filter(
    (rule): rule is CSSStyleRule => rule instanceof CSSStyleRule,
  )
}

function fontFamilyFor(rules: CSSStyleRule[], selectorFragment: string): string {
  return rules
    .filter(rule => rule.selectorText.split(',').map(selector => selector.trim()).includes(selectorFragment))
    .map(rule => rule.style.fontFamily)
    .filter(Boolean)
    .join(' | ')
}

describe('optional font family compatibility', () => {
  it('uses the macOS LXGW family name before localized notebook fallbacks', () => {
    const rules = styleRules('notebook.css')
    const family = fontFamilyFor(rules, '.markleaf-style-notebook .markleaf-document')

    expect(family).toContain('"LXGW WenKai"')
    expect(family.indexOf('"LXGW WenKai"')).toBeLessThan(family.indexOf('"霞鹜文楷"'))
    expect(fontFamilyFor(rules, '.markleaf-style-notebook .markleaf-document code')).toContain('"LXGW WenKai Mono"')
  })

  it('maps every retro-print role to the PostScript families carried by the audited pack', () => {
    const rules = styleRules('retro-print.css')

    expect(fontFamilyFor(rules, '.markleaf-style-retro-print .markleaf-document')).toContain('Huiwen-mincho')
    expect(fontFamilyFor(rules, '.markleaf-style-retro-print .markleaf-document h1')).toContain('ZhaohuaMinA')
    expect(fontFamilyFor(rules, '.markleaf-style-retro-print .markleaf-document h2')).toContain('Huiwen-HKHei')
    expect(fontFamilyFor(rules, '.markleaf-style-retro-print .markleaf-document em')).toContain('Huiwen-ZhengKai')
    expect(fontFamilyFor(rules, '.markleaf-style-retro-print .markleaf-document blockquote p')).toContain('Huiwen-Fangsong')
    expect(fontFamilyFor(rules, '.markleaf-style-retro-print .markleaf-document code')).toContain('ZhaohuaTypeWriter')
  })
})
