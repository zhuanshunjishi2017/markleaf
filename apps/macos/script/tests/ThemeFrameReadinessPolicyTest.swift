import AppKit
import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let dark = NSColor(srgbRed: 0x1C / 255.0, green: 0x1C / 255.0, blue: 0x1E / 255.0, alpha: 1)
let white = NSColor(srgbRed: 1, green: 1, blue: 1, alpha: 1)

expect(ThemeFrameReadinessPolicy.isReady(pixel: dark, target: dark), "exact theme pixels should be ready")
expect(ThemeFrameReadinessPolicy.isReady(pixel: dark.withAlphaComponent(0.99), target: dark), "near-opaque theme pixels should be ready")
expect(!ThemeFrameReadinessPolicy.isReady(pixel: white, target: dark), "white pixels must not satisfy a dark theme")
expect(!ThemeFrameReadinessPolicy.isReady(pixel: dark.withAlphaComponent(0.1), target: dark), "transparent pixels must not satisfy a theme")

expect(!ThemeFrameReadinessPolicy.shouldContinueWaiting(didCapture: true, pixel: dark, target: dark, elapsed: 0), "matching frames should reveal")
expect(ThemeFrameReadinessPolicy.shouldContinueWaiting(didCapture: true, pixel: white, target: dark, elapsed: 0), "stale white frames should keep waiting")
expect(!ThemeFrameReadinessPolicy.shouldContinueWaiting(didCapture: true, pixel: white, target: dark, elapsed: 1.1), "timeout should provide a fallback reveal")
expect(ThemeFrameReadinessPolicy.shouldContinueWaiting(didCapture: false, pixel: nil, target: dark, elapsed: 0), "missing captures should keep waiting")

print("PASS")
