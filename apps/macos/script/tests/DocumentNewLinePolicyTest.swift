import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(DocumentNewLinePolicy.detect("one\ntwo\n") == .lf,
       "LF documents should be detected as LF")
expect(DocumentNewLinePolicy.detect("one\r\ntwo\r\n") == .crlf,
       "CRLF documents should be detected as CRLF")
expect(DocumentNewLinePolicy.detect("one\r\ntwo\n") == .mixed,
       "documents containing both newline styles should be mixed")

expect(DocumentNewLinePolicy.normalize("one\r\ntwo\n", to: .lf) == "one\ntwo\n",
       "normalizing to LF should remove CR bytes")
expect(DocumentNewLinePolicy.normalize("one\r\ntwo\n", to: .crlf) == "one\r\ntwo\r\n",
       "normalizing to CRLF should use CRLF for every line ending")
expect(DocumentNewLinePolicy.normalize("one\r\ntwo\n", to: .mixed) == "one\r\ntwo\n",
       "mixed output should preserve the caller-provided line endings")

print("PASS")
