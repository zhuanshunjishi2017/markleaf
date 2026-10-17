export type MermaidImageFormat = 'png' | 'svg'
export type MermaidImageExport = {
  mediaType: 'image/png' | 'image/svg+xml'
  fileExtension: MermaidImageFormat
  base64: string
  width: number
  height: number
}

const presentation = ['color', 'fill', 'fill-opacity', 'stroke', 'stroke-width', 'stroke-dasharray',
  'stroke-linecap', 'stroke-linejoin', 'stroke-opacity', 'opacity', 'font-family', 'font-size',
  'font-weight', 'font-style', 'text-anchor', 'dominant-baseline', 'text-decoration', 'white-space',
  'background-color', 'line-height', 'letter-spacing', 'display', 'visibility']

function base64(bytes: Uint8Array): string {
  let text = ''
  for (let i = 0; i < bytes.length; i += 8192) text += String.fromCharCode(...bytes.subarray(i, i + 8192))
  return btoa(text)
}

function background(element: Element): string {
  for (let current: Element | null = element; current; current = current.parentElement) {
    const color = getComputedStyle(current).backgroundColor
    if (color && color !== 'transparent' && !/rgba\([^)]*,\s*0\s*\)/.test(color)) return color
  }
  return getComputedStyle(document.documentElement).getPropertyValue('--bg-primary').trim() || '#ffffff'
}

export async function exportMermaidImage(element: Element, format: MermaidImageFormat): Promise<MermaidImageExport> {
  if (format !== 'png' && format !== 'svg') throw new Error('Unsupported image format')
  await document.fonts?.ready
  const original = element.querySelector<SVGSVGElement>('.markleaf-mermaid-view svg')
    ?? (element.matches('svg') ? element as SVGSVGElement : null)
  if (!original || element.querySelector('.markleaf-mermaid-message')) throw new Error('Diagram has not rendered successfully')
  const viewBox = original.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number)
  if (!viewBox || viewBox.length !== 4 || viewBox.some(n => !Number.isFinite(n))
    || viewBox[2]! <= 0 || viewBox[3]! <= 0) throw new Error('Diagram has no valid full image bounds')
  const width = Math.ceil(viewBox[2]!), height = Math.ceil(viewBox[3]!)
  const svg = original.cloneNode(true) as SVGSVGElement
  const sources = [original, ...original.querySelectorAll('*')]
  const copies = [svg, ...svg.querySelectorAll('*')]
  for (let i = 0; i < copies.length; i++) {
    const copy = copies[i]!, source = sources[i]!
    const css = getComputedStyle(source)
    for (const property of presentation) {
      const value = css.getPropertyValue(property)
      if (value) (copy as HTMLElement | SVGElement).style.setProperty(property, value.replace(/url\(["']?[^)"']*#([^)'" ]+)["']?\)/g, 'url(#$1)'))
    }
    for (const attribute of [...copy.attributes]) {
      if (attribute.name.startsWith('on') || attribute.name.startsWith('data-')) copy.removeAttribute(attribute.name)
      if (['href', 'xlink:href', 'src'].includes(attribute.name) && !attribute.value.startsWith('#') && !attribute.value.startsWith('data:')) {
        if (copy.localName === 'a') copy.removeAttribute(attribute.name)
        else throw new Error('Diagram contains an external resource that cannot be exported independently')
      }
    }
  }
  svg.querySelectorAll('script, button, .markleaf-format-preview, .markleaf-source-editor').forEach(node => node.remove())
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  svg.setAttribute('width', String(width)); svg.setAttribute('height', String(height))
  svg.style.removeProperty('max-width')
  const surface = document.createElementNS('http://www.w3.org/2000/svg', 'rect')
  surface.setAttribute('x', String(viewBox[0])); surface.setAttribute('y', String(viewBox[1]))
  surface.setAttribute('width', String(viewBox[2])); surface.setAttribute('height', String(viewBox[3]))
  surface.setAttribute('fill', background(element))
  svg.insertBefore(surface, svg.firstChild)
  const encoded = base64(new TextEncoder().encode(new XMLSerializer().serializeToString(svg)))
  if (format === 'svg') return { mediaType: 'image/svg+xml', fileExtension: 'svg', base64: encoded, width, height }
  const outputWidth = width * 2, outputHeight = height * 2
  // Refuse unsafe allocations rather than silently clipping or downscaling.
  if (outputWidth > 32767 || outputHeight > 32767 || outputWidth * outputHeight > 64 * 1024 * 1024) throw new Error('Diagram exceeds PNG canvas limits; use SVG')
  const image = new Image()
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Image decoding timed out')), 15_000)
    image.onload = () => { clearTimeout(timer); resolve() }
    image.onerror = () => { clearTimeout(timer); reject(new Error('Cannot decode diagram image')) }
    // A data URL also avoids foreignObject SVG blob-origin canvas tainting.
    image.src = `data:image/svg+xml;base64,${encoded}`
  })
  const canvas = document.createElement('canvas')
  canvas.width = outputWidth; canvas.height = outputHeight
  const context = canvas.getContext('2d')
  if (!context) throw new Error('PNG canvas unavailable')
  context.drawImage(image, 0, 0, outputWidth, outputHeight)
  const url = canvas.toDataURL('image/png')
  canvas.width = 0; canvas.height = 0
  if (!url.startsWith('data:image/png;base64,')) throw new Error('PNG encoding failed; use SVG')
  return { mediaType: 'image/png', fileExtension: 'png', base64: url.split(',')[1]!, width: outputWidth, height: outputHeight }
}
