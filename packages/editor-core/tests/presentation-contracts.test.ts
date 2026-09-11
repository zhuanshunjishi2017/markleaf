import { afterEach, expect, it } from 'vitest'
import { assignHeadingAnchors } from '../src/outline'
import { resolveTypographyStyle } from '../src/typography'
import { getImageResourcePath, refreshImageResources, setImageResourceResolver } from '../src/image-resources'

afterEach(() => { setImageResourceResolver(); document.body.innerHTML = '' })

it('assigns nonempty unique anchors when titles and generated suffixes collide', () => {
  document.body.innerHTML = '<h1>Title</h1><h2>Title</h2><h3>Title-1</h3><h6>???</h6>'
  assignHeadingAnchors(document.body)
  expect([...document.querySelectorAll('h1,h2,h3,h6')].map(node => node.id)).toEqual(['title', 'title-1', 'title-1-1', 'heading'])
})

it('refreshes resolved image URLs without replacing original Markdown paths', () => {
  document.body.innerHTML = '<img data-markleaf-path="./图片.png" src="">'
  const image = document.querySelector('img')!
  setImageResourceResolver({ resolve: path => `https://resource.invalid/${encodeURIComponent(path)}` })
  refreshImageResources(document.body)
  expect(image.src).toContain('https://resource.invalid/')
  expect(getImageResourcePath(image)).toBe('./图片.png')
  setImageResourceResolver({ resolve: () => 'blob:new-resource' })
  refreshImageResources(document.body)
  expect(image.src).toBe('blob:new-resource')
  expect(getImageResourcePath(image)).toBe('./图片.png')
})

it('resolves parent typography before child typography and includes a cycle only once', () => {
  const result = resolveTypographyStyle('child', [
    { id: 'child', css: '/* @depends: parent */ .child {}' },
    { id: 'parent', css: '.parent {}', dependsOn: 'child' },
  ])
  expect(result.rootClass).toBe('markleaf-style-parent markleaf-style-child')
  expect(result.css).toBe('.parent {}\n/* @depends: parent */ .child {}')
})
