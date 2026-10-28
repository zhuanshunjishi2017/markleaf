// 快捷键表测试:默认键位对齐桌面、自定义覆盖、冲突检测、accel 解析。
use markleaf::ui::shortcuts::{default_table, resolve_accel, ShortcutConflict, ShortcutTable};

#[test]
fn defaults_cover_core_commands_with_desktop_keys() {
    let table = default_table();
    // 与 macOS ShortcutSettings 对齐的核心键位(Ctrl 代替 Cmd)。
    assert_eq!(table.accel_for("save").as_deref(), Some("<Control>s"));
    assert_eq!(table.accel_for("open").as_deref(), Some("<Control>o"));
    assert_eq!(table.accel_for("new").as_deref(), Some("<Control>n"));
    assert_eq!(table.accel_for("find").as_deref(), Some("<Control>f"));
    assert_eq!(table.accel_for("sourceMode").as_deref(), Some("<Control>e"));
    assert_eq!(table.accel_for("print").as_deref(), Some("<Control>p"));
    assert_eq!(table.accel_for("toggleBold").as_deref(), Some("<Control>b"));
    assert_eq!(table.accel_for("toggleItalic").as_deref(), Some("<Control>i"));
    assert_eq!(table.accel_for("undo").as_deref(), Some("<Control>z"));
    assert_eq!(table.accel_for("redo").as_deref(), Some("<Control><Shift>z"));
}

#[test]
fn custom_override_replaces_default() {
    let mut table = default_table();
    table.bind("save", "<Alt>s").expect("无冲突");
    assert_eq!(table.accel_for("save").as_deref(), Some("<Alt>s"));
}

#[test]
fn conflict_detected_when_accel_already_used() {
    let mut table = default_table();
    // <Control>s 已被 save 占用。
    assert_eq!(
        table.bind("find", "<Control>s"),
        Err(ShortcutConflict { command: "save".into(), accel: "<Control>s".into() })
    );
    // 清除既有绑定后允许重派。
    table.clear_accel("save");
    assert_eq!(table.bind("find", "<Control>s"), Ok(()));
}

#[test]
fn accel_string_normalizes_order() {
    // 显示用字符串与解析等价(顺序无关)。
    assert_eq!(resolve_accel("<Shift><Control>p"), Some(("<Control><Shift>p".into(), "p".into())));
    assert_eq!(resolve_accel("<Control>p"), Some(("<Control>p".into(), "p".into())));
    assert_eq!(resolve_accel("p"), Some(("p".into(), "p".into())), "无修饰键返回完整 accel");
    assert_eq!(resolve_accel("<Control><Control>p"), None, "重复修饰键拒绝");
    assert_eq!(resolve_accel("<Control>"), None, "空键拒绝");
}
