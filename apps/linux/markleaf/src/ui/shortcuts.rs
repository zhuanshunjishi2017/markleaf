// 快捷键系统:默认表(对齐 macOS ShortcutSettings,Ctrl 代替 Cmd)、
// 自定义覆盖、冲突检测、GTK accel 字符串解析。
// 设置持久化走 settings.shortcuts(命令 id → accel 字符串)。

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShortcutConflict {
    pub command: String,
    pub accel: String,
}

/// 解析 GTK accel 字符串 → (规范化修饰串, 键);非法输入返回 None。
pub fn resolve_accel(accel: &str) -> Option<(String, String)> {
    let mut ctrl = false;
    let mut shift = false;
    let mut alt = false;
    let key: Option<String> = None;
    let mut rest = accel;
    while let Some(start) = rest.find('<') {
        let after = &rest[start + 1..];
        let end = after.find('>')?;
        let modifier = &after[..end];
        match modifier {
            "Control" if !ctrl => ctrl = true,
            "Shift" if !shift => shift = true,
            "Alt" if !alt => alt = true,
            _ => return None, // 未知或重复修饰键。
        }
        rest = &after[end + 1..];
    }
    let key = key.or_else(|| {
        let trimmed = rest.trim();
        if trimmed.is_empty() || trimmed.contains('<') {
            None
        } else {
            Some(trimmed.to_string())
        }
    })?;
    // 规范化顺序:Control → Shift → Alt;返回完整 accel(修饰串+键)。
    let mut normalized = String::new();
    if ctrl {
        normalized.push_str("<Control>");
    }
    if shift {
        normalized.push_str("<Shift>");
    }
    if alt {
        normalized.push_str("<Alt>");
    }
    normalized.push_str(&key);
    Some((normalized, key))
}

/// 默认键位表(核心命令;与 macOS ShortcutSettings.swift 对齐)。
pub fn default_table() -> ShortcutTable {
    let defaults: &[(&str, &str)] = &[
        ("save", "<Control>s"),
        ("saveAll", "<Control><Shift>s"),
        ("saveAs", "<Control><Alt>s"),
        ("open", "<Control>o"),
        ("new", "<Control>n"),
        ("newWindow", "<Control><Shift>n"),
        ("newPlainText", "<Control><Alt>n"),
        ("print", "<Control>p"),
        ("find", "<Control>f"),
        ("sourceMode", "<Control>e"),
        ("undo", "<Control>z"),
        ("redo", "<Control><Shift>z"),
        ("toggleBold", "<Control>b"),
        ("toggleItalic", "<Control>i"),
        ("toggleCode", "<Control><Shift>c"),
        ("insertLink", "<Control>k"),
        ("toggleSidebar", "<Control>backslash"),
        ("outlineTab", "<Control><Shift>o"),
        ("workspaceTab", "<Control><Shift>e"),
        ("zoomIn", "<Control>plus"),
        ("zoomOut", "<Control>minus"),
        ("resetZoom", "<Control>0"),
        ("closeCurrentTab", "<Control>w"),
        ("showPreferences", "<Control>comma"),
    ];
    let mut table = ShortcutTable::default();
    for (command, accel) in defaults {
        table
            .bind(command, accel)
            .expect("默认表内部无冲突");
    }
    table
}

#[derive(Debug, Default, Clone)]
pub struct ShortcutTable {
    /// 命令 id → 规范化 accel(如 <Control><Shift>s)。
    bindings: Vec<(String, String)>,
    /// 自定义覆盖的命令集合(设置持久化来源)。
    custom: std::collections::HashMap<String, String>,
}

impl ShortcutTable {
    pub fn accel_for(&self, command: &str) -> Option<String> {
        self.bindings
            .iter()
            .find(|(id, _)| id == command)
            .map(|(_, accel)| accel.clone())
    }

    pub fn custom_bindings(&self) -> &std::collections::HashMap<String, String> {
        &self.custom
    }

    /// 应用设置里的自定义映射(在默认表之上)。
    pub fn apply_custom(&mut self, custom: &std::collections::HashMap<String, String>) {
        for (command, accel) in custom {
            if self.bind(command, accel).is_err() {
                eprintln!("[shortcuts] 跳过冲突的自定义键位 {command}={accel}");
            }
        }
    }

    /// 绑定命令;accel 已被其他命令占用时返回冲突。
    pub fn bind(&mut self, command: &str, accel: &str) -> Result<(), ShortcutConflict> {
        let Some((normalized, _key)) = resolve_accel(accel) else {
            return Err(ShortcutConflict {
                command: "invalid".to_string(),
                accel: accel.to_string(),
            });
        };
        let command = command.to_string();
        // 冲突检测:同一 accel 已被其他命令占用。
        if let Some((owner, _)) = self
            .bindings
            .iter()
            .find(|(id, existing)| *id != command && existing == &normalized)
        {
            return Err(ShortcutConflict {
                command: owner.clone(),
                accel: accel.to_string(),
            });
        }
        self.bindings.retain(|(id, _)| id != &command);
        self.bindings.push((command.clone(), normalized));
        self.custom.insert(command, accel.to_string());
        Ok(())
    }

    pub fn clear_accel(&mut self, command: &str) {
        self.bindings.retain(|(id, _)| id != command);
        self.custom.remove(command);
    }
}
