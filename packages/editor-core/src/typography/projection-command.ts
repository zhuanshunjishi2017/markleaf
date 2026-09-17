export type TypographyProjectionCommand = {
  enabled: boolean
}

export function parseTypographyProjectionCommand(payload: unknown): TypographyProjectionCommand | null {
  if (!payload || typeof payload !== 'object') return null
  const record = payload as Record<string, unknown>
  if (record.command !== 'setTypographyProjection') return null
  if (record.text === '1') return { enabled: true }
  if (record.text === '0') return { enabled: false }
  return null
}
