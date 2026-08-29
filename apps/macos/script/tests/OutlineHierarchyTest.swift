import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let headings = [
    OutlineHeading(level: 1, text: "A", position: 1),
    OutlineHeading(level: 2, text: "A.1", position: 4),
    OutlineHeading(level: 3, text: "A.1.1", position: 8),
    OutlineHeading(level: 2, text: "A.2", position: 12),
    OutlineHeading(level: 1, text: "B", position: 16),
]
let roots = OutlineHierarchy.makeNodes(headings)
expect(roots.count == 2, "level-one headings should be roots")
expect(roots[0].children.count == 2, "level-two headings should be siblings")
expect(roots[0].children[0].children.first?.heading.text == "A.1.1", "deeper headings should nest under the nearest parent")
expect(OutlineHierarchy.flatten(roots).map(\.heading.position) == [1, 4, 8, 12, 16], "flatten should preserve document order")

print("PASS")
