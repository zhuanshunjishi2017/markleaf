import AppKit

enum ExportFormatSelector {
    static func make() -> NSSegmentedControl {
        let selector = NSSegmentedControl()
        selector.segmentStyle = .texturedRounded
        selector.controlSize = .large
        selector.trackingMode = .selectOne
        selector.segmentCount = 2

        let formats = [
            (title: "PDF", symbol: "doc.richtext"),
            (title: "HTML", symbol: "curlybraces"),
        ]
        for (index, format) in formats.enumerated() {
            selector.setLabel(format.title, forSegment: index)
            selector.setImage(
                NSImage(systemSymbolName: format.symbol, accessibilityDescription: format.title),
                forSegment: index
            )
            selector.setImageScaling(.scaleProportionallyDown, forSegment: index)
            selector.setWidth(92, forSegment: index)
        }
        selector.selectedSegment = 0
        selector.identifier = NSUserInterfaceItemIdentifier("exportFormatSelector")
        return selector
    }

    static func selectedFormat(in selector: NSSegmentedControl) -> String {
        selector.selectedSegment == 1 ? "html" : "pdf"
    }

    static func select(format: String, in selector: NSSegmentedControl) {
        selector.selectedSegment = format == "html" ? 1 : 0
    }
}
