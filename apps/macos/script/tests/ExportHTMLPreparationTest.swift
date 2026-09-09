import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let originalDocumentURL = URL(fileURLWithPath: "/tmp/original/document.md")
var activeDocumentURL = originalDocumentURL
let contexts = ExportHTMLRoute.allCases.map {
    ExportHTMLRequestContext(route: $0, documentURL: activeDocumentURL)
}
activeDocumentURL = URL(fileURLWithPath: "/tmp/switched/other.md")

for context in contexts {
    var invocationCount = 0
    var observedDocumentURL: URL?
    let prepared = ExportHTMLPreparation.prepare(
        html: #"<img src="asset.png">"#,
        context: context
    ) { html, documentURL in
        invocationCount += 1
        observedDocumentURL = documentURL
        return ExportLocalImageEmbeddingResult(
            html: html.replacingOccurrences(of: "asset.png", with: "data:image/png;base64,AA=="),
            embeddedCount: 1,
            issues: []
        )
    }

    expect(invocationCount == 1, "\(context.route) must prepare generated HTML exactly once")
    expect(observedDocumentURL == originalDocumentURL,
           "\(context.route) must resolve against the document captured when the request started")
    expect(prepared.route == context.route, "prepared HTML must retain its destination route")
    expect(prepared.html == #"<img src="data:image/png;base64,AA==">"#,
           "every native route must receive the same transformed HTML")
    expect(prepared.unresolvedCount == 0, "successful preparation must not report unresolved resources")
}

expect(activeDocumentURL != contexts[0].documentURL,
       "test must simulate switching documents after export request capture")

print("PASS")
