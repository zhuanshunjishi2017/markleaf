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

    private enum CodingKeys: String, CodingKey {
        case format, paperSize, landscape, marginTop, marginBottom, marginLeft, marginRight
        case style, colorTheme, htmlHeader, htmlFooter
        case headerPreset, headerCustom, headerAlignment
        case footerPreset, footerCustom, footerAlignment
        case headerFontFamily, footerFontFamily
        case keepTablesTogether, keepHeadingsWithNextBlock
    }

    mutating func normalize() {
        format = format.lowercased() == "html" ? "html" : "pdf"
        paperSize = ["A4", "A3", "A5", "Letter", "Legal", "B4", "B5"].contains(paperSize) ? paperSize : "A4"
        marginTop = Self.normalizedMargin(marginTop, fallback: 18)
        marginBottom = Self.normalizedMargin(marginBottom, fallback: 18)
        marginLeft = Self.normalizedMargin(marginLeft, fallback: 15)
        marginRight = Self.normalizedMargin(marginRight, fallback: 15)
        style = style.isEmpty ? "serif" : style
        headerPreset = PDFHeaderFooterPolicy.normalizePreset(headerPreset)
        footerPreset = PDFHeaderFooterPolicy.normalizePreset(footerPreset)
        headerAlignment = PDFHeaderFooterPolicy.normalizeAlignment(headerAlignment)
        footerAlignment = PDFHeaderFooterPolicy.normalizeAlignment(footerAlignment)
    }

    private static func normalizedMargin(_ value: Double, fallback: Double) -> Double {
        value.isFinite && value >= 0 && value <= 1000 ? value : fallback
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
        normalize()
    }
}
