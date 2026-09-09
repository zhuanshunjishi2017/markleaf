import Foundation

struct PersistedExportSettings: Codable, Equatable {
    var format = "pdf"
    var paperSize = "A4"
    var landscape = false
    var marginTop = 18.0
    var marginBottom = 18.0
    var marginLeft = 15.0
    var marginRight = 15.0
    var style = "serif"
    var colorTheme = ""
    var htmlHeader = ""
    var htmlFooter = ""
    var headerPreset = "none"
    var headerCustom = ""
    var headerAlignment = ""
    var footerPreset = "none"
    var footerCustom = ""
    var footerAlignment = ""
    var headerFontFamily = ""
    var footerFontFamily = ""
    var keepTablesTogether = true
    var keepHeadingsWithNextBlock = true
    var imageMaxHeight = 12000.0
    var imageContentWidth = 1200.0
    var imageScale = 2.0
    var imageFormat = "png"
    var imageJpegQuality = 90.0

    private enum CodingKeys: String, CodingKey {
        case format, paperSize, landscape, marginTop, marginBottom, marginLeft, marginRight
        case style, colorTheme, htmlHeader, htmlFooter
        case headerPreset, headerCustom, headerAlignment
        case footerPreset, footerCustom, footerAlignment
        case headerFontFamily, footerFontFamily
        case keepTablesTogether, keepHeadingsWithNextBlock
        case imageMaxHeight, imageContentWidth, imageScale, imageFormat, imageJpegQuality
    }

    mutating func normalize() {
        format = ["html", "image"].contains(format.lowercased()) ? format.lowercased() : "pdf"
        paperSize = ["A4", "A3", "A5", "Letter", "Legal", "B4", "B5"].contains(paperSize) ? paperSize : "A4"
        marginTop = Self.normalizedMargin(marginTop, fallback: 18)
        marginBottom = Self.normalizedMargin(marginBottom, fallback: 18)
        marginLeft = Self.normalizedMargin(marginLeft, fallback: 15)
        marginRight = Self.normalizedMargin(marginRight, fallback: 15)
        style = style.isEmpty ? "serif" : style
        colorTheme = ThemeIDNormalizer.normalize(colorTheme)
        headerPreset = PDFHeaderFooterPolicy.normalizePreset(headerPreset)
        footerPreset = PDFHeaderFooterPolicy.normalizePreset(footerPreset)
        headerAlignment = PDFHeaderFooterPolicy.normalizeAlignment(headerAlignment)
        footerAlignment = PDFHeaderFooterPolicy.normalizeAlignment(footerAlignment)
        imageMaxHeight = Self.normalizedImageDimension(imageMaxHeight, fallback: 12000, range: 1000...30000)
        imageContentWidth = Self.normalizedImageDimension(imageContentWidth, fallback: 1200, range: 320...4000)
        imageScale = Self.normalizedImageDimension(imageScale, fallback: 2, range: 1...4)
        imageFormat = imageFormat.lowercased() == "jpg" ? "jpg" : "png"
        imageJpegQuality = Self.normalizedImageDimension(imageJpegQuality, fallback: 90, range: 1...100)
    }

    private static func normalizedMargin(_ value: Double, fallback: Double) -> Double {
        value.isFinite && value >= 0 && value <= 1000 ? value : fallback
    }

    private static func normalizedImageDimension(_ value: Double, fallback: Double, range: ClosedRange<Double>) -> Double {
        value.isFinite ? min(max(value, range.lowerBound), range.upperBound) : fallback
    }
}

extension PersistedExportSettings {
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        format = try container.decodeIfPresent(String.self, forKey: .format) ?? "pdf"
        paperSize = try container.decodeIfPresent(String.self, forKey: .paperSize) ?? "A4"
        landscape = try container.decodeIfPresent(Bool.self, forKey: .landscape) ?? false
        marginTop = try container.decodeIfPresent(Double.self, forKey: .marginTop) ?? 18
        marginBottom = try container.decodeIfPresent(Double.self, forKey: .marginBottom) ?? 18
        marginLeft = try container.decodeIfPresent(Double.self, forKey: .marginLeft) ?? 15
        marginRight = try container.decodeIfPresent(Double.self, forKey: .marginRight) ?? 15
        style = try container.decodeIfPresent(String.self, forKey: .style) ?? "serif"
        colorTheme = try container.decodeIfPresent(String.self, forKey: .colorTheme) ?? ""
        htmlHeader = try container.decodeIfPresent(String.self, forKey: .htmlHeader) ?? ""
        htmlFooter = try container.decodeIfPresent(String.self, forKey: .htmlFooter) ?? ""
        headerPreset = try container.decodeIfPresent(String.self, forKey: .headerPreset) ?? "none"
        headerCustom = try container.decodeIfPresent(String.self, forKey: .headerCustom) ?? ""
        headerAlignment = try container.decodeIfPresent(String.self, forKey: .headerAlignment) ?? ""
        footerPreset = try container.decodeIfPresent(String.self, forKey: .footerPreset) ?? "none"
        footerCustom = try container.decodeIfPresent(String.self, forKey: .footerCustom) ?? ""
        footerAlignment = try container.decodeIfPresent(String.self, forKey: .footerAlignment) ?? ""
        headerFontFamily = try container.decodeIfPresent(String.self, forKey: .headerFontFamily) ?? ""
        footerFontFamily = try container.decodeIfPresent(String.self, forKey: .footerFontFamily) ?? ""
        keepTablesTogether = try container.decodeIfPresent(Bool.self, forKey: .keepTablesTogether) ?? true
        keepHeadingsWithNextBlock = try container.decodeIfPresent(Bool.self, forKey: .keepHeadingsWithNextBlock) ?? true
        imageMaxHeight = try container.decodeIfPresent(Double.self, forKey: .imageMaxHeight) ?? 12000
        imageContentWidth = try container.decodeIfPresent(Double.self, forKey: .imageContentWidth) ?? 1200
        imageScale = try container.decodeIfPresent(Double.self, forKey: .imageScale) ?? 2
        imageFormat = try container.decodeIfPresent(String.self, forKey: .imageFormat) ?? "png"
        imageJpegQuality = try container.decodeIfPresent(Double.self, forKey: .imageJpegQuality) ?? 90
        normalize()
    }
}
