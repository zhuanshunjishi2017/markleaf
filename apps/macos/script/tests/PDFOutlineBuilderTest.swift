import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let html = """
<h1 id="a">安装 <strong>指南</strong></h1>
<p>正文</p>
<h2 class='x'>快速开始</h2>
<h1>&amp;高级</h1>
"""
let headings = PDFOutlineBuilder.headings(in: html)
expect(headings == ["安装 指南", "快速开始", "&高级"], "outline headings should be extracted in document order")
expect(PDFOutlineBuilder.headings(in: "<p>没有标题</p>").isEmpty, "documents without headings should have no outline")
print("PASS")
