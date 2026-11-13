import katexCss from 'virtual:katex-css'
export { default } from 'katex'

// 通过 <style> 内联注入，而不是 `?url` <link>：renderer 构建的资产 URL 以
// `/` 为 base 烤死（/katex.min.css），在 markleaf:// 等自定义 scheme 的
// 文档根下解析不到 kernel/katex.min.css → CSS 404 → KaTeX 的 .katex-html
// 按 DOM 裸排（\frac{n}{2} 显示成 "2n"、公式退化为正文字体）。
// virtual:katex-css 是自包含字符串（woff2 内联），无任何 URL 解析。
const style = document.createElement('style')
style.textContent = katexCss
document.head.append(style)
