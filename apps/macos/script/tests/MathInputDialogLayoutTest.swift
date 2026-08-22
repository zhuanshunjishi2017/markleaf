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

print("PASS")
