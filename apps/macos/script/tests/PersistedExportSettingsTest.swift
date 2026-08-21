import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let defaults = PersistedExportSettings()
expect(defaults.format == "pdf", "default export format should be PDF")
expect(defaults.paperSize == "A4", "default paper size should be A4")
expect(defaults.marginTop == 18 && defaults.marginBottom == 18
       && defaults.marginLeft == 15 && defaults.marginRight == 15,
       "default export margins should use the standard preset")
expect(defaults.headerPreset == "none", "default header should be empty")

let custom = PersistedExportSettings(
    format: "html", paperSize: "A3", landscape: true,
    marginTop: 6, marginBottom: 7, marginLeft: 8, marginRight: 9,
    style: "serif", colorTheme: "colors-saltlemon",
    htmlHeader: "Title", htmlFooter: "Footer",
    headerPreset: "custom", headerCustom: "{title}", headerAlignment: "right",
    footerPreset: "page-total-center", footerCustom: "", footerAlignment: "center",
    headerFontFamily: "Georgia", footerFontFamily: "Menlo")
let data = try! JSONEncoder().encode(custom)
let decoded = try! JSONDecoder().decode(PersistedExportSettings.self, from: data)
expect(decoded == custom, "export settings should round-trip through Codable")

var invalid = PersistedExportSettings(format: "doc", paperSize: "B0", landscape: false,
                                      marginTop: -1, marginBottom: 2000, marginLeft: .nan, marginRight: .infinity,
                                      style: "", colorTheme: "", htmlHeader: "", htmlFooter: "",
                                      headerPreset: "bad", headerCustom: "", headerAlignment: "bad",
                                      footerPreset: "bad", footerCustom: "", footerAlignment: "bad",
                                      headerFontFamily: "", footerFontFamily: "")
invalid.normalize()
expect(invalid.format == "pdf" && invalid.paperSize == "A4", "invalid enum-like values should normalize")
expect(invalid.marginTop == 18 && invalid.marginBottom == 18
       && invalid.marginLeft == 15 && invalid.marginRight == 15,
       "invalid margins should use standard defaults")
expect(invalid.headerPreset == "none" && invalid.footerPreset == "none", "invalid presets should normalize")

let resolved = PDFHeaderFooterPolicy.resolve("{title} · {page}/{pages}", title: "Notes", page: 2, pages: 5)
expect(resolved == "Notes · 2/5", "header/footer placeholders should resolve")
expect(PDFHeaderFooterPolicy.alignment(for: "page-right") == "right", "page-right should align right")
expect(PDFHeaderFooterPolicy.alignment(for: "bad") == "", "none/invalid presets should have no alignment")

print("PASS")
