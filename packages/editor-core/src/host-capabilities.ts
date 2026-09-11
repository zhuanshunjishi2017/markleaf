export type HostCapabilities = {
  usesThemedVisualSelection: boolean
  primaryActivationModifier: 'meta' | 'ctrl'
  installsFrontendWheelHandler: boolean
}

export function hasPrimaryActivationModifier(
  event: Pick<MouseEvent, 'metaKey' | 'ctrlKey'>,
  capabilities: HostCapabilities,
): boolean {
  return capabilities.primaryActivationModifier === 'meta'
    ? event.metaKey
    : event.ctrlKey
}
