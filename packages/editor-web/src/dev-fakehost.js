// dev 专用 fakehost（仅 ?fakehost 时激活）：在无宿主环境（浏览器 dev 页）中
// 模拟 native bridge。分层隔离实验参数：
//   ?nothemed  关闭主题化托管选区（ThemedSelection / themed-visual-selection）
// 构建产物不含此行为：宿主环境中 chrome.webview 已存在，此处仅做叠加。
(function () {
  if (!location.search.includes('fakehost')) return
  var listeners = []
  var webview = {
    hostPlatform: 'macOS',
    _posted: [],
    addEventListener: function (t, fn) { listeners.push(fn) },
    postMessage: function (m) { this._posted.push(m) },
  }
  Object.defineProperty(window, 'chrome', { value: { webview } })
  window.__hostDeliver = function (msg) {
    listeners.forEach(function (fn) { fn({ data: msg }) })
  }
  var q = new URLSearchParams(location.search)
  window.__markleafTestFlags = {
    themedSelection: !q.has('nothemed'),
  }

  // DOM 突变记录器：组字期间谁动了编辑器 DOM（取证重启触发器）。
  window.__mutLog = []
  function installMutObserver() {
    new MutationObserver(function (recs) {
      recs.forEach(function (r) {
        var t = r.target
        var label = t.nodeType === 3
          ? String((t.parentElement && t.parentElement.className) || 'text').slice(0, 36)
          : String(t.className || t.nodeName).slice(0, 36)
        var kind = r.type === 'characterData' ? 'cd' : (r.type === 'attributes' ? 'attr:' + r.attributeName : 'cl')
        var detail = ''
        if (r.type === 'attributes' && r.attributeName === 'class') {
          var oldC = String(r.oldValue == null ? '' : r.oldValue).split(/\s+/)
          var newC = String(t.className || '').split(/\s+/)
          var gone = oldC.filter(function (c) { return c && newC.indexOf(c) < 0 })
          var added = newC.filter(function (c) { return c && oldC.indexOf(c) < 0 })
          detail = '-' + gone.join(',') + ' +' + added.join(',')
        } else {
          detail = String(r.oldValue == null ? '' : r.oldValue).slice(0, 12) + '>' + (t.nodeValue != null ? String(t.nodeValue).slice(-6) : '')
        }
        window.__mutLog.push(kind + '@' + label + ' ' + detail)
      })
      if (window.__mutLog.length > 24) window.__mutLog.splice(0, window.__mutLog.length - 24)
    }).observe(document.body, { childList: true, characterData: true, subtree: true, attributes: true, attributeOldValue: true })
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', installMutObserver)
  else installMutObserver()
  document.addEventListener('focusin', function (e) {
    window.__mutLog.push('FOCUSIN@' + String((e.target && e.target.className) || e.target && e.target.nodeName).slice(0, 30))
  }, true)
  document.addEventListener('focusout', function (e) {
    window.__mutLog.push('FOCUSOUT@' + String((e.target && e.target.className) || e.target && e.target.nodeName).slice(0, 30))
  }, true)
  window.__titleFreezeUntil = 0
  setInterval(function () {
    if (Date.now() < window.__titleFreezeUntil) return
    var posts = webview._posted.filter(function (m) { return m.type === 'debugLog' })
    var last = posts.slice(-1).map(function (m) { return String((m.payload && m.payload.text) || '').replace(/stack=[^\n]*/, '').slice(0, 50) })
    document.title = last.concat((window.__mutLog || []).slice(-6)).join(' | ')
  }, 250)

  setTimeout(function deliver() {
    if (!listeners.length) return setTimeout(deliver, 50)
    listeners.forEach(function (fn) {
      fn({
        data: {
          protocolVersion: 1,
          type: 'loadDocument',
          documentId: 'dev',
          revision: 0,
          payload: {
            markdown:
              '回答完整讲解了分治三步骤（分解/解决/合并）、递归式 $T(n)= T(\\frac{n}{2})+\\Theta(n)$ 与主定理内容，耗时 4—7 倍。',
          },
        },
      })
    })
  }, 300)
})()
