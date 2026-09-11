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

enum EncodingConversionTransaction {
    typealias AtomicWriter = (_ data: Data, _ destination: URL) throws -> Void

    static func directlyRead(
        from url: URL,
        using target: DocumentEncodingPolicy
    ) throws -> EncodingDirectReadSnapshot {
        let data = try Data(contentsOf: url)
        let markdown: String = try DocumentCoreRuntime.shared.call("decode", ["bytes": Array(data), "encoding": target.rawValue])
        return EncodingDirectReadSnapshot(data: data, markdown: markdown)
    }

    static func encodedData(
        markdown: String,
        target: DocumentEncodingPolicy
    ) throws -> Data {
        let bytes: [UInt8] = try DocumentCoreRuntime.shared.call("encode", ["text": markdown, "encoding": target.rawValue])
        return Data(bytes)
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
