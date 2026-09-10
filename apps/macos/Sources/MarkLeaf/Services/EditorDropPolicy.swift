import Foundation

/// 空编辑区和 WebView 共用的文件拖放分类规则。
enum EditorDropPolicy {
    struct Drop: Equatable {
        var images: [URL] = []
        var documents: [URL] = []

        var isEmpty: Bool { images.isEmpty && documents.isEmpty }
    }

    static let imageExtensions: Set<String> = ["png", "jpg", "jpeg", "gif", "webp", "bmp"]
    static let documentExtensions: Set<String> = ["md", "txt", "markdown"]

    static func classify(_ urls: [URL]) -> Drop {
        var drop = Drop()
        for url in urls {
            let ext = url.pathExtension.lowercased()
            if imageExtensions.contains(ext) {
                drop.images.append(url)
            } else if documentExtensions.contains(ext) {
                drop.documents.append(url)
            }
        }
        return drop
    }
}
