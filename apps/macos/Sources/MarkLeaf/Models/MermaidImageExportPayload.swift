import Foundation

struct MermaidExportIdentity {
    let requestID: String
    let documentID: String
    let revision: Int64
    func matches(requestID: String?, documentID: String?, revision: Int64?, currentDocumentID: String, currentRevision: Int64) -> Bool {
        self.requestID == requestID && self.documentID == documentID && self.revision == revision
            && self.documentID == currentDocumentID && self.revision == currentRevision
    }
}

struct MermaidImageExportPayload {
    let data: Data
    let fileExtension: String
    let mediaType: String
    let width: Int
    let height: Int
    static func decode(_ payload: [String: Any]) throws -> Self {
        if let error = payload["error"] as? String { throw NSError(domain: "MarkLeaf.ImageExport", code: 1, userInfo: [NSLocalizedDescriptionKey: error]) }
        guard let encoded = payload["base64"] as? String, encoded.utf8.count < 16 * 1024 * 1024 - 1024,
              let data = Data(base64Encoded: encoded), !data.isEmpty,
              let ext = payload["fileExtension"] as? String, let mime = payload["mediaType"] as? String,
              (ext == "png" && mime == "image/png") || (ext == "svg" && mime == "image/svg+xml"),
              let width = payload["width"] as? Int, let height = payload["height"] as? Int,
              width > 0, height > 0 else { throw CocoaError(.fileReadCorruptFile) }
        if ext == "png" {
            guard data.count >= 24, data.starts(with: [137,80,78,71,13,10,26,10]) else { throw CocoaError(.fileReadCorruptFile) }
        } else {
            let parser = XMLParser(data: data)
            let root = ExportSVGValidator()
            parser.delegate = root
            parser.shouldResolveExternalEntities = false
            guard parser.parse(), root.valid else { throw CocoaError(.fileReadCorruptFile) }
        }
        return Self(data: data, fileExtension: ext, mediaType: mime, width: width, height: height)
    }
}

private final class ExportSVGValidator: NSObject, XMLParserDelegate {
    var valid = false
    private var first = true
    func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String]) {
        if first { valid = name == "svg"; first = false }
    }
}
