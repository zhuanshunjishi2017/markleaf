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

// jsdom has no media-query engine. Reading behavior still needs the standard
// subscription surface even when no reduced-motion preference is active.
if (typeof window.matchMedia === 'undefined') {
  window.matchMedia = (media: string): MediaQueryList => Object.assign(new EventTarget(), {
    media, matches: false, onchange: null, addListener() {}, removeListener() {},
  }) as MediaQueryList
}
