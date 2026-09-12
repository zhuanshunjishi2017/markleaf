/** A document stores Markdown paths; the host supplies loadable resource URLs. */
export type ImageResourceResolver = {
  resolve(path: string): string
  originalPath?(url: string): string | null
}

let resources: ImageResourceResolver | undefined

export function setImageResourceResolver(resolver?: ImageResourceResolver): void {
  resources = resolver
}

export function resolveImageResource(path: string): string {
  return resources?.resolve(path) ?? path
}

export function getImageResourcePath(element: Element): string | null {
  const path = element.getAttribute('data-markleaf-path')
  if (path) return path
  const url = element.getAttribute('src')?.trim()
  return url ? resources?.originalPath?.(url) ?? null : null
}

export function refreshImageResources(root: ParentNode): void {
  for (const image of root.querySelectorAll<HTMLImageElement>('img[data-markleaf-path]')) {
    const path = image.getAttribute('data-markleaf-path')!
    const url = resolveImageResource(path)
    if (image.getAttribute('src') !== url) image.setAttribute('src', url)
  }
}
