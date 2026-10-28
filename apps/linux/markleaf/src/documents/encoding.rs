// 编码检测与回写(Rust 原生,镜像 macOS DocumentEncodingPolicy 路径):
// BOM 优先 → 严格 UTF-8 校验 → chardetng 候选打分 → 有损判断。
// 红线:有损转换不产出替代成功(lossy 标志必须如实),不用 UTF-8
// 替代数据制造成功(spec §9)。
use encoding_rs::Encoding;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum NewLinePolicy {
    Lf,
    Crlf,
    Cr,
    Mixed,
}

impl NewLinePolicy {
    pub fn as_str(&self) -> &'static str {
        match self {
            NewLinePolicy::Lf => "LF",
            NewLinePolicy::Crlf => "CRLF",
            NewLinePolicy::Cr => "CR",
            NewLinePolicy::Mixed => "MIXED",
        }
    }
}

#[derive(Debug, Clone)]
pub struct EncodingDetection {
    pub encoding: &'static Encoding,
    pub had_bom: bool,
    pub lossy: bool,
    pub decoded: String,
}

impl EncodingDetection {
    /// 编码名(协议/设置用,IETF 名称,如 UTF-8/GBK/GB18030)。
    pub fn encoding_name(&self) -> &'static str {
        self.encoding.name()
    }
}

/// 检测字节序列的编码并解码。
/// 顺序:UTF-8/UTF-16 BOM → 严格 UTF-8 → chardetng 候选(无损校验)→ Latin-1 兜底(lossy)。
pub fn detect(bytes: &[u8]) -> EncodingDetection {
    // 1) BOM 优先(镜像 macOS DocumentEncodingPolicy)。
    if bytes.starts_with(&[0xEF, 0xBB, 0xBF]) {
        let (decoded, _, had_errors) = encoding_rs::UTF_8.decode(&bytes[3..]);
        return EncodingDetection {
            encoding: encoding_rs::UTF_8,
            had_bom: true,
            lossy: had_errors,
            decoded: decoded.into_owned(),
        };
    }
    if bytes.starts_with(&[0xFF, 0xFE]) {
        let (decoded, _, had_errors) = encoding_rs::UTF_16LE.decode(bytes);
        return EncodingDetection {
            encoding: encoding_rs::UTF_16LE,
            had_bom: true,
            lossy: had_errors,
            decoded: decoded.into_owned(),
        };
    }
    if bytes.starts_with(&[0xFE, 0xFF]) {
        let (decoded, _, had_errors) = encoding_rs::UTF_16BE.decode(bytes);
        return EncodingDetection {
            encoding: encoding_rs::UTF_16BE,
            had_bom: true,
            lossy: had_errors,
            decoded: decoded.into_owned(),
        };
    }

    // 2) 严格 UTF-8(替换错误即非 UTF-8)。
    if let Ok(text) = std::str::from_utf8(bytes) {
        return EncodingDetection {
            encoding: encoding_rs::UTF_8,
            had_bom: false,
            lossy: false,
            decoded: text.to_string(),
        };
    }

    // 3) chardetng 候选探测:解码后逐字节回编,无损才接受。
    let mut detector = chardetng::EncodingDetector::new();
    detector.feed(bytes, true);
    let encoding = detector.guess(None, true);
    let (decoded, _, had_errors) = encoding.decode(bytes);
    if !had_errors {
        // 无损校验:回编必须逐字节还原(GBK ↔ UTF-8 往返歧义防护)。
        let (re_encoded, _, _) = encoding.encode(&decoded);
        if re_encoded.as_ref() == bytes {
            return EncodingDetection {
                encoding,
                had_bom: false,
                lossy: false,
                decoded: decoded.into_owned(),
            };
        }
    }

    // 4) 兜底:Latin-1 逐字节映射(必然"成功"),如实标记 lossy。
    let (decoded, _, _) = encoding_rs::WINDOWS_1252.decode(bytes);
    EncodingDetection {
        encoding: encoding_rs::WINDOWS_1252,
        had_bom: false,
        lossy: true,
        decoded: decoded.into_owned(),
    }
}

/// 按编码回写(可选 BOM)。
pub fn encode(text: &str, encoding: &'static Encoding, bom: bool) -> Vec<u8> {
    let mut bytes = Vec::new();
    if bom && encoding == encoding_rs::UTF_8 {
        bytes.extend_from_slice(&[0xEF, 0xBB, 0xBF]);
    } else if bom && encoding == encoding_rs::UTF_16LE {
        bytes.extend_from_slice(&[0xFF, 0xFE]);
    }
    let (encoded, _, _) = encoding.encode(text);
    bytes.extend_from_slice(&encoded);
    bytes
}

/// 检测换行策略(镜像 DocumentNewLinePolicy:混合在明确选择前保持)。
pub fn detect_newline(text: &str) -> NewLinePolicy {
    let has_crlf = text.contains("\r\n");
    let has_lone_lf = text.replace("\r\n", "").contains('\n');
    let has_cr = text.replace("\r\n", "").contains('\r');
    match (has_crlf, has_lone_lf, has_cr) {
        (true, false, false) => NewLinePolicy::Crlf,
        (false, true, false) => NewLinePolicy::Lf,
        (false, false, true) => NewLinePolicy::Cr,
        (false, false, false) => NewLinePolicy::Lf,
        _ => NewLinePolicy::Mixed,
    }
}

/// 按策略统一换行;Mixed 原样返回。
pub fn apply_newline(text: &str, policy: NewLinePolicy) -> String {
    match policy {
        NewLinePolicy::Mixed => text.to_string(),
        NewLinePolicy::Lf => text.replace("\r\n", "\n").replace('\r', "\n"),
        NewLinePolicy::Crlf => apply_newline(text, NewLinePolicy::Lf)
            .split('\n')
            .collect::<Vec<_>>()
            .join("\r\n"),
        NewLinePolicy::Cr => apply_newline(text, NewLinePolicy::Lf).replace('\n', "\r"),
    }
}
