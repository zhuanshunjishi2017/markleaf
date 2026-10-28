// 文档读写:原子替换 + 外部修改冲突检测。
// 语义对齐桌面端:保存前 mtime+内容哈希比对,变化即 Conflict(不覆盖);
// 写入经 temp + rename,读侧永不看见半文件。
use std::fs;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

use sha2::{Digest, Sha256};

#[derive(Debug, Clone)]
pub struct DocumentSnapshot {
    pub path: PathBuf,
    pub bytes: Vec<u8>,
    pub mtime: SystemTime,
    pub content_hash: [u8; 32],
}

impl Default for DocumentSnapshot {
    fn default() -> Self {
        Self {
            path: PathBuf::new(),
            bytes: Vec::new(),
            mtime: SystemTime::UNIX_EPOCH,
            content_hash: [0; 32],
        }
    }
}

#[derive(Debug)]
pub enum WriteError {
    /// 外部修改:读入快照后文件已被他人改变,保存被拒绝。
    Conflict,
    Io(std::io::Error),
}

impl std::fmt::Display for WriteError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            WriteError::Conflict => write!(f, "文件已被外部修改,保存被拒绝"),
            WriteError::Io(err) => write!(f, "写入失败:{err}"),
        }
    }
}

fn hash(bytes: &[u8]) -> [u8; 32] {
    Sha256::digest(bytes).into()
}

pub fn read_document(path: &Path) -> std::io::Result<DocumentSnapshot> {
    let bytes = fs::read(path)?;
    let mtime = fs::metadata(path)?.modified()?;
    Ok(DocumentSnapshot {
        path: path.to_path_buf(),
        content_hash: hash(&bytes),
        mtime,
        bytes,
    })
}

/// 原子写回:写前重读比对 mtime/哈希,变化即 Conflict;temp + rename。
/// `expected` 为空(Default)时仅要求文件不存在或为空(新建文档路径)。
pub fn write_document(path: &Path, bytes: &[u8], expected: &DocumentSnapshot) -> Result<(), WriteError> {
    // 冲突检测:当前磁盘状态必须与读入快照一致。
    match read_document(path) {
        Ok(current) => {
            let unchanged = expected.path == current.path
                && expected.content_hash == current.content_hash;
            if !unchanged {
                return Err(WriteError::Conflict);
            }
        }
        Err(err) if err.kind() == std::io::ErrorKind::NotFound => {
            // 新建文档:expected 为 Default 快照时放行。
            if expected.path != PathBuf::new() {
                return Err(WriteError::Conflict);
            }
        }
        Err(err) => return Err(WriteError::Io(err)),
    }

    // temp + rename 原子替换(同目录保证同文件系统)。
    let temp_path = path.with_extension("markleaf-tmp");
    fs::write(&temp_path, bytes).map_err(WriteError::Io)?;
    fs::rename(&temp_path, path).map_err(|err| {
        let _ = fs::remove_file(&temp_path);
        WriteError::Io(err)
    })
}
