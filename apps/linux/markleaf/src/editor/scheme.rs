// 自定义 URI scheme 解析与 GTK 注册:
//   markleaf://editor/<rel>         → resources/editor 内的页面与静态资产(镜像 macOS EditorSchemeHandler)
//   markleaf-asset://local/image?…  → 文档图片(镜像 macOS markleaf-asset:,对齐内核 assetOrigin Linux 分支)
// 路径约束:页面路径必须在 editor_root 内(拒绝 `..` 穿越);图片路径在此层
// 仅做归一化,工作区越权防护由持有文档上下文的调用层完成(见 spec §4)。
use std::path::{Path, PathBuf};


#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SchemeRequest {
    /// editor 静态资源,相对于 resources/editor。
    Page(PathBuf),
    /// 文档图片路径(已百分号解码、POSIX 归一化)。
    AssetImage(PathBuf),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SchemeError {
    UnsupportedScheme(String),
    Traversal,
    MissingPath,
    UnexpectedEndpoint,
}

fn percent_decode(raw: &str) -> String {
    let bytes = raw.as_bytes();
    let mut out: Vec<u8> = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' {
            let hex = bytes.get(i + 1..i + 3);
            if let Some(hex) = hex {
                if let Ok(byte) = u8::from_str_radix(std::str::from_utf8(hex).unwrap_or(""), 16) {
                    out.push(byte);
                    i += 3;
                    continue;
                }
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

fn has_traversal(relative: &str) -> bool {
    relative.split('/').any(|segment| segment == "..")
}

/// 解析宿主 scheme URI(纯逻辑,GTK 注册见 [`register_schemes`])。
pub fn resolve_scheme_uri(uri: &str, editor_root: &Path) -> Result<SchemeRequest, SchemeError> {
    let (scheme, rest) = uri.split_once("://").ok_or_else(|| SchemeError::UnsupportedScheme(uri.to_string()))?;
    match scheme {
        "markleaf" => {
            // markleaf://editor/<path>:host 必须为 editor。
            let (host, path) = rest.split_once('/').ok_or(SchemeError::UnexpectedEndpoint)?;
            if host != "editor" {
                return Err(SchemeError::UnsupportedScheme(uri.to_string()));
            }
            let path = percent_decode(path);
            if path.is_empty() || has_traversal(&path) {
                return Err(SchemeError::Traversal);
            }
            Ok(SchemeRequest::Page(editor_root.join(path)))
        }
        "markleaf-asset" => {
            // markleaf-asset://local/image?path=<encoded>
            let (host, rest) = rest.split_once('/').ok_or(SchemeError::UnexpectedEndpoint)?;
            if host != "local" {
                return Err(SchemeError::UnsupportedScheme(uri.to_string()));
            }
            let (endpoint, query) = rest.split_once('?').unwrap_or((rest, ""));
            if endpoint != "image" {
                return Err(SchemeError::UnexpectedEndpoint);
            }
            let raw_path = query
                .split('&')
                .find_map(|pair| pair.strip_prefix("path="))
                .ok_or(SchemeError::MissingPath)?;
            let decoded = percent_decode(raw_path);
            if decoded.is_empty() {
                return Err(SchemeError::MissingPath);
            }
            // 归一化:消除 `./` 与重复分隔符;保留 `..` 段交给上层按工作区裁决。
            let mut normalized = PathBuf::new();
            for segment in Path::new(&decoded).components() {
                match segment {
                    std::path::Component::CurDir => {}
                    other => normalized.push(other),
                }
            }
            Ok(SchemeRequest::AssetImage(normalized))
        }
        other => Err(SchemeError::UnsupportedScheme(other.to_string())),
    }
}

/// 静态资源 MIME 推断(编辑器 dist 内出现的类型全覆盖)。
pub fn guess_mime(path: &Path) -> &'static str {
    match path
        .extension()
        .and_then(|ext| ext.to_str())
        .map(|ext| ext.to_ascii_lowercase())
        .as_deref()
    {
        Some("html") => "text/html",
        Some("js") | Some("mjs") => "text/javascript",
        Some("css") => "text/css",
        Some("json") | Some("map") => "application/json",
        Some("png") => "image/png",
        Some("jpg") | Some("jpeg") => "image/jpeg",
        Some("webp") => "image/webp",
        Some("gif") => "image/gif",
        Some("svg") => "image/svg+xml",
        Some("ico") => "image/x-icon",
        Some("woff") => "font/woff",
        Some("woff2") => "font/woff2",
        Some("ttf") => "font/ttf",
        Some("otf") => "font/otf",
        Some("wasm") => "application/wasm",
        Some("txt") => "text/plain",
        _ => "application/octet-stream",
    }
}


// ---- GTK 侧:向 WebContext 注册两个 scheme ----
// 注册在 webkitgtk-6.0 的 WebContext 上;页面请求由 resolve_scheme_uri 裁决,
// 文档图片在 M1 返回空响应(M2 由文档控制器持工作区上下文供图)。
use gtk::gio;
use gtk::glib;
use webkit::{URISchemeRequest, WebContext};

pub fn register_schemes(context: &WebContext, editor_root: &Path) {
    let root = editor_root.to_path_buf();
    context.register_uri_scheme("markleaf", move |request: &URISchemeRequest| {
        serve_page(request, &root);
    });
    context.register_uri_scheme("markleaf-asset", |request: &URISchemeRequest| {
        // M1:文档图片尚未接线,返回空响应;M2 由文档层解析工作区路径。
        finish_empty(request);
    });
}

fn serve_page(request: &URISchemeRequest, editor_root: &Path) {
    let uri = request.uri().unwrap_or_default();
    match resolve_scheme_uri(&uri, editor_root) {
        // 内核懒加载按文档根请求 katex.min.css,而装配产物在 kernel/ 下;
        // macOS 端同样缺省(样式内嵌,404 无害),此处回退映射消掉错误日志。
        Ok(SchemeRequest::Page(ref path))
            if path.file_name().map(|n| n == "katex.min.css").unwrap_or(false)
                && !path.exists() =>
        {
            serve_page_fallback_kernel(request, editor_root, path)
        }
        Ok(SchemeRequest::Page(path)) => match std::fs::read(&path) {
            Ok(bytes) => {
                let mime = guess_mime(&path);
                let length = bytes.len() as i64;
                let stream = gio::MemoryInputStream::from_bytes(&glib::Bytes::from_owned(bytes));
                request.finish(&stream, length, Some(mime));
            }
            Err(err) => {
                eprintln!("[scheme] 读取失败 {:?}: {err}", path);
                finish_empty(request);
            }
        },
        Ok(SchemeRequest::AssetImage(_)) => finish_empty(request),
        Err(err) => {
            eprintln!("[scheme] 拒绝 {}: {err:?}", uri);
            finish_empty(request);
        }
    }
}

fn serve_page_fallback_kernel(
    request: &URISchemeRequest,
    editor_root: &Path,
    original: &Path,
) {
    let fallback = editor_root.join("kernel").join("katex.min.css");
    match std::fs::read(&fallback) {
        Ok(bytes) => {
            let length = bytes.len() as i64;
            let stream = gio::MemoryInputStream::from_bytes(&glib::Bytes::from_owned(bytes));
            request.finish(&stream, length, Some("text/css"));
        }
        Err(_) => {
            eprintln!("[scheme] 读取失败 {:?} 与回退均失败", original);
            finish_empty(request);
        }
    }
}

fn finish_empty(request: &URISchemeRequest) {
    let stream = gio::MemoryInputStream::new();
    request.finish(&stream, 0, None);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn root() -> PathBuf {
        PathBuf::from("/tmp/markleaf-editor")
    }

    #[test]
    fn resolves_editor_pages_within_root() {
        let request = resolve_scheme_uri("markleaf://editor/index.html", &root()).unwrap();
        assert_eq!(request, SchemeRequest::Page(root().join("index.html")));

        let request = resolve_scheme_uri("markleaf://editor/kernel/editor-core.js", &root()).unwrap();
        assert_eq!(request, SchemeRequest::Page(root().join("kernel/editor-core.js")));
    }

    #[test]
    fn rejects_editor_traversal_and_unknown_hosts() {
        assert_eq!(
            resolve_scheme_uri("markleaf://editor/../../etc/passwd", &root()),
            Err(SchemeError::Traversal)
        );
        assert!(resolve_scheme_uri("markleaf://other/index.html", &root()).is_err());
        assert!(resolve_scheme_uri("https://example.com/index.html", &root()).is_err());
    }

    #[test]
    fn resolves_asset_image_paths() {
        let request =
            resolve_scheme_uri("markleaf-asset://local/image?path=%2Fhome%2Fu%2Fpic.png", &root()).unwrap();
        assert_eq!(request, SchemeRequest::AssetImage(PathBuf::from("/home/u/pic.png")));

        // 与内核 resolve() 输出一致的相对路径(含百分号编码的 ./)。
        let request =
            resolve_scheme_uri("markleaf-asset://local/image?path=.%2Fimages%2Fpic.png", &root()).unwrap();
        assert_eq!(request, SchemeRequest::AssetImage(PathBuf::from("images/pic.png")));

        // 中文文件名往返。
        let request =
            resolve_scheme_uri("markleaf-asset://local/image?path=%E4%B8%AD%E6%96%87.png", &root()).unwrap();
        assert_eq!(request, SchemeRequest::AssetImage(PathBuf::from("中文.png")));
    }

    #[test]
    fn rejects_invalid_asset_uris() {
        assert_eq!(
            resolve_scheme_uri("markleaf-asset://local/image?path=", &root()),
            Err(SchemeError::MissingPath)
        );
        assert_eq!(
            resolve_scheme_uri("markleaf-asset://local/image", &root()),
            Err(SchemeError::MissingPath)
        );
        assert_eq!(
            resolve_scheme_uri("markleaf-asset://local/other?path=x.png", &root()),
            Err(SchemeError::UnexpectedEndpoint)
        );
    }

    #[test]
    fn guesses_mime_types() {
        assert_eq!(guess_mime(Path::new("index.html")), "text/html");
        assert_eq!(guess_mime(Path::new("a.js")), "text/javascript");
        assert_eq!(guess_mime(Path::new("a.css")), "text/css");
        assert_eq!(guess_mime(Path::new("a.png")), "image/png");
        assert_eq!(guess_mime(Path::new("a.svg")), "image/svg+xml");
        assert_eq!(guess_mime(Path::new("a.woff2")), "font/woff2");
        assert_eq!(guess_mime(Path::new("a.unknown")), "application/octet-stream");
    }
}
