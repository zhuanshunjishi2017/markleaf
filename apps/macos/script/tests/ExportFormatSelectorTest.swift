import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

_ = NSApplication.shared
let selector = ExportFormatSelector.make()

expect(selector.segmentCount == 3, "export selector must expose PDF, HTML and image")
expect(selector.label(forSegment: 0) == "PDF", "PDF must be the first selector item")
expect(selector.label(forSegment: 1) == "HTML", "HTML must be the second selector item")
expect(selector.image(forSegment: 0) != nil, "PDF needs a selector icon")
expect(selector.image(forSegment: 1) != nil, "HTML needs a selector icon")
expect(selector.selectedSegment == 0, "PDF must be selected by default")
expect(ExportFormatSelector.selectedFormat(in: selector) == "pdf", "default selector selection must map to PDF")

ExportFormatSelector.select(format: "html", in: selector)
expect(selector.selectedSegment == 1, "persisted HTML must select the HTML selector item")
expect(ExportFormatSelector.selectedFormat(in: selector) == "html", "HTML selector selection must map to HTML")

ExportFormatSelector.select(format: "image", in: selector)
expect(selector.selectedSegment == 2, "image selection must restore")
expect(ExportFormatSelector.selectedFormat(in: selector) == "image", "image selection must map to image")
expect(selector.image(forSegment: 2) != nil, "image needs a selector icon")
expect(selector.segmentDistribution == .fillEqually, "segments must share available width")
for width in [300.0, 340.0, 400.0] {
    selector.frame.size = NSSize(width: width, height: 32)
    selector.layoutSubtreeIfNeeded()
    expect(selector.frame.width == width, "selector must accept available width")
}
print("PASS")
