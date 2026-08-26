import Foundation

struct EncodingConversionSnapshot: Equatable {
    let markdown: String
    let encoding: DocumentEncodingPolicy
    let isDirty: Bool
}

struct EncodingDirectReadSnapshot: Equatable {
    let data: Data
    let markdown: String
}

enum EncodingConversionTransactionError: Error {
    case unrepresentableText
    case unreadableData
}

enum EncodingConversionTransaction {
    typealias AtomicWriter = (_ data: Data, _ destination: URL) throws -> Void

    static func directlyRead(
        from url: URL,
        using target: DocumentEncodingPolicy
    ) throws -> EncodingDirectReadSnapshot {
        let data = try Data(contentsOf: url)
        guard let markdown = DocumentEncodingPolicy.decode(data, using: target) else {
            throw EncodingConversionTransactionError.unreadableData
        }
        return EncodingDirectReadSnapshot(data: data, markdown: markdown)
    }

    static func encodedData(
        markdown: String,
        target: DocumentEncodingPolicy
    ) throws -> Data {
        guard let data = DocumentEncodingPolicy.encode(markdown, using: target) else {
            throw EncodingConversionTransactionError.unrepresentableText
        }
        return data
    }

    static func convert(
        snapshot: EncodingConversionSnapshot,
        target: DocumentEncodingPolicy,
        destination: URL,
        atomicWriter: AtomicWriter = { data, url in
            try data.write(to: url, options: .atomic)
        }
    ) throws -> EncodingConversionSnapshot {
        let data = try encodedData(markdown: snapshot.markdown, target: target)
        try atomicWriter(data, destination)
        return EncodingConversionSnapshot(
            markdown: snapshot.markdown,
            encoding: target,
            isDirty: false
        )
    }
}
