import Foundation
import ImageIO
import UniformTypeIdentifiers

struct ImageOriginalExport {
    let data: Data
    let fileExtension: String
    let mediaType: String
}

enum ImageOriginalExportService {
    static func read(source: String, documentURL: URL?) async throws -> ImageOriginalExport {
        let data: Data
        if let url = URL(string: source), ["http", "https"].contains(url.scheme?.lowercased() ?? "") {
            var request = URLRequest(url: url)
            request.timeoutInterval = 30
            let (download, response) = try await URLSession.shared.data(for: request)
            guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode),
                  ["http", "https"].contains(http.url?.scheme?.lowercased() ?? "") else { throw CocoaError(.fileReadUnknown) }
            data = download
        } else {
            let decoded = source.removingPercentEncoding ?? source
            let url: URL
            if let parsed = URL(string: source), parsed.isFileURL { url = parsed }
            else if decoded.hasPrefix("/") { url = URL(fileURLWithPath: decoded) }
            else if URL(string: source)?.scheme != nil { throw CocoaError(.fileReadUnsupportedScheme) }
            else if let documentURL { url = documentURL.deletingLastPathComponent().appendingPathComponent(decoded) }
            else { throw CocoaError(.fileNoSuchFile) }
            data = try Data(contentsOf: url)
        }
        try Task.checkCancellation()
        guard !data.isEmpty, data.count <= 64 * 1024 * 1024 else { throw CocoaError(.fileReadTooLarge) }
        if let image = CGImageSourceCreateWithData(data as CFData, nil),
           CGImageSourceGetCount(image) > 0, let identifier = CGImageSourceGetType(image),
           let type = UTType(identifier as String),
           let ext = type.preferredFilenameExtension, let mime = type.preferredMIMEType {
            return ImageOriginalExport(data: data, fileExtension: ext == "jpeg" ? "jpg" : ext, mediaType: mime)
        }
        // ImageIO does not support SVG. Parse without loading external entities.
        let parser = XMLParser(data: data)
        let delegate = SVGRootValidator()
        parser.delegate = delegate
        parser.shouldResolveExternalEntities = false
        if parser.parse(), delegate.isSVG {
            return ImageOriginalExport(data: data, fileExtension: "svg", mediaType: "image/svg+xml")
        }
        throw CocoaError(.fileReadCorruptFile)
    }

    static func write(_ image: ImageOriginalExport, to url: URL) throws {
        try image.data.write(to: url, options: .atomic)
    }
}

private final class SVGRootValidator: NSObject, XMLParserDelegate {
    var isSVG = false
    private var sawRoot = false
    func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String]) {
        if !sawRoot { isSVG = name == "svg"; sawRoot = true }
    }
}
