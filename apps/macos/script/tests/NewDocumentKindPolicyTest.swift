import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(NewDocumentKind.markdown.fileExtension == "md", "Markdown should use .md")
expect(NewDocumentKind.markdown.editorDocumentType == "markdown", "Markdown should use markdown editor type")
expect(NewDocumentKind.markdown.defaultFileName == "未命名.md", "Markdown should use the md untitled name")
expect(NewDocumentKind.plainText.fileExtension == "txt", "plain text should use .txt")
expect(NewDocumentKind.plainText.editorDocumentType == "plainText", "plain text should use plainText editor type")
expect(NewDocumentKind.plainText.defaultFileName == "未命名.txt", "plain text should use the txt untitled name")
expect(NewDocumentKind.from(fileExtension: "TXT") == .plainText, "TXT should detect plain text")
expect(NewDocumentKind.from(fileExtension: "markdown") == .markdown, "Markdown extension should detect Markdown")

print("PASS")
