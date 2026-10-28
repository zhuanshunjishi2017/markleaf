// 协议同步测试:解析 packages/editor-web/src/protocol.ts 源码中的消息 type
// 全集,与 Rust 枚举逐一比对,双向集合相等——杜绝宿主间白名单漂移
// (参照 Windows EditorProtocolSyncTests 的纪律)。
use markleaf::bridge::protocol::{EditorMessageType, HostMessageType};

fn protocol_ts_source() -> String {
    let path = format!(
        "{}/../../../packages/editor-web/src/protocol.ts",
        env!("CARGO_MANIFEST_DIR")
    );
    std::fs::read_to_string(&path)
        .unwrap_or_else(|err| panic!("读取 {path} 失败:{err}(请从仓库根运行)"))
}

/// 抽取 HostMessage type 联合(isHostMessage 的 `message.type === '…'` 链)。
/// split 的第 0 段是首个匹配前的文本,必须跳过。
fn extract_host_types(source: &str) -> Vec<String> {
    let mut types = Vec::new();
    for line in source.lines() {
        for (idx, part) in line.split("message.type === '").enumerate() {
            if idx == 0 {
                continue;
            }
            if let Some(end) = part.find('\'') {
                types.push(part[..end].to_string());
            }
        }
    }
    types.sort();
    types.dedup();
    types
}

/// 抽取 EditorMessage type 联合的 | 'xxx' 字面量。
/// 锚定 `export type EditorMessage` 之后的第一个 type: 联合,避免命中 HostMessage。
fn extract_editor_types(source: &str) -> Vec<String> {
    let anchor = source
        .find("export type EditorMessage")
        .unwrap_or_else(|| panic!("protocol.ts 缺 export type EditorMessage"));
    let start = source[anchor..]
        .find("type:")
        .map(|offset| anchor + offset)
        .unwrap_or_else(|| panic!("protocol.ts 缺 EditorMessage type 联合"));
    let end = source[start..]
        .find("requestId?: string")
        .unwrap_or_else(|| panic!("protocol.ts 结构变化,无法定位联合结束"));
    let union = &source[start..start + end];
    let mut types = Vec::new();
    for part in union.split('\'') {
        // | 'ready' 形态:单引号之间是类型名;过滤多行空白段。
        let candidate = part.trim();
        if candidate.len() > 0
            && candidate.chars().all(|c| c.is_ascii_alphabetic() && c.is_lowercase() || c.is_ascii_uppercase())
            && candidate.chars().next().is_some_and(|c| c.is_lowercase())
        {
            types.push(candidate.to_string());
        }
    }
    types.sort();
    types.dedup();
    types
}

#[test]
fn host_types_match_protocol_ts() {
    let source = protocol_ts_source();
    let ts = extract_host_types(&source);
    let rust: Vec<&str> = HostMessageType::all_names().to_vec();
    assert_eq!(ts.len(), rust.len(), "宿主消息数量漂移:ts={ts:?} rust={rust:?}");
    for name in &rust {
        assert!(ts.iter().any(|item| item == name), "protocol.ts 有 {name} 而 Rust 枚举缺");
    }
}

#[test]
fn editor_types_match_protocol_ts() {
    let source = protocol_ts_source();
    let ts = extract_editor_types(&source);
    let rust: Vec<&str> = EditorMessageType::all_names().to_vec();
    assert_eq!(ts.len(), rust.len(), "编辑器消息数量漂移:ts={ts:?} rust={rust:?}");
    for name in &rust {
        assert!(ts.iter().any(|item| item == name), "protocol.ts 有 {name} 而 Rust 枚举缺");
    }
}
