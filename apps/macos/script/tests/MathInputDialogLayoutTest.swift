import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let label = NSTextField(labelWithString: "公式编号")
label.font = .systemFont(ofSize: 12)
let width = MathInputDialogLayout.numberLabelColumnWidth(for: label)

expect(width == ceil(label.fittingSize.width),
       "formula number label column should hug its visible text")
expect(width < 82,
       "formula number label should not retain the old oversized column")
expect(MathInputDialogLayout.numberColumnSpacing == 8,
       "formula number field should use the standard eight-point gap")
expect(MathInputDialogLayout.verticalInset == 4,
       "formula dialog should use a compact four-point vertical inset")
expect(MathInputDialogLayout.rowSpacing == 4,
       "formula dialog should use a compact four-point row gap")
expect(MathInputDialogLayout.latexFieldY(showNumber: false) == 4,
       "inline formula field should sit close to the accessory top edge")
expect(MathInputDialogLayout.latexFieldY(showNumber: true) == 36,
       "block formula field should sit four points above the number row")
expect(MathInputDialogLayout.accessoryHeight(showNumber: false, fieldHeight: 24) == 32,
       "inline formula accessory should not retain excess vertical space")
expect(MathInputDialogLayout.accessoryHeight(showNumber: true, fieldHeight: 24) == 64,
       "block formula accessory should keep only compact row spacing")

print("PASS")
