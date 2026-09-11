import Foundation
func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
let catalog: [KernelEncoding] = try DocumentCoreRuntime.shared.call("encodings")
expect(DocumentEncodingPolicy.orderedRawValues == catalog.map(\.label), "native menus consume the core catalog")
let samples: [DocumentEncodingPolicy: String] = [
    .utf8: "中文 😀", .utf8BOM: "中文 😀", .utf16: "中文 😀", .utf16NoBOM: "中文 😀",
    .utf16BE: "中文 😀", .utf16BENoBOM: "中文 😀", .gb18030: "中文𠀀", .gbk: "中文",
    .gb2312: "中文", .big5: "繁體中文", .shiftJIS: "日本語", .usASCII: "ASCII"
]
for (encoding, text) in samples {
    let data = DocumentEncodingPolicy.encode(text, using: encoding)!
    expect(DocumentEncodingPolicy.decode(data, using: encoding) == text, "OS codec roundtrip: \(encoding)")
}
expect(DocumentEncodingPolicy.encode("中文", using: .usASCII) == nil, "OS codec must reject unrepresentable text")
let decoded: KernelDocument = try DocumentCoreRuntime.shared.call("read", ["bytes": [254, 255, 0, 65]])
expect(decoded.text == "A" && decoded.encoding.id == "utf-16be-bom", "native adapter preserves the core's byte-order result")
print("PASS")
