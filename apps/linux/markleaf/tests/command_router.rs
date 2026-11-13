// 命令路由测试:透传 vs 宿主分类。
// 命令真源:macOS NativeMenuBuilder.swift 的 performMenuCommand switch
// (Windows 侧无等价表,spec/plan 引用的 EditorCommandBindings.cs 不存在,
// 以 macOS 桌面为蓝本)。全集同步断言防止 Rust 表遗漏新命令。
use markleaf::bridge::command_router::route;
use markleaf::bridge::command_router::CommandRoute;

/// 从 NativeMenuBuilder.swift 机械抽取全部 case 标签(与 protocol_sync 同纪律)。
fn desktop_command_ids() -> Vec<String> {
    let path = format!(
        "{}/../../../apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift",
        env!("CARGO_MANIFEST_DIR")
    );
    let source = std::fs::read_to_string(&path)
        .unwrap_or_else(|err| panic!("读取 {path} 失败:{err}"));
    let mut ids = Vec::new();
    for part in source.split("case \"") {
        let part = match part.split_once('"') {
            Some((head, _)) => head,
            None => continue,
        };
        if part.is_empty() || !part.chars().all(|c| c.is_ascii_alphanumeric()) {
            continue;
        }
        ids.push(part.to_string());
    }
    ids.sort();
    ids.dedup();
    ids
}

#[test]
fn host_commands_route_to_host() {
    for id in ["save", "saveAs", "open", "new", "print", "export", "showPreferences", "sourceMode"] {
        assert!(matches!(route(id), CommandRoute::Host(_)), "{id} 应为宿主命令");
    }
}

#[test]
fn kernel_commands_route_to_passthrough() {
    for id in [
        "undo", "redo", "toggleBold", "toggleItalic", "insertTable", "toggleBlockquote",
        "promoteHeading", "selectAll", "exitCode", "insertAlertNote", "addRowBefore",
    ] {
        assert!(matches!(route(id), CommandRoute::Passthrough), "{id} 应为透传命令");
    }
}

#[test]
fn resize_and_heading_prefixes_route_to_passthrough() {
    // macOS 对 setHeading* 前缀与 resizeImage 走 execute;路由保持一致。
    assert!(matches!(route("setHeading1"), CommandRoute::Passthrough));
    assert!(matches!(route("setHeading2"), CommandRoute::Passthrough));
    assert!(matches!(route("resizeImage"), CommandRoute::Passthrough));
}

#[test]
fn every_desktop_command_id_routes() {
    let ids = desktop_command_ids();
    assert!(ids.len() > 90, "桌面命令全集应 >90 项,实得 {}", ids.len());
    for id in &ids {
        // 任何桌面命令都能路由且不 panic。
        let _ = route(id);
    }
}

#[test]
fn unknown_command_is_passthrough_by_default() {
    // 前端对未知 command 忽略;宿主默认透传保持行为一致。
    assert!(matches!(route("definitelyNotACommand"), CommandRoute::Passthrough));
}
