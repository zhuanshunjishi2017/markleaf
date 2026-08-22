type MotionPreferenceListener = (event: { matches: boolean }) => void

export interface MotionPreferenceSource {
  readonly matches: boolean
  addEventListener(type: 'change', listener: MotionPreferenceListener): void
  removeEventListener(type: 'change', listener: MotionPreferenceListener): void
}

const reducedMotionClass = 'markleaf-reduced-motion'

type FrameCallback = (time: number) => void

export interface AlphaScheduler {
  now(): number
  requestFrame(callback: FrameCallback): number
  cancelFrame(requestId: number): void
}

/**
 * 驱动滚动条不透明度从 from 平滑过渡到 to。
 *
 * WebKit 的 ::-webkit-scrollbar-thumb 不参与 CSS transition，所以这里用
 * requestAnimationFrame 逐帧写入 CSS alpha 变量，让滚动条真正产生淡入/淡出。
 * reducedMotion 为 true 时直接跳到目标值，不产生动画。
 */
export function scrollbarAlphaAnimation(
  from: number,
  to: number,
  durationMs: number,
  reducedMotion: boolean,
  setAlpha: (alpha: number) => void,
  scheduler: AlphaScheduler,
): () => void {
  setAlpha(to)
  if (reducedMotion || durationMs <= 0) {
    return () => scheduler.cancelFrame(-1)
  }

  const start = scheduler.now()
  let cancelled = false
  let frameId = 0

  const step = (now: number): void => {
    if (cancelled) {
      return
    }
    const progress = Math.min(1, Math.max(0, (now - start) / durationMs))
    const eased = progress < 0.5
      ? 2 * progress * progress
      : 1 - Math.pow(-2 * progress + 2, 2) / 2
    const nextAlpha = from + (to - from) * eased
    setAlpha(nextAlpha)
    if (progress < 1) {
      frameId = scheduler.requestFrame(step)
    }
  }

  // 先用一次帧启动，避免在首帧之前就直接落到目标值导致看不出动画。
  frameId = scheduler.requestFrame((now) => {
    // 首帧使用起始值，从下一帧开始推进，保证能看到 0 -> 1 的变化。
    if (now <= start) {
      setAlpha(from)
      frameId = scheduler.requestFrame(step)
    } else {
      step(now)
    }
  })

  return () => {
    cancelled = true
    scheduler.cancelFrame(frameId)
  }
}

export function bindReducedMotionPreference(
  preference: MotionPreferenceSource,
  root: HTMLElement,
  body: HTMLElement,
): () => void {
  const apply = (reduced: boolean) => {
    root.classList.toggle(reducedMotionClass, reduced)
    body.classList.toggle(reducedMotionClass, reduced)
  }
  const onChange: MotionPreferenceListener = (event) => apply(event.matches)

  apply(preference.matches)
  preference.addEventListener('change', onChange)

  return () => preference.removeEventListener('change', onChange)
}
