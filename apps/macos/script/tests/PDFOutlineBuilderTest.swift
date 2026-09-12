import Foundation
import CoreText
import AppKit
import PDFKit

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

// Use real PDF text extraction, including a blank page, to cover the export path.
func makePDF(pages: [String?]) -> Data {
    let data = NSMutableData()
    var bounds = CGRect(x: 0, y: 0, width: 600, height: 800)
    let consumer = CGDataConsumer(data: data as CFMutableData)!
    let context = CGContext(consumer: consumer, mediaBox: &bounds, nil)!
    for text in pages {
        context.beginPDFPage(nil)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: false)
        if let text {
            for (index, line) in text.components(separatedBy: "\n").enumerated() {
                NSAttributedString(string: line, attributes: [.font: NSFont.systemFont(ofSize: 16)])
                    .draw(at: NSPoint(x: 40, y: 740 - CGFloat(index) * 30))
            }
        }
        NSGraphicsContext.restoreGraphicsState()
        context.endPDFPage()
    }
    context.closePDF()
    return data as Data
}

let source = makePDF(pages: ["First", nil, "Third\nRepeat", "Repeat"])
let partialHTML = """
<h1>Missing first</h1><h1>First</h1><h2>Missing middle</h2>
<h2>Third</h2><h2>Repeat</h2><h2>Missing after repeat</h2>
<h2>Repeat</h2><h2>Missing last</h2>
"""
guard let outlined = PDFOutlineBuilder.addOutline(to: source, html: partialHTML),
      let document = PDFDocument(data: outlined), let root = document.outlineRoot else {
    fatalError("matched headings should produce a readable PDF outline")
}
let bookmarks = (0..<root.numberOfChildren).compactMap { root.child(at: $0) }
expect(bookmarks.map(\.label) == ["First", "Third", "Repeat", "Repeat"],
       "unmatched headings must not borrow another heading's label or destination")
expect(bookmarks.map { item in item.destination?.page.map { document.index(for: $0) } } == [0, 2, 2, 3],
       "blank pages keep their page indices and repeated headings advance to the next occurrence")
expect(PDFOutlineBuilder.addOutline(to: source, html: "<h1>Absent</h1>") == nil,
       "unmatched headings must not create fabricated bookmarks")
func makeSinglePagePDF(text: String) -> Data {
    let output = NSMutableData()
    let consumer = CGDataConsumer(data: output as CFMutableData)!
    var mediaBox = CGRect(x: 0, y: 0, width: 500, height: 400)
    let context = CGContext(consumer: consumer, mediaBox: &mediaBox, nil)!
    context.beginPDFPage(nil)
    let font = CTFontCreateWithName("Helvetica" as CFString, 24, nil)
    let attributes = [kCTFontAttributeName: font] as CFDictionary
    let attributedText = (CFAttributedStringCreate(nil, text as CFString, attributes) as NSAttributedString?)!
    let line = CTLineCreateWithAttributedString(attributedText)
    context.textPosition = CGPoint(x: 50, y: 300)
    CTLineDraw(line, context)
    context.endPDFPage()
    context.closePDF()
    return output as Data
}

let partialPDF = makeSinglePagePDF(text: "Second Heading")
let singlePageHTML = "<h1>Missing Heading</h1><h2>Second Heading</h2>"
let outlinedPDF = PDFOutlineBuilder.addOutline(to: partialPDF, html: singlePageHTML)
expect(outlinedPDF != nil, "outline generation should skip headings that cannot be located")
let outlinedDocument = outlinedPDF.flatMap(PDFDocument.init(data:))
let outlineRoot = outlinedDocument?.outlineRoot
expect(outlineRoot?.numberOfChildren == 1, "only the located heading should become an outline entry")
expect(outlineRoot?.child(at: 0)?.label == "Second Heading", "the outline entry should preserve the located heading")
expect(outlineRoot?.child(at: 0)?.destination?.page == outlinedDocument?.page(at: 0), "the outline entry should point to the located page")
print("PASS")
