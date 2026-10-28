// EditorBridge:protocol.ts 的 Rust 实现。
// M1 最小集;M2(Task 8)扩展为 protocol.ts 全 12+31 消息。
pub mod messages;

/// 把已序列化的 JSON 文本转成 JS 单引号字符串字面量的安全内容。
/// 宿主→前端的注入形如 `window.postMessage(JSON.parse('<escaped>'), '*')`
/// (与 macOS EditorSession.swift:896、Android EditorBridge.kt:95 同构)。
/// 本函数只允许出现 JS 单引号字面量合法的转义:\' \\ \n \r \t \uXXXX;
/// 其余 <0x20 控制字符一律转 \uXXXX。
pub fn escape_for_js_literal(text: &str) -> String {
    let mut out = String::with_capacity(text.len() + 8);
    for ch in text.chars() {
        match ch {
            '\\' => out.push_str("\\\\"),
            '\'' => out.push_str("\\'"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out
}

/// 按 JS 单引号字面量语义还原转义(仅覆盖 escape_for_js_literal 可能产生的
/// 转义序列),用于测试 round-trip——等价于在 JS 里执行 JSON.parse('…') 的
/// 字符串解析阶段。
#[cfg(test)]
fn js_unescape_literal(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut chars = text.chars();
    while let Some(ch) = chars.next() {
        if ch != '\\' {
            out.push(ch);
            continue;
        }
        match chars.next() {
            Some('\'') => out.push('\''),
            Some('\\') => out.push('\\'),
            Some('n') => out.push('\n'),
            Some('r') => out.push('\r'),
            Some('t') => out.push('\t'),
            Some('u') => {
                let hex: String = chars.by_ref().take(4).collect();
                let code = u32::from_str_radix(&hex, 16).expect("valid hex escape");
                out.push(char::from_u32(code).expect("valid code point"));
            }
            other => panic!("非本桥产生的转义序列:{other:?}"),
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn escapes_single_quotes_backslashes_and_newlines() {
        let escaped = escape_for_js_literal("it's a \\ back\nline\r\nend");
        assert!(escaped.contains("it\\'s"));
        assert!(escaped.contains("\\\\ back"));
        assert!(escaped.contains("\\nline\\r\\nend"));
        assert!(!escaped.contains('\n'));
    }

    #[test]
    fn round_trips_through_js_literal_semantics() {
        let cases = [
            "plain",
            "it's",
            r"back\slash",
            "line1\nline2\r\nline3\ttabbed",
            "中文与 😀 emoji",
            "{\"json\":\"with 'quotes' and \\\"escapes\\\"\"}",
            "\u{1}\u{2}control\u{1f}",
            "",
        ];
        for case in cases {
            let escaped = escape_for_js_literal(case);
            // 模拟 JSON.parse('<escaped>') 的字符串解析阶段。
            assert_eq!(js_unescape_literal(&escaped), case, "round trip 失败:{case:?}");
        }
    }
}
