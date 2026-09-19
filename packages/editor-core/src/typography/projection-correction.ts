export type TypographySpacingMode = 'word' | 'character'

export type TypographySpacingCorrection = {
  mode: TypographySpacingMode
  spacing: number
  delta: number
}

export function calculateTypographySpacingCorrection(input: {
  mode: TypographySpacingMode
  currentSpacing: number
  currentWidth: number
  targetWidth: number
  gapCount: number
  threshold?: number
}): TypographySpacingCorrection | null {
  const { mode, currentSpacing, currentWidth, targetWidth, gapCount } = input
  const threshold = input.threshold ?? 0.25
  if (![currentSpacing, currentWidth, targetWidth].every(Number.isFinite)) return null
  if (targetWidth <= 0 || currentWidth <= 0 || gapCount <= 0) return null

  const delta = targetWidth - currentWidth
  if (Math.abs(delta) <= threshold) return null
  const spacing = currentSpacing + delta / gapCount
  if (!Number.isFinite(spacing)) return null
  return { mode, spacing, delta }
}
