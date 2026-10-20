import katexCssUrl from 'katex/dist/katex.min.css?url'
export { default } from 'katex'

const stylesheet = document.createElement('link')
stylesheet.rel = 'stylesheet'
stylesheet.href = katexCssUrl
document.head.append(stylesheet)
