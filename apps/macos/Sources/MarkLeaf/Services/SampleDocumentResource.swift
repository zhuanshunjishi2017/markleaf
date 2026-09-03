import Foundation

/// Windows 1.6.0 示例文档的稳定标识、资源路径与菜单命令映射。
enum SampleDocumentResource: String, CaseIterable {
    case alertExamples = "alert-examples"
    case yamlBasic = "yaml-front-matter-basic"
    case yamlAdvanced = "yaml-front-matter-advanced"

    var fileName: String {
        "\(rawValue).md"
    }

    var titleKey: String {
        switch self {
        case .alertExamples: return "示例：提示框"
        case .yamlBasic: return "示例：YAML 基础"
        case .yamlAdvanced: return "示例：YAML 进阶"
        }
    }

    init?(command: String) {
        switch command {
        case "openSampleAlert": self = .alertExamples
        case "openSampleYamlBasic": self = .yamlBasic
        case "openSampleYamlAdvanced": self = .yamlAdvanced
        default: return nil
        }
    }

    func bundledURL(in bundle: Bundle) -> URL? {
        bundle.url(
            forResource: rawValue,
            withExtension: "md",
            subdirectory: "Samples"
        )
    }

    func cachedURL(cacheDirectory: URL) -> URL {
        cacheDirectory.appendingPathComponent(fileName)
    }
}
