import Foundation
import CoreGraphics

enum WindowFrameRestorationPolicy {
    static func constrain(_ frame: CGRect, screens: [CGRect]) -> CGRect {
        guard !screens.isEmpty else { return frame }
        if screens.contains(where: { !$0.intersection(frame).isNull }) { return frame }
        let screen = screens[0]
        let width = min(frame.size.width, max(1, screen.size.width - 40))
        let height = min(frame.size.height, max(1, screen.size.height - 40))
        let x = screen.minX + max(20, (screen.size.width - width) / 2)
        let y = screen.minY + max(20, (screen.size.height - height) / 2)
        return CGRect(x: x, y: y, width: width, height: height)
    }

    static func stagger(_ frames: [CGRect]) -> [CGRect] {
        var result: [CGRect] = []
        var originals: [CGRect] = []
        for frame in frames {
            let duplicates = originals.filter { $0.equalTo(frame) }.count
            result.append(duplicates == 0 ? frame : CGRect(x: frame.minX + CGFloat(duplicates) * 24, y: frame.minY - CGFloat(duplicates) * 24, width: frame.size.width, height: frame.size.height))
            originals.append(frame)
        }
        return result
    }
}
