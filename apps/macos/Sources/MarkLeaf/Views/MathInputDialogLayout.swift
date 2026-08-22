import AppKit

enum MathInputDialogLayout {
    static let numberColumnSpacing: CGFloat = 8

    static func numberLabelColumnWidth(for label: NSTextField) -> CGFloat {
        ceil(label.fittingSize.width)
    }
}
