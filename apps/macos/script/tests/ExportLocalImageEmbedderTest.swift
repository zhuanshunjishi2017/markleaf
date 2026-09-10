import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func write(_ bytes: [UInt8], to url: URL) throws {
    try Data(bytes).write(to: url)
}

let root = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let documentDirectory = root.appendingPathComponent("文 档", isDirectory: true)
let assetDirectory = documentDirectory.appendingPathComponent("assets", isDirectory: true)
try FileManager.default.createDirectory(at: assetDirectory, withIntermediateDirectories: true)
let documentURL = documentDirectory.appendingPathComponent("说明.md")
try Data("# test".utf8).write(to: documentURL)

let pngURL = assetDirectory.appendingPathComponent("图 片.png")
let jpegURL = assetDirectory.appendingPathComponent("100% & 图.jpg")
try write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], to: pngURL)
try write([0xff, 0xd8, 0xff, 0xd9], to: jpegURL)

do {
    let html = #"<p><img alt="png" src="assets/%E5%9B%BE%20%E7%89%87.png"><img src='assets/100%25%20&amp;%20%E5%9B%BE.jpg'></p>"#
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(
        result.html == #"<p><img alt="png" src="data:image/png;base64,iVBORw0KGgo="><img src='data:image/jpeg;base64,/9j/2Q=='></p>"#,
        "relative encoded paths must produce exact PNG/JPEG data URIs while preserving HTML"
    )
    expect(result.embeddedCount == 2, "relative files must both be embedded")
    expect(result.unresolvedCount == 0, "successful local files must not produce warnings")
}

do {
    let encodedPath = jpegURL.path.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed)!
        .replacingOccurrences(of: "&", with: "%26")
    let html = "<img src=\"https://assets.local/image?path=\(encodedPath)&amp;cache=0\">"
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.html == #"<img src="data:image/jpeg;base64,/9j/2Q==">"#,
           "assets.local query paths must decode percent escapes and HTML entities")
    expect(result.unresolvedCount == 0, "virtual local URL must embed without warning")
}

do {
    let html = "<img src=\"\(pngURL.path)\"><img src='\(pngURL.absoluteString)'>"
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.html == #"<img src="data:image/png;base64,iVBORw0KGgo="><img src='data:image/png;base64,iVBORw0KGgo='>"#,
           "absolute local paths and file URLs must embed using the same exact bytes")
    expect(result.embeddedCount == 2, "absolute local references must both be embedded")
    expect(result.unresolvedCount == 0, "readable absolute local references must not warn")
}

do {
    let literalPercentURL = assetDirectory.appendingPathComponent("literal%20.png")
    try write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], to: literalPercentURL)
    let encodedPath = literalPercentURL.path.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed)!
    let html = "<img src=\"https://assets.local/image?path=\(encodedPath)\">"
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.html == #"<img src="data:image/png;base64,iVBORw0KGgo=">"#,
           "a literal percent escape sequence in a filename must be decoded exactly once")
    expect(result.unresolvedCount == 0, "a readable percent-named file must not warn")
}

do {
    let html = #"<img src="data:image/png;base64,AA=="><img src='https://example.com/a.png'><img src=http://example.com/b.jpg>"#
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.html == html, "existing data URIs and remote HTTP(S) URLs must remain byte-for-byte unchanged")
    expect(result.embeddedCount == 0, "preserved resources must not count as embedded")
    expect(result.unresolvedCount == 0, "remote and data resources must not produce warnings")
}

do {
    let unsupportedURL = assetDirectory.appendingPathComponent("shape.svg")
    let unreadableURL = assetDirectory.appendingPathComponent("directory.png", isDirectory: true)
    try Data("<svg/>".utf8).write(to: unsupportedURL)
    try FileManager.default.createDirectory(at: unreadableURL, withIntermediateDirectories: false)
    let html = #"<img src="assets/missing.png"><img src="assets/shape.svg"><img src="assets/directory.png"><img src="file://%ZZ"><img src="ftp://example.com/a.png">"#
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.html == html, "failed local and unsupported image references must remain unchanged")
    expect(result.unresolvedCount == 5, "all five unsupported/missing/unreadable/malformed sources must be reported")
    expect(result.issues.map(\.reason) == [.missing, .unsupportedType, .unreadable, .malformed, .unsupportedSource],
           "issues must be deterministic and distinguish failure reasons")
}

do {
    let fileURL = pngURL.absoluteString
    let html = """
    <source src="assets/%E5%9B%BE%20%E7%89%87.png">
    <script>const sample = '<img src="assets/%E5%9B%BE%20%E7%89%87.png">'</script>
    <style>.sample::before { content: '<img src="assets/%E5%9B%BE%20%E7%89%87.png">' }</style>
    <IMG data-x="1" src="assets/%E5%9B%BE%20%E7%89%87.png">
    <img src='\(fileURL)'>
    <img src=assets/%E5%9B%BE%20%E7%89%87.png>
    """
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.html.contains(#"<source src="assets/%E5%9B%BE%20%E7%89%87.png">"#),
           "non-image src attributes must stay unchanged")
    expect(result.html.contains(#"const sample = '<img src="assets/%E5%9B%BE%20%E7%89%87.png">'"#),
           "script text resembling an image tag must stay unchanged")
    expect(result.html.contains(#"content: '<img src="assets/%E5%9B%BE%20%E7%89%87.png">'"#),
           "style text resembling an image tag must stay unchanged")
    expect(result.embeddedCount == 3, "double-quoted, single-quoted and unquoted img src values must embed")
    expect(result.unresolvedCount == 0, "supported attribute forms must not warn")
}

do {
    let html = #"<!-- <img src="assets/missing.png"> --><img src="assets/%E5%9B%BE%20%E7%89%87.png"><img src="assets/missing.png"><img src="https://example.com/remote.png">"#
    let result = ExportLocalImageEmbedder.embed(in: html, documentURL: documentURL)
    expect(result.embeddedCount == 1, "mixed input must embed the one readable local image")
    expect(result.unresolvedCount == 1, "mixed input must report only the missing local image")
    expect(result.issues.first?.source == "assets/missing.png", "issue order must follow document order")
    expect(result.html.contains("<!-- <img src=\"assets/missing.png\"> -->"), "comments must remain unchanged")
    expect(result.html.contains("https://example.com/remote.png"), "remote image must remain unchanged")
}

do {
    let result = ExportLocalImageEmbedder.embed(in: #"<img src="assets/%E5%9B%BE%20%E7%89%87.png">"#, documentURL: nil)
    expect(result.unresolvedCount == 1, "a relative local image without a saved document base must be reported")
    expect(result.issues.first?.reason == .missingBaseURL, "missing document base must have a structured reason")
}

print("PASS")
