export type NativeWheelHost = 'macOS'

export function shouldInstallFrontendWheelHandler(host: NativeWheelHost | undefined): boolean {
  return host !== 'macOS'
}
