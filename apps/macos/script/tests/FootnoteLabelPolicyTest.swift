import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(FootnoteLabelPolicy.normalized("  1.1  ") == "1.1", "label should be trimmed")
expect(FootnoteLabelPolicy.normalized("chapter note") == "chapter note", "spaces inside a label should remain valid")
expect(FootnoteLabelPolicy.normalized("") == nil, "empty labels should be invalid")
expect(FootnoteLabelPolicy.normalized("bad]label") == nil, "closing brackets should be invalid")
expect(FootnoteLabelPolicy.normalized("bad\nlabel") == nil, "newlines should be invalid")
print("PASS")
