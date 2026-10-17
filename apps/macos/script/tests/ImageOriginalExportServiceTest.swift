import Foundation

@main struct ImageOriginalExportServiceTest {
    static func main() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let bytes = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jX1sAAAAASUVORK5CYII=")!
        let source = root.appendingPathComponent("original.unknown")
        try bytes.write(to: source)
        let result = try await ImageOriginalExportService.read(source: source.path, documentURL: nil)
        precondition(result.data == bytes && result.fileExtension == "png" && result.mediaType == "image/png")
        let target = root.appendingPathComponent("saved.png")
        try ImageOriginalExportService.write(result, to: target)
        let saved = try Data(contentsOf: target)
        precondition(saved == bytes)
        let invalid = root.appendingPathComponent("error.png")
        try Data("<html>Error</html>".utf8).write(to: invalid)
        do { _ = try await ImageOriginalExportService.read(source: invalid.path, documentURL: nil); fatalError("HTML accepted") } catch {}
        do { try ImageOriginalExportService.write(result, to: root.appendingPathComponent("missing/image.png")); fatalError("write succeeded") } catch {}
        let preserved = try Data(contentsOf: target)
        precondition(preserved == bytes)
        print("PASS")
    }
}
