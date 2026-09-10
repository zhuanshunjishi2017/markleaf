import AppKit

enum ExportFormatSelector {
    static func make() -> NSSegmentedControl {
        let selector = NSSegmentedControl()
        selector.segmentStyle = .texturedRounded
        selector.controlSize = .large
        selector.trackingMode = .selectOne
        selector.segmentDistribution = .fillEqually
        selector.segmentCount = 3

        let formats = [
            (title: "PDF", symbol: "doc.richtext"),
            (title: "HTML", symbol: "curlybraces"),
            (title: L10n.t("图像"), symbol: "photo"),
        ]
        for (index, format) in formats.enumerated() {
            selector.setLabel(format.title, forSegment: index)
            selector.setImage(
                NSImage(systemSymbolName: format.symbol, accessibilityDescription: format.title),
                forSegment: index
            )
            selector.setImageScaling(.scaleProportionallyDown, forSegment: index)
        }
        selector.selectedSegment = 0
        selector.identifier = NSUserInterfaceItemIdentifier("exportFormatSelector")
        return selector
    }

    static func selectedFormat(in selector: NSSegmentedControl) -> String {
        switch selector.selectedSegment {
        case 1: return "html"
        case 2: return "image"
        default: return "pdf"
        }
    }

    static func select(format: String, in selector: NSSegmentedControl) {
        selector.selectedSegment = switch format {
        case "html": 1
        case "image": 2
        default: 0
        }
    }
}
