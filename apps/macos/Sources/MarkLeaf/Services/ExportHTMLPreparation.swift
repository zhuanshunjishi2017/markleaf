import Foundation

enum ExportHTMLRoute: CaseIterable, Equatable {
    case preview
    case html
    case pdf
    case print
    case image
}

struct ExportHTMLRequestContext: Equatable {
    let route: ExportHTMLRoute
    let documentURL: URL?
}

struct PreparedExportHTML: Equatable {
    let route: ExportHTMLRoute
    let embedding: ExportLocalImageEmbeddingResult

    var html: String { embedding.html }
    var unresolvedCount: Int { embedding.unresolvedCount }
}

enum ExportHTMLPreparation {
    static func prepare(
        html: String,
        context: ExportHTMLRequestContext,
        embed: (String, URL?) -> ExportLocalImageEmbeddingResult = ExportLocalImageEmbedder.embed
    ) -> PreparedExportHTML {
        let embedding = embed(html, context.documentURL)
        return PreparedExportHTML(route: context.route, embedding: embedding)
    }
}
