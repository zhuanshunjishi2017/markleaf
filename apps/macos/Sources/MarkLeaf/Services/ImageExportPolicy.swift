import Foundation

/// UI ranges and renderer validation share one definition. Dimensions are integral;
/// content width is in CSS pixels, maximum height is in final output pixels.
enum ImageExportPolicy {
    static let contentWidthRange = 320...4000
    static let maxHeightRange = 1000...30000

    static func isValid(_ options: ExportOptions) -> Bool {
        func integer(_ value: Double, in range: ClosedRange<Int>) -> Bool {
            value.isFinite && value.rounded() == value
                && value >= Double(range.lowerBound) && value <= Double(range.upperBound)
        }
        return integer(options.imageContentWidth, in: contentWidthRange)
            && integer(options.imageMaxHeight, in: maxHeightRange)
            && integer(options.imageScale, in: 1...4)
            && ["png", "jpg"].contains(options.imageFormat)
            && options.imageJpegQuality.isFinite && (1...100).contains(options.imageJpegQuality)
    }
}
