import Foundation

/// Platform menu identifiers; detection, BOM and conversion rules belong to DocumentCoreRuntime.
enum DocumentEncodingPolicy: String, CaseIterable, Equatable {
    case utf8 = "UTF-8"
    case utf8BOM = "UTF-8 with BOM"
    case utf16NoBOM = "UTF-16"
    case utf16 = "UTF-16 with BOM"
    case utf16BENoBOM = "UTF-16 BE"
    case utf16BE = "UTF-16 BE with BOM"
    case gb18030 = "GB18030"
    case gbk = "GBK"
    case gb2312 = "GB2312"
    case big5 = "Big5"
    case shiftJIS = "Shift_JIS"
    case usASCII = "US-ASCII"

    static var orderedRawValues: [String] {
        let catalog: [KernelEncoding] = DocumentCoreRuntime.shared.require("encodings")
        return catalog.map(\.label)
    }
    static func defaultEncoding(rawValue: String) -> Self {
        let value: KernelEncoding = DocumentCoreRuntime.shared.require("resolveEncoding", ["value": rawValue])
        return Self(rawValue: value.label)!
    }
    static func encode(_ text: String, using encoding: Self) -> Data? {
        guard let bytes: [UInt8] = try? DocumentCoreRuntime.shared.call("encode", ["text": text, "encoding": encoding.rawValue]) else { return nil }
        return Data(bytes)
    }
    static func decode(_ data: Data, using encoding: Self) -> String? {
        try? DocumentCoreRuntime.shared.call("decode", ["bytes": Array(data), "encoding": encoding.rawValue])
    }
    static func reloadWouldRiskGarbling(data: Data, using encoding: Self) -> Bool {
        DocumentCoreRuntime.shared.require("reloadWouldLoseData", ["bytes": Array(data), "encoding": encoding.rawValue])
    }
    static func detect(data: Data) throws -> Self {
        let value: KernelEncoding = try DocumentCoreRuntime.shared.call("detectEncoding", ["bytes": Array(data)])
        return Self(rawValue: value.label)!
    }
}
