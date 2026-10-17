import Foundation

func expect(_ value: Bool) { precondition(value) }
let svg = Data("<svg xmlns=\"http://www.w3.org/2000/svg\"/>".utf8).base64EncodedString()
let valid: [String: Any] = ["mediaType": "image/svg+xml", "fileExtension": "svg", "base64": svg, "width": 320, "height": 180]
expect((try? MermaidImageExportPayload.decode(valid))?.width == 320)
for replacement in [["base64": "!"], ["mediaType": "image/png"], ["width": -1], ["base64": String(repeating: "A", count: 16 * 1024 * 1024)]] as [[String: Any]] {
    expect((try? MermaidImageExportPayload.decode(valid.merging(replacement) { _, new in new })) == nil)
}
let identity = MermaidExportIdentity(requestID: "r", documentID: "d", revision: 2)
expect(identity.matches(requestID: "r", documentID: "d", revision: 2, currentDocumentID: "d", currentRevision: 2))
expect(!identity.matches(requestID: "other", documentID: "d", revision: 2, currentDocumentID: "d", currentRevision: 2))
expect(!identity.matches(requestID: "r", documentID: "d", revision: 2, currentDocumentID: "next", currentRevision: 2))
expect(!identity.matches(requestID: "r", documentID: "d", revision: 2, currentDocumentID: "d", currentRevision: 3))
print("PASS")
