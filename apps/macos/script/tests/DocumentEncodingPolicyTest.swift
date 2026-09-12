import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(DocumentEncodingPolicy.orderedRawValues == [
    "UTF-8", "UTF-8 with BOM", "UTF-16", "UTF-16 with BOM",
    "GB18030", "GBK", "GB2312", "Big5", "Shift_JIS", "US-ASCII"
], "encoding menu should group Unicode, then Chinese (GB18030/GBK/GB2312), and keep US-ASCII last")

let samples: [DocumentEncodingPolicy: String] = [
    .utf8: "中文、日本語、繁體",
    .utf8BOM: "中文、日本語、繁體",
    .usASCII: "ASCII only",
    .utf16: "中文、日本語、繁體",
    .utf16NoBOM: "中文、日本語、繁體",
    .gb2312: "中文简体",
    .gbk: "中文简体",
    .gb18030: "中文简体",
    .big5: "繁體中文",
    .shiftJIS: "日本語",
]
for encoding in DocumentEncodingPolicy.allCases {
    let source = samples[encoding]!
    guard let data = DocumentEncodingPolicy.encode(source, using: encoding),
          let decoded = DocumentEncodingPolicy.decode(data, using: encoding) else {
        fatalError("round trip failed for \(encoding.rawValue)")
    }
    expect(decoded == source, "round trip should preserve text for \(encoding.rawValue)")
    expect(!DocumentEncodingPolicy.reloadWouldRiskGarbling(data: data, using: encoding),
           "matching encoding should not be marked risky")
}

let gb18030Data = DocumentEncodingPolicy.encode("中文𠀀", using: .gb18030)!
expect(DocumentEncodingPolicy.detect(data: gb18030Data) == .gb18030,
       "GB18030 bytes should be detected as GB18030")
let gb2312Data = DocumentEncodingPolicy.encode(samples[.gb2312]!, using: .gb2312)!
expect(DocumentEncodingPolicy.detect(data: gb2312Data) == .gb2312,
       "GB2312 bytes should prefer the narrowest matching Chinese encoding")
let gbkData = DocumentEncodingPolicy.encode("中文龘", using: .gbk)!
expect(DocumentEncodingPolicy.detect(data: gbkData) == .gbk,
       "GBK-only bytes should be detected as GBK")

let utf8BOMData = DocumentEncodingPolicy.encode(samples[.utf8BOM]!, using: .utf8BOM)!
expect(utf8BOMData.starts(with: [0xEF, 0xBB, 0xBF]), "UTF-8 with BOM should write a BOM")
expect(DocumentEncodingPolicy.detect(data: utf8BOMData) == .utf8BOM,
       "UTF-8 BOM should be detected as the BOM variant")

let utf16Data = DocumentEncodingPolicy.encode(samples[.utf16]!, using: .utf16)!
expect(utf16Data.starts(with: [0xFF, 0xFE]), "UTF-16 should write a little-endian BOM")
expect(DocumentEncodingPolicy.detect(data: utf16Data) == .utf16,
       "UTF-16 BOM should be detected as UTF-16")
let utf16NoBOMData = DocumentEncodingPolicy.encode(samples[.utf16NoBOM]!, using: .utf16NoBOM)!
expect(!utf16NoBOMData.starts(with: [0xFF, 0xFE]), "UTF-16 without BOM should omit the BOM")

expect(DocumentEncodingPolicy.encode("中文", using: .usASCII) == nil,
       "US-ASCII should reject characters it cannot represent")
expect(!DocumentEncodingPolicy.reloadWouldRiskGarbling(data: utf8BOMData, using: .utf8),
       "changing only the UTF-8 BOM marker should not be considered garbling")

let utf8Data = DocumentEncodingPolicy.encode(samples[.utf8]!, using: .utf8)!
expect(DocumentEncodingPolicy.reloadWouldRiskGarbling(data: utf8Data, using: .shiftJIS),
       "decoding UTF-8 bytes as Shift_JIS should be marked risky")
expect(DocumentEncodingPolicy.defaultEncoding(rawValue: "unknown") == .utf8,
       "unknown defaults should fall back to UTF-8")

print("PASS")
