import AppKit

enum MathInputDialogLayout {
    static let numberColumnSpacing: CGFloat = 8
    static let verticalInset: CGFloat = 4
    static let rowSpacing: CGFloat = 4
    static let numberRowHeight: CGFloat = 28

    static func latexFieldY(showNumber: Bool) -> CGFloat {
        showNumber ? verticalInset + numberRowHeight + rowSpacing : verticalInset
    }

    static func accessoryHeight(showNumber: Bool, fieldHeight: CGFloat) -> CGFloat {
        let contentHeight = showNumber
            ? numberRowHeight + rowSpacing + fieldHeight
            : fieldHeight
        return ceil(verticalInset * 2 + contentHeight)
    }

    static func numberLabelColumnWidth(for label: NSTextField) -> CGFloat {
        ceil(label.fittingSize.width)
    }
}
