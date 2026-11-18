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

/// 删除文件(remove)或文件夹(递归)。
pub fn delete_entry(root: &Path, relative: &str) -> std::io::Result<()> {
    let target = resolve(root, relative);
    if target.is_dir() {
        fs::remove_dir_all(target)
    } else {
        fs::remove_file(target)
    }
}

/// 读取文本文件内容(复制文件内容用)。
pub fn read_text(root: &Path, relative: &str) -> std::io::Result<String> {
    fs::read_to_string(resolve(root, relative))
}
