import Foundation

extension SampleDocumentResource {
    var command: String {
        switch self {
        case .alertExamples: return "openSampleAlert"
        case .yamlBasic: return "openSampleYamlBasic"
        case .yamlAdvanced: return "openSampleYamlAdvanced"
        }
    }
}
