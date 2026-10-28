// 编码检测/回写测试:对齐 macOS DocumentEncodingPolicy 行径
// (BOM 优先 → 严格 UTF-8 → 候选打分;有损不产出替代成功)。
use markleaf::documents::encoding::{detect, encode, apply_newline, detect_newline, NewLinePolicy};

fn utf8(s: &str) -> Vec<u8> {
    s.as_bytes().to_vec()
}

#[test]
fn utf8_passes_through_strictly() {
    let detection = detect(&utf8("# 标题\n\n中文正文"));
    assert_eq!(detection.encoding.name(), "UTF-8");
    assert!(!detection.had_bom);
    assert!(!detection.lossy);
    assert_eq!(detection.decoded, "# 标题\n\n中文正文");
}

#[test]
fn utf8_bom_is_recognized_and_stripped() {
    let mut bytes = vec![0xEF, 0xBB, 0xBF];
    bytes.extend_from_slice("正文".as_bytes());
    let detection = detect(&bytes);
    assert!(detection.had_bom);
    assert_eq!(detection.encoding.name(), "UTF-8");
    assert_eq!(detection.decoded, "正文");
}

#[test]
fn gbk_bytes_decode_as_gb18030_without_loss() {
    // iconv -f UTF-8 -t GBK 「中文测试」
    let gbk: [u8; 8] = [0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4];
    let detection = detect(&gbk);
    assert!(!detection.lossy, "GBK 应被识别为无损:{}", detection.encoding.name());
    assert_eq!(detection.decoded, "中文测试");
}

#[test]
fn random_binary_is_flagged_lossy_not_silently_substituted() {
    // 纯二进制噪声:任何编码都无法无损解读,必须给出 lossy 标志。
    let noise: Vec<u8> = (0..=255u8).cycle().take(512).collect();
    let detection = detect(&noise);
    // 语义红线:不允许"用替代字符假装成功"。
    if detection.decoded.contains('\u{FFFD}') {
        assert!(detection.lossy, "含 U+FFFD 却未标记 lossy = 静默替代成功");
    }
}

#[test]
fn invalid_utf8_sequences_reject_utf8() {
    // 0xC0 0x80 是非法的过长编码,严格 UTF-8 校验必须拒绝。
    let bytes = [0xC0, 0x80, 0x41, 0x42];
    let detection = detect(&bytes);
    assert_ne!(detection.encoding.name(), "UTF-8");
}

#[test]
fn utf16le_bom_is_recognized() {
    let mut bytes = vec![0xFF, 0xFE];
    bytes.extend("ab".encode_utf16().flat_map(|u| u.to_le_bytes()).collect::<Vec<u8>>());
    let detection = detect(&bytes);
    assert!(detection.had_bom);
    assert_eq!(detection.decoded, "ab");
}

#[test]
fn round_trip_encode_preserves_content() {
    let text = "中文 English 混排\n第二行";
    let bytes = encode(text, encoding_rs::UTF_8, false);
    assert_eq!(detect(&bytes).decoded, text);
}

#[test]
fn newline_policies_detect_and_apply() {
    assert_eq!(detect_newline("a\nb\n"), NewLinePolicy::Lf);
    assert_eq!(detect_newline("a\r\nb\r\n"), NewLinePolicy::Crlf);
    assert_eq!(detect_newline("a\rb\r"), NewLinePolicy::Cr);
    assert_eq!(detect_newline("a\r\nb\n"), NewLinePolicy::Mixed);
    assert_eq!(detect_newline(""), NewLinePolicy::Lf);
    assert_eq!(apply_newline("a\nb", NewLinePolicy::Crlf), "a\r\nb");
    assert_eq!(apply_newline("a\r\nb", NewLinePolicy::Lf), "a\nb");
    // Mixed 保持原样(见 kernel-boundaries:混合换行在明确选择前保持)。
    let mixed = "a\r\nb\n";
    assert_eq!(apply_newline(mixed, NewLinePolicy::Mixed), mixed);
}
