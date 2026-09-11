import { shouldInstallFrontendWheelHandler, type NativeWheelHost } from './wheel-routing'

export type HostCapabilities = {
  usesThemedVisualSelection: boolean
  primaryActivationModifier: 'meta' | 'ctrl'
  installsFrontendWheelHandler: boolean
}

export function resolveHostCapabilities(
  hostPlatform?: NativeWheelHost,
): HostCapabilities {
  const macOS = hostPlatform === 'macOS'
  return {
    usesThemedVisualSelection: macOS,
    primaryActivationModifier: macOS ? 'meta' : 'ctrl',
    installsFrontendWheelHandler: shouldInstallFrontendWheelHandler(hostPlatform),
  }
}

export function hasPrimaryActivationModifier(
  event: Pick<MouseEvent, 'metaKey' | 'ctrlKey'>,
  capabilities: HostCapabilities,
): boolean {
  return capabilities.primaryActivationModifier === 'meta'
    ? event.metaKey
    : event.ctrlKey
}
