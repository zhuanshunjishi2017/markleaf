// 工作区文件系统变更:新建/重命名/删除(对齐桌面 WorkspaceMutationPolicy)。
// 所有路径参数为"工作区根内的相对路径",函数内强制拼回根下,杜绝逃逸。
use std::fs;
use std::path::{Path, PathBuf};

fn resolve(root: &Path, relative: &str) -> PathBuf {
    // 防御性过滤分隔符逃逸(".."、绝对路径),相对路径分段拼接。
    let safe: String = relative
        .replace('\\', "/")
        .split('/')
        .filter(|seg| !seg.is_empty() && *seg != ".." && *seg != ".")
        .collect::<Vec<_>>()
        .join("/");
    root.join(safe)
}

fn unique_path(target: &Path) -> PathBuf {
    // 同名去重:未命名.md → 未命名-2.md → 未命名-3.md(保留扩展名)。
    if !target.exists() {
        return target.to_path_buf();
    }
    let stem = target
        .file_stem()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default();
    let ext = target
        .extension()
        .map(|s| format!(".{}", s.to_string_lossy()))
        .unwrap_or_default();
    for index in 2..1000 {
        let candidate = target.with_file_name(format!("{stem}-{index}{ext}"));
        if !candidate.exists() {
            return candidate;
        }
    }
    target.with_file_name(format!("{stem}-new{ext}"))
}

fn ensure_parent(target: &Path) -> std::io::Result<()> {
    if let Some(parent) = target.parent() {
        fs::create_dir_all(parent)?;
    }
    Ok(())
}

/// 在相对目录(空 = 根)下新建 Markdown 文件;同名自动追加序号。
pub fn create_markdown(root: &Path, dir_relative: &str) -> std::io::Result<PathBuf> {
    let dir = resolve(root, dir_relative);
    let target = unique_path(&dir.join("未命名.md"));
    ensure_parent(&target)?;
    fs::write(&target, "# \n")?;
    Ok(target)
}

/// 在相对目录下新建纯文本文件。
pub fn create_plain_text(root: &Path, dir_relative: &str) -> std::io::Result<PathBuf> {
    let dir = resolve(root, dir_relative);
    let target = unique_path(&dir.join("未命名.txt"));
    ensure_parent(&target)?;
    fs::write(&target, "")?;
    Ok(target)
}

/// 在相对目录下新建文件夹。
pub fn create_folder(root: &Path, dir_relative: &str, name: &str) -> std::io::Result<PathBuf> {
    let dir = resolve(root, dir_relative);
    let target = unique_path(&dir.join(name));
    fs::create_dir_all(&target)?;
    Ok(target)
}

/// 重命名(同目录内);返回新相对路径。目标同名已存在时报错。
pub fn rename_entry(root: &Path, relative: &str, new_name: &str) -> std::io::Result<String> {
    let source = resolve(root, relative);
    let parent = source
        .parent()
        .map(|p| p.to_path_buf())
        .unwrap_or_else(|| root.to_path_buf());
    let target = unique_path(&parent.join(new_name));
    fs::rename(&source, &target)?;
    let root_str = root.to_string_lossy();
    let new_abs = target.to_string_lossy();
    Ok(new_abs
        .strip_prefix(root_str.as_ref())
        .map(|rest| rest.trim_start_matches('/').to_string())
        .unwrap_or(new_abs.into_owned()))
}

/// 删除 = 移入 XDG 用户回收站(~/.local/share/Trash),可从文件管理器还原。
/// 回收站不可用时回退为永久删除(行为退化但功能不阻断)。
pub fn delete_entry(root: &Path, relative: &str) -> std::io::Result<()> {
    let target = resolve(root, relative);
    if move_to_trash(&target).is_ok() {
        return Ok(());
    }
    // 回退:永久删除(回收站不可用:无 HOME/卷不可写等)。
    if target.is_dir() {
        fs::remove_dir_all(target)
    } else {
        fs::remove_file(target)
    }
}

fn deletion_date_text() -> String {
    // trashinfo 要求本地时间 ISO 格式;用 UTC 近似(还原语义不受影响)。
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    let days = (secs / 86_400) as i64;
    let rem = secs % 86_400;
    let (y, mo, d) = civil_from_days(days);
    format!("{y:04}-{mo:02}-{d:02}T{:02}:{:02}:{:02}", rem / 3600, (rem % 3600) / 60, rem % 60)
}

fn civil_from_days(z: i64) -> (i64, i64, i64) {
    let z = z + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as i64;
    let m: i64 = if mp < 10 { (mp + 3) as i64 } else { (mp - 9) as i64 };
    (if m <= 2 { y + 1 } else { y }, m, d)
}

fn move_to_trash(target: &Path) -> std::io::Result<()> {
    let home = std::env::var("HOME")
        .map_err(|_| std::io::Error::other("无 HOME 目录,无法定位回收站"))?;
    let trash_root = PathBuf::from(&home).join(".local/share/Trash");
    fs::create_dir_all(trash_root.join("files"))?;
    fs::create_dir_all(trash_root.join("info"))?;

    let name = target
        .file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_else(|| "entry".into());
    let files_target = unique_path(&trash_root.join("files").join(&name));
    let info_name = files_target
        .file_name()
        .map(|n| format!("{}.trashinfo", n.to_string_lossy()))
        .unwrap_or_else(|| format!("{name}.trashinfo"));
    let info_target = trash_root.join("info").join(info_name);

    fs::rename(target, &files_target)?;
    let abs_path = percent_encode(&target.to_string_lossy());
    let info = format!(
        "[Trash Info]\nPath={}\nDeletionDate={}\n",
        abs_path,
        deletion_date_text()
    );
    fs::write(info_target, info)?;
    Ok(())
}

fn percent_encode(path: &str) -> String {
    let mut out = String::new();
    for byte in path.bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' | b'/' => {
                out.push(byte as char)
            }
            _ => out.push_str(&format!("%{byte:02X}")),
        }
    }
    out
}

/// 读取文本文件内容(复制文件内容用)。
pub fn read_text(root: &Path, relative: &str) -> std::io::Result<String> {
    fs::read_to_string(resolve(root, relative))
}
