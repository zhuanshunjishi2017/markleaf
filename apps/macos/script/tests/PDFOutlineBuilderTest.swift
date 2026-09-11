import CoreGraphics
import CoreText
import Foundation
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
let partialHTML = "<h1>Missing Heading</h1><h2>Second Heading</h2>"
let outlinedPDF = PDFOutlineBuilder.addOutline(to: partialPDF, html: partialHTML)
expect(outlinedPDF != nil, "outline generation should skip headings that cannot be located")
let outlinedDocument = outlinedPDF.flatMap(PDFDocument.init(data:))
let outlineRoot = outlinedDocument?.outlineRoot
expect(outlineRoot?.numberOfChildren == 1, "only the located heading should become an outline entry")
expect(outlineRoot?.child(at: 0)?.label == "Second Heading", "the outline entry should preserve the located heading")
expect(outlineRoot?.child(at: 0)?.destination?.page == outlinedDocument?.page(at: 0), "the outline entry should point to the located page")
print("PASS")
