// jsdom 未实现 ResizeObserver，编辑器图片 NodeView 依赖它监听容器尺寸变化。
// 在测试环境中用一个空实现替代，避免 `new ResizeObserver` 抛 ReferenceError。
class ResizeObserverStub {
  observe(): void {}

  unobserve(): void {}

  disconnect(): void {}
}

if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

// jsdom supplies no text layout. Match its empty Element geometry for Range
// so active CodeMirror views can measure; real geometry belongs in browser tests.
if (typeof Range !== 'undefined' && typeof Range.prototype.getClientRects === 'undefined') {
  Range.prototype.getClientRects = () => Object.assign([], { item: () => null }) as unknown as DOMRectList
}
if (typeof Range !== 'undefined' && typeof Range.prototype.getBoundingClientRect === 'undefined') {
  Range.prototype.getBoundingClientRect = () => new DOMRect()
}
