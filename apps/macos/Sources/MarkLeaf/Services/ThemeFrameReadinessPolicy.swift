import CoreGraphics
import Foundation
import AppKit

/// WKWebView 的 `afterScreenUpdates` 回调仍可能返回样式注入前的白色旧帧；
/// 只有抽样像素达到主题底色时，才允许移除加载遮罩。
enum ThemeFrameReadinessPolicy {
    static let tolerance: CGFloat = 0.035

    static func isReady(pixel: NSColor, target: NSColor) -> Bool {
        guard let sample = pixel.usingColorSpace(.sRGB),
              let expected = target.usingColorSpace(.sRGB) else { return false }
        guard sample.alphaComponent > 0.98 else { return false }

        let differences = [
            abs(sample.redComponent - expected.redComponent),
            abs(sample.greenComponent - expected.greenComponent),
            abs(sample.blueComponent - expected.blueComponent),
        ]
        return differences.allSatisfy { $0 <= tolerance }
    }

    static func shouldContinueWaiting(
        didCapture: Bool,
        pixel: NSColor?,
        target: NSColor,
        elapsed: TimeInterval,
        timeout: TimeInterval = 1.0
    ) -> Bool {
        if let pixel, isReady(pixel: pixel, target: target) { return false }
        return elapsed < timeout
    }
}
