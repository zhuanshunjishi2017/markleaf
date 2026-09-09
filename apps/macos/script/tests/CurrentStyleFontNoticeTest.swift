import Foundation
func expect(_ value: @autoclosure () -> Bool, _ message: String) { if !value() { fputs("FAIL: \(message)\n", stderr); exit(1) } }
let catalog = OptionalFontCatalog.packs
let latex = catalog.first { $0.styleID == "latex" }!
let notebook = catalog.first { $0.styleID == "notebook" }!
func missing(_ style: String?, _ packs: [OptionalFontPack], _ statuses: [OptionalFontPackStatus]) -> [String] {
    CurrentStyleFontNotice.missingPacks(styleID: style, packs: packs, statuses: statuses).map(\.id)
}
expect(missing(nil, catalog, []) == [], "no session hides notice")
expect(missing("default", catalog, catalog.map { _ in .missing }) == [], "unrelated style hides notice")
expect(missing("latex", [latex], [.missing]) == ["computer-modern"], "current missing pack is named")
expect(missing("latex", [latex], [.partial]) == ["computer-modern"], "partial pack still lacks required fonts")
for status: OptionalFontPackStatus in [.installedByMarkLeaf, .installedExternally, .unavailable("unknown")] {
    expect(missing("latex", [latex], [status]) == [], "available or unverified status must not be called missing")
}
let extra = OptionalFontPack(id: "extra", styleID: "latex", displayName: "Extra", assetName: "", sha256: "", licenseName: nil, licenseURL: nil, licenseFileName: nil, files: [])
expect(missing("latex", [latex, extra, notebook], [.missing, .missing, .missing]) == ["computer-modern", "extra"], "include all required packs only")
expect(missing("latex", [latex], []) == [], "unknown status does not claim missing")
expect(missing("notebook", [latex, notebook], [.missing, .missing]) == ["lxgw-wenkai"], "style change updates notice")
expect(missing("latex", [latex], [.installedExternally]) == [], "status refresh clears notice")
expect(missing("latex", [latex], [.missing]) == ["computer-modern"], "uninstall refresh restores notice")
print("Current style missing font notice tests passed")
