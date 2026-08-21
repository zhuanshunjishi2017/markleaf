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

    mutating func normalize() {
        format = format.lowercased() == "html" ? "html" : "pdf"
        paperSize = ["A4", "A3", "A5", "Letter", "Legal"].contains(paperSize) ? paperSize : "A4"
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
