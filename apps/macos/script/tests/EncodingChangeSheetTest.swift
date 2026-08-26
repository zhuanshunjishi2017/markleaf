import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(EncodingChangeSheetPresenter.choice(for: .alertFirstButtonReturn) == .directRead,
       "the first sheet action should directly read disk bytes")
expect(EncodingChangeSheetPresenter.choice(for: .alertSecondButtonReturn) == .convertEncoding,
       "the second sheet action should convert the current editor text")
expect(EncodingChangeSheetPresenter.choice(for: .alertThirdButtonReturn) == .cancel,
       "the third sheet action should cancel without changing state")
expect(EncodingChangeSheetPresenter.choice(for: .abort) == .cancel,
       "closing or escaping the sheet should be treated as cancel")

print("PASS")
