import Foundation
import CoreFoundation

/// 文档文件编码。状态栏与偏好设置共用这组编码，并明确区分 BOM 变体。
enum DocumentEncodingPolicy: String, CaseIterable, Equatable {
    case utf8 = "UTF-8"
    case utf8BOM = "UTF-8 with BOM"
    case usASCII = "US-ASCII"
    case utf16 = "UTF-16 with BOM"
    case utf16NoBOM = "UTF-16"
    case gb2312 = "GB2312"
    case gbk = "GBK"
    case gb18030 = "GB18030"
    case big5 = "Big5"
    case shiftJIS = "Shift_JIS"

    static var orderedRawValues: [String] { allCases.map(\.rawValue) }

    static func defaultEncoding(rawValue: String) -> Self {
        if rawValue.caseInsensitiveCompare("UTF-8 without BOM") == .orderedSame {
            return .utf8
        }
        if rawValue.caseInsensitiveCompare("UTF-16 without BOM") == .orderedSame {
            return .utf16NoBOM
        }
        return allCases.first { $0.rawValue.caseInsensitiveCompare(rawValue) == .orderedSame } ?? .utf8
    }

    private var ianaName: String {
        switch self {
        case .utf8, .utf8BOM: return "utf-8"
        case .usASCII: return "us-ascii"
        case .utf16, .utf16NoBOM: return "utf-16le"
        case .gb2312: return "gb2312"
        case .gbk: return "gbk"
        case .gb18030: return "gb18030"
        case .big5: return "big5"
        case .shiftJIS: return "shift_jis"
        }
    }

    private var stringEncoding: String.Encoding? {
        let cfEncoding = CFStringConvertIANACharSetNameToEncoding(ianaName as CFString)
        guard cfEncoding != kCFStringEncodingInvalidId else { return nil }
        return String.Encoding(rawValue: CFStringConvertEncodingToNSStringEncoding(cfEncoding))
    }

    static func encode(_ text: String, using encoding: Self) -> Data? {
        guard let body = text.data(using: encoding.stringEncoding ?? .utf8, allowLossyConversion: false) else {
            return nil
        }
        switch encoding {
        case .utf8BOM:
            return Data([0xEF, 0xBB, 0xBF]) + body
        case .utf16:
            return Data([0xFF, 0xFE]) + body
        default:
            return body
        }
    }

    static func decode(_ data: Data, using encoding: Self) -> String? {
        switch encoding {
        case .utf8, .utf8BOM:
            let body = data.starts(with: [0xEF, 0xBB, 0xBF]) ? data.dropFirst(3) : data[...]
            return String(data: Data(body), encoding: .utf8)
        case .utf16:
            if data.starts(with: [0xFF, 0xFE]) {
                return String(data: Data(data.dropFirst(2)), encoding: .utf16LittleEndian)
            }
            if data.starts(with: [0xFE, 0xFF]) {
                return String(data: Data(data.dropFirst(2)), encoding: .utf16BigEndian)
            }
            return String(data: data, encoding: .utf16LittleEndian)
        case .utf16NoBOM:
            let body = data.starts(with: [0xFF, 0xFE]) || data.starts(with: [0xFE, 0xFF])
                ? data.dropFirst(2) : data[...]
            return String(data: Data(body), encoding: .utf16LittleEndian)
        default:
            guard let stringEncoding = encoding.stringEncoding else { return nil }
            return String(data: data, encoding: stringEncoding)
        }
    }

    static func reloadWouldRiskGarbling(data: Data, using encoding: Self) -> Bool {
        guard let decoded = decode(data, using: encoding),
              !decoded.contains("\u{FFFD}"),
              let roundTrip = encode(decoded, using: encoding) else {
            return true
        }
        return normalizedBytes(data, encoding: encoding) != normalizedBytes(roundTrip, encoding: encoding)
    }

    static func detect(data: Data) -> Self {
        if data.starts(with: [0xEF, 0xBB, 0xBF]) { return .utf8BOM }
        if data.starts(with: [0xFF, 0xFE]) || data.starts(with: [0xFE, 0xFF]) { return .utf16 }
        if looksLikeUTF16WithoutBOM(data),
           !reloadWouldRiskGarbling(data: data, using: .utf16NoBOM) {
            return .utf16NoBOM
        }
        // UTF-8 是严格自校验的；只要完整有效，就优先于兼容性更强的中文本地编码。
        if !reloadWouldRiskGarbling(data: data, using: .utf8) {
            return .utf8
        }

        // 其余候选编码可能都能解码一部分字节，因此按可逆性和乱码特征评分，
        // 而不是简单取固定列表中的第一项。
        let candidates: [Self] = [.gb2312, .gbk, .gb18030, .big5, .shiftJIS]
        var best: (encoding: Self, score: Int)?
        for encoding in candidates {
            guard let text = decodedRoundTripText(data: data, using: encoding) else { continue }
            let score = plausibilityScore(text, for: encoding)
            if best == nil || score > best!.score {
                best = (encoding, score)
            }
        }
        return best?.encoding ?? .utf8
    }

    private static func decodedRoundTripText(data: Data, using encoding: Self) -> String? {
        guard let text = decode(data, using: encoding),
              !text.contains("\u{FFFD}"),
              let roundTrip = encode(text, using: encoding),
              normalizedBytes(data, encoding: encoding) == normalizedBytes(roundTrip, encoding: encoding) else {
            return nil
        }
        return text
    }

    private static func plausibilityScore(_ text: String, for encoding: Self) -> Int {
        var score = 0
        var cjkCount = 0
        var kanaCount = 0
        for scalar in text.unicodeScalars {
            let value = scalar.value
            if value == 0 || (value >= 1 && value <= 8) || (value >= 11 && value <= 12) || (value >= 14 && value <= 31) {
                score -= 24
                continue
            }
            if (0x3400...0x4DBF).contains(value) || (0x4E00...0x9FFF).contains(value) {
                cjkCount += 1
                score += 3
            } else if (0x3040...0x30FF).contains(value) || (0xFF65...0xFF9F).contains(value) {
                kanaCount += 1
                score += 2
            } else {
                score += 1
            }
        }
        if encoding == .shiftJIS && kanaCount > 0 { score += 18 }
        if [.gb2312, .gbk, .gb18030, .big5].contains(encoding) && cjkCount > 0 { score += 6 }
        return score
    }

    private static func looksLikeUTF16WithoutBOM(_ data: Data) -> Bool {
        guard data.count >= 4, data.count.isMultiple(of: 2) else { return false }
        let bytes = Array(data)
        let evenNulCount = stride(from: 0, to: bytes.count, by: 2).reduce(into: 0) { count, index in
            if bytes[index] == 0 { count += 1 }
        }
        let oddNulCount = stride(from: 1, to: bytes.count, by: 2).reduce(into: 0) { count, index in
            if bytes[index] == 0 { count += 1 }
        }
        let threshold = max(1, data.count / 8)
        return evenNulCount >= threshold || oddNulCount >= threshold
    }

    private static func normalizedBytes(_ data: Data, encoding: Self) -> Data {
        switch encoding {
        case .utf8, .utf8BOM:
            return data.starts(with: [0xEF, 0xBB, 0xBF]) ? Data(data.dropFirst(3)) : data
        case .utf16, .utf16NoBOM:
            return data.starts(with: [0xFF, 0xFE]) || data.starts(with: [0xFE, 0xFF])
                ? Data(data.dropFirst(2)) : data
        default:
            return data
        }
    }
}
