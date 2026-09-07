import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let markdown = """
---
title: Doc
---

# 标题

链接 [文本](./page.md)、图片 ![说明](./a.png)、代码 `var value = 1` 和 **重点**。
"""

let plain = MarkdownPlainText.fromDocument(markdown, isMarkdown: true)
expect(!plain.contains("# "), "heading markers should be stripped")
expect(!plain.contains("---\ntitle"), "front matter should be stripped")
expect(plain.contains("标题"), "heading text should remain")
expect(plain.contains("文本") && !plain.contains("[文本]"), "link text should remain")
expect(plain.contains("说明") && !plain.contains("![说明]"), "image alt text should remain")
expect(plain.contains("var value = 1"), "inline code content should remain")
expect(plain.contains("重点") && !plain.contains("**重点"), "emphasis markers should be stripped")

let snippet = WorkspacePlainTextSnippet.snippet(
    "Before 0123456789 needle after 0123456789",
    query: "needle"
)
expect(snippet?.hasSuffix("needle") == true || snippet?.contains("needle after") == true, "content snippets should include the query")
expect(snippet?.contains("needle") == true, "content snippets should include the query")
expect((snippet?.count ?? 0) <= "needle".count + 42, "content snippets should use the Windows length limit")
print("PASS")
