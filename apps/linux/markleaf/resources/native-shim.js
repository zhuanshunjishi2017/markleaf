// MarkLeaf for Linux: 在 WebKitGTK 中提供 window.chrome.webview (WebView2) 兼容桥。
// 与 macOS shim 的差异:
//   1. postMessage 走字符串传输(JSON.stringify),Rust 侧统一按字符串解析,
//      规避 JSC 值桥接的版本差异(对齐 Android 桥的取舍)。
//   2. 无需 URL 重写:Linux 图片走原生 markleaf-asset:// 自定义 scheme,
//      由内核 native-capabilities 的 assetOrigin Linux 分支直接产出。
(function () {
  'use strict';
  if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
    return;
  }
  var listeners = [];
  var webview = {
    hostPlatform: 'Linux',
    postMessage: function (message) {
      window.webkit.messageHandlers.markleafHost.postMessage(JSON.stringify(message));
    },
    postMessageWithAdditionalObjects: function (message) {
      // WebKitGTK 消息处理器无法序列化 File 对象;与 macOS 同,仅转发消息体。
      window.webkit.messageHandlers.markleafHost.postMessage(JSON.stringify(message));
    },
    addEventListener: function (type, listener) {
      if (type === 'message' && typeof listener === 'function') {
        listeners.push(listener);
      }
    }
  };
  try {
    Object.defineProperty(window, 'chrome', { value: window.chrome || {}, writable: true, configurable: true });
    Object.defineProperty(window.chrome, 'webview', { value: webview, writable: true, configurable: true });
  } catch (e) {
    window.chrome = window.chrome || {};
    window.chrome.webview = webview;
  }
  // 宿主→前端方向不走本桥:宿主经 evaluateJavaScript 直接 window.postMessage,
  // 这里按 macOS shim 同款过滤规则把它分发给协议监听者。
  window.addEventListener('message', function (event) {
    var data = event && event.data;
    if (data && typeof data === 'object' && typeof data.protocolVersion === 'number') {
      for (var i = 0; i < listeners.length; i++) {
        try { listeners[i]({ data: data }); } catch (e) { /* ignore */ }
      }
    }
  });
})();
