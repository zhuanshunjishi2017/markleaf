import Foundation

/// Markdown 图片路径序列化规则（对齐 Windows 1.7.x ImageAssetService）：
/// 路径保持人类可读，只有空格写入 `%20`；不做完整 URI 百分号编码。
enum MarkdownImagePathPolicy {
    static func relative(
        documentPath: String,
        filePath: String,
        prefixDotSlash: Bool
    ) -> String? {
        let documentDirectory = URL(fileURLWithPath: documentPath)
            .deletingLastPathComponent()
            .standardizedFileURL.path
        let target = URL(fileURLWithPath: filePath).standardizedFileURL.path
        guard target.hasPrefix(documentDirectory + "/") else { return nil }

        var relative = String(target.dropFirst(documentDirectory.count + 1))
        if prefixDotSlash {
            relative = "./" + relative
        }
        return encode(relative)
    }

    static func absolute(_ path: String) -> String {
        encode(URL(fileURLWithPath: path).standardizedFileURL.path)
    }

    static func encode(_ path: String) -> String {
        path.replacingOccurrences(of: " ", with: "%20")
    }
}
