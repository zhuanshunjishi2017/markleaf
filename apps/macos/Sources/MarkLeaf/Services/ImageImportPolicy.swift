import Foundation
import UniformTypeIdentifiers

/// Keep native pickers and file imports limited to formats supported by the editor.
enum ImageImportPolicy {
    static let contentTypes: [UTType] = [.png, .jpeg, .gif, .bmp, UTType(filenameExtension: "webp") ?? .png]

    static func supports(_ url: URL) -> Bool {
        guard let type = UTType(filenameExtension: url.pathExtension.lowercased()) else { return false }
        return contentTypes.contains { type.conforms(to: $0) }
    }
}
