export type ExportPaginationOptions = {
  keepTablesTogether: boolean
  keepHeadingsWithNextBlock: boolean
}

export const exportPaginationCss = `
.markleaf-document .markleaf-keep-together,
.markleaf-document .markleaf-heading-with-next {
  break-inside: avoid-page !important;
  page-break-inside: avoid !important;
}

.markleaf-document .markleaf-heading-with-next > h1,
.markleaf-document .markleaf-heading-with-next > h2,
.markleaf-document .markleaf-heading-with-next > h3,
.markleaf-document .markleaf-heading-with-next > h4,
.markleaf-document .markleaf-heading-with-next > h5,
.markleaf-document .markleaf-heading-with-next > h6 {
  break-after: avoid-page !important;
  page-break-after: avoid !important;
}
`

const headingSelector = 'h1, h2, h3, h4, h5, h6'

export function applyExportPagination(
  html: string,
  options: ExportPaginationOptions,
): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html')

  if (options.keepTablesTogether) {
    for (const table of Array.from(parsed.body.querySelectorAll('table'))) {
      const figure = table.parentElement?.matches('figure.markleaf-figure')
        ? table.parentElement
        : null
      ;(figure ?? table).classList.add('markleaf-keep-together')
    }
  }

  if (options.keepHeadingsWithNextBlock) {
    for (const heading of Array.from(parsed.body.querySelectorAll(headingSelector))) {
      const next = heading.nextElementSibling
      const parent = heading.parentElement
      if (!next || !parent || next.matches(headingSelector)) continue

      const group = parsed.createElement('div')
      group.className = 'markleaf-heading-with-next'
      parent.insertBefore(group, heading)
      group.append(heading, next)
    }
  }

  return parsed.body.innerHTML.replace(/\u2060/g, '')
}
