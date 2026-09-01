import { describe, expect, it } from 'vitest'
import { applyExportPagination, exportPaginationCss } from '../src/export-pagination'

function body(html: string): HTMLElement {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  return parsed.body
}

describe('export pagination', () => {
  it('keeps plain tables together', () => {
    const root = body('<table><tr><td>A</td></tr></table>')
    const html = applyExportPagination(root.innerHTML, {
      keepTablesTogether: true,
      keepHeadingsWithNextBlock: false,
    })

    expect(body(html).querySelector('table')?.classList.contains('markleaf-keep-together')).toBe(true)
  })

  it('keeps a captioned table through its figure', () => {
    const html = applyExportPagination(
      '<figure class="markleaf-figure"><table><tr><td>A</td></tr></table></figure>',
      { keepTablesTogether: true, keepHeadingsWithNextBlock: false },
    )

    expect(body(html).querySelector('figure')?.classList.contains('markleaf-keep-together')).toBe(true)
  })

  it('groups a heading with its following block', () => {
    const html = applyExportPagination(
      '<h2>Results</h2><p>Text</p>',
      { keepTablesTogether: false, keepHeadingsWithNextBlock: true },
    )
    const root = body(html)

    expect(root.querySelector('.markleaf-heading-with-next')).not.toBeNull()
    expect(root.querySelector('.markleaf-heading-with-next')?.children).toHaveLength(2)
  })

  it('does not group a heading before another heading', () => {
    const html = applyExportPagination(
      '<h2>One</h2><h3>Two</h3>',
      { keepTablesTogether: false, keepHeadingsWithNextBlock: true },
    )

    expect(body(html).querySelector('.markleaf-heading-with-next')).toBeNull()
  })

  it('does not group a heading at the end of the document', () => {
    const html = applyExportPagination(
      '<p>Before</p><h2>End</h2>',
      { keepTablesTogether: false, keepHeadingsWithNextBlock: true },
    )

    expect(body(html).querySelector('.markleaf-heading-with-next')).toBeNull()
  })

  it('leaves content unchanged when pagination behavior is disabled', () => {
    const html = applyExportPagination(
      '<h2>Results</h2><p>Text</p><table><tr><td>A</td></tr></table>',
      { keepTablesTogether: false, keepHeadingsWithNextBlock: false },
    )
    const root = body(html)

    expect(root.querySelector('.markleaf-keep-together')).toBeNull()
    expect(root.querySelector('.markleaf-heading-with-next')).toBeNull()
  })

  it('exposes shared print pagination CSS', () => {
    expect(exportPaginationCss).toContain('.markleaf-keep-together')
    expect(exportPaginationCss).toContain('break-inside: avoid-page')
  })
})
