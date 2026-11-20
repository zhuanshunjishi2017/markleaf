// GtkApplication 装配:单实例、文件打开、会话恢复/保存、设置持久化。
use std::cell::RefCell;
use std::collections::VecDeque;
use std::fs;
use std::path::PathBuf;
use std::rc::Rc;

use adw::prelude::*;
use gtk::gio;
use gtk::glib;

use crate::data::session::{load as load_session, save as save_session, SessionManifest};
use crate::data::settings::{load_settings, save_settings, AppSettings};
use crate::editor::webview::build_context;
use crate::ui::window::AppWindow;

pub const APP_ID: &str = "com.markleaf.MarkLeaf";

/// XDG 目录:(配置目录, 状态目录);变量缺失回退 ~/.config 与 ~/.local/state。
pub fn storage_dirs() -> (PathBuf, PathBuf) {
    let config = std::env::var("XDG_CONFIG_HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            let home = std::env::var("HOME").unwrap_or_default();
            PathBuf::from(home).join(".config")
        })
        .join("markleaf");
    let state = std::env::var("XDG_STATE_HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            let home = std::env::var("HOME").unwrap_or_default();
            PathBuf::from(home).join(".local/state")
        })
        .join("markleaf");
    (config, state)
}

pub fn settings_path() -> PathBuf {
    storage_dirs().0.join("settings.json")
}

pub fn session_path() -> PathBuf {
    storage_dirs().1.join("session/manifest.json")
}

pub fn run() -> glib::ExitCode {
    if std::env::args().any(|arg| arg == "--version") {
        println!("markleaf {}", env!("CARGO_PKG_VERSION"));
        return glib::ExitCode::SUCCESS;
    }

    let _ = env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("warn")).try_init();

    ensure_desktop_integration();

    let app = adw::Application::builder()
        .application_id(APP_ID)
        .flags(gio::ApplicationFlags::HANDLES_OPEN)
        .build();

    // 共享状态:设置 + WebContext(全部窗口共用一个 WebKit 进程池)+ 窗口表。
    let settings = Rc::new(RefCell::new(AppSettings::default()));
    let context: Rc<RefCell<Option<webkit::WebContext>>> = Rc::new(RefCell::new(None));
    let windows: Rc<RefCell<Vec<Rc<AppWindow>>>> = Rc::new(RefCell::new(Vec::new()));

    {
        let settings = settings.clone();
        let context = context.clone();
        app.connect_startup(move |_app| {
            gtk::Window::set_default_icon_name("markleaf");
            *settings.borrow_mut() = load_settings(&settings_path());
            let resources = editor_resources_root();
            *context.borrow_mut() = Some(build_context(&resources));
        });
    }

    {
        let settings = settings.clone();
        let context = context.clone();
        let windows_activate = windows.clone();
        app.connect_activate(move |app| {
            let Some(context) = context.borrow().clone() else {
                return;
            };
            // 会话恢复:清单里的每个窗口一组标签;无清单则开空窗口。
            let restored = restored_session();
            match restored {
                Some(manifest) if !manifest.windows.is_empty() => {
                    for window_session in &manifest.windows {
                        let window = AppWindow::new(
                            app,
                            &context.clone(),
                            settings.clone(),
                            windows_activate.clone(),
                        );
                        // 异步建标签,全部完成后选 active_tab(同步 select 在
                        // TabView 空时触发 libadwaita 断言崩溃,见 restore_tabs)。
                        window.restore_tabs(&window_session.tabs, window_session.active_tab);
                        window.present();
                        windows_activate.borrow_mut().push(window);
                    }
                }
                _ => {
                    let window = AppWindow::new(app, &context, settings.clone(), windows_activate.clone());
                    // 对齐桌面 restoreSession 兜底:无任何会话/历史时新建空白文档,
                    // 而不是开一个右侧全空的窗口。
                    window.create_tab_blank();
                    window.present();
                    windows_activate.borrow_mut().push(window.clone());
                    prompt_recovered_documents(&window);
                }
            }
        });
    }

    {
        let settings = settings.clone();
        let context = context.clone();
        let windows_open = windows.clone();
        app.connect_open(move |app, files, _hint| {
            let Some(context) = context.borrow().clone() else {
                return;
            };
            let paths: Vec<PathBuf> = files.iter().filter_map(|file| file.path()).collect();
            let existing = windows_open.borrow().last().cloned();
            match existing {
                Some(window) => {
                    for path in paths {
                        window.open_document_in_tab(&path);
                    }
                    window.present();
                }
                None => {
                    let window = AppWindow::new(app, &context, settings.clone(), windows_open.clone());
                    for path in paths {
                        window.open_document_in_tab(&path);
                    }
                    window.present();
                    windows_open.borrow_mut().push(window);
                }
            }
        });
    }

    // 正常退出:保存设置与会话清单(shutdown 在窗口销毁前触发)。
    {
        let settings = settings.clone();
        let windows_shutdown = windows.clone();
        app.connect_shutdown(move |_app| {
            let manifest = SessionManifest {
                windows: windows_shutdown
                    .borrow()
                    .iter()
                    .map(|window| crate::data::session::WindowSession {
                        tabs: window.tab_paths(),
                        active_tab: window.active_tab_index(),
                        width: 0,
                        height: 0,
                    })
                    .collect(),
            };
            let _ = save_session(&session_path(), &manifest);
            persist_settings(&settings.borrow());
        });
    }

    app.run()
}

fn recovered_dir() -> PathBuf {
    storage_dirs().1.join("recovery")
}

fn restored_session() -> Option<SessionManifest> {
    load_session(&session_path())
}

/// 启动恢复:发现 .recovery.json → 每条弹恢复/丢弃对话框。
/// 恢复 = 打开原文件并以恢复内容载入(脏);丢弃 = 删除快照。
pub fn prompt_recovered_documents(window: &Rc<AppWindow>) {
    let entries = crate::data::recovery::list_recovery(&recovered_dir());
    // 串行队列:一个弹窗关闭后再 present 下一个。并行弹窗全部模态叠在
    // 同一坐标,点掉顶层立刻露出下一个一模一样的,看起来像按钮无反应。
    let mut queue: VecDeque<crate::data::recovery::RecoveryEntry> = entries.into();
    let window = window.clone();
    glib::idle_add_local(move || {
        let Some(entry) = queue.pop_front() else {
            return glib::ControlFlow::Break;
        };
        let path = entry.path.clone().map(PathBuf::from);
        let window = window.clone();
        let entry_id = entry.document_id.clone();
        let entry_id_discard = entry_id.clone();
        let dialog = adw::MessageDialog::builder()
            .heading("发现未保存的恢复内容")
            .body(format!(
                "{}\n修订 {} · {}",
                entry.path.as_deref().unwrap_or("(未命名)"),
                entry.revision,
                glib::DateTime::from_unix_local(entry.saved_at)
                    .map(|d| d.format("%F %T").map(|s| s.to_string()).unwrap_or_default())
                    .unwrap_or_default()
            ))
            .transient_for(window.gtk_window())
            .modal(true)
            .build();
        dialog.add_response("discard", "丢弃");
        dialog.add_response("restore", "恢复");
        dialog.set_response_appearance("restore", adw::ResponseAppearance::Suggested);
        dialog.connect_response(Some("restore"), move |dialog, _response| {
            let markdown = entry_markdown_of(&entry_id, &recovered_dir());
            window.open_document_recovered(path.as_deref(), markdown);
            dialog.close();
        });
        dialog.connect_response(Some("discard"), move |dialog, _response| {
            crate::data::recovery::discard_recovery(&recovered_dir(), &entry_id_discard);
            dialog.close();
        });
        dialog.present();
        glib::ControlFlow::Break
    });
}

fn entry_markdown_of(document_id: &str, dir: &std::path::Path) -> String {
    crate::data::recovery::list_recovery(dir)
        .into_iter()
        .find(|candidate| candidate.document_id == document_id)
        .map(|found| found.markdown)
        .unwrap_or_default()
}

pub fn persist_settings(settings: &AppSettings) {
    let _ = save_settings(&settings_path(), settings);
}

/// 编辑器静态资源根查找链(发布链路核心):
/// 1. MARKLEAF_EDITOR_ROOT 环境变量(诊断/CI 显式指定)
/// 2. 可执行文件相对安装布局:AppImage(exe 在 usr/bin,资源在 share/markleaf/
///    editor)与 Flatpak(exe 在 /app/bin,资源在 /app/share/markleaf/editor)
///    共用 ../../share/markleaf/editor;另探 ../share/markleaf/editor 兼容
///    exe 与 share 同级的布局
/// 3. 开发目录(CARGO_MANIFEST_DIR/resources/editor,编译期路径)
/// 判定依据:index.html 存在。
pub fn editor_resources_root() -> PathBuf {
    if let Ok(root) = std::env::var("MARKLEAF_EDITOR_ROOT") {
        let root = PathBuf::from(root);
        if root.join("index.html").exists() {
            return root;
        }
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(bin_dir) = exe.parent() {
            for relative in ["../../share/markleaf/editor", "../share/markleaf/editor"] {
                let candidate = bin_dir.join(relative);
                if candidate.join("index.html").exists() {
                    return candidate;
                }
            }
        }
    }
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources/editor")
}

/// 桌面集成(开发跑法免安装):把 resources/icon 的多尺寸图标装进用户
/// hicolor theme,并把 .desktop 写入用户 applications 目录(Exec 指向当前
/// 二进制,任务栏/启动器即可显示图标并可固定启动)。幂等:已存在则跳过。
fn ensure_desktop_integration() {
    let Ok(exe) = std::env::current_exe() else {
        return;
    };
    let Ok(home) = std::env::var("HOME") else {
        return;
    };
    let icon_root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources/icon");
    let hicolor = PathBuf::from(home.clone()).join(".local/share/icons/hicolor");

    let sizes = [
        ("16x16", "16"),
        ("24x24", "24"),
        ("32x32", "32"),
        ("48x48", "48"),
        ("64x64", "64"),
        ("128x128", "128"),
        ("256x256", "256"),
        ("512x512", "512"),
        ("scalable", "1024"),
    ];
    let mut installed = false;
    for (dir_size, file_size) in sizes {
        let source = icon_root.join(format!("markleaf-{file_size}.png"));
        let Ok(bytes) = fs::read(&source) else {
            continue;
        };
        let target_dir = hicolor.join(format!("{dir_size}/apps"));
        if fs::create_dir_all(&target_dir).is_err() {
            continue;
        }
        let target = target_dir.join("markleaf.png");
        match fs::read(&target) {
            Ok(existing) if existing == bytes => {}
            _ => {
                if fs::write(&target, bytes).is_ok() {
                    installed = true;
                }
            }
        }
    }

    let desktop_target = PathBuf::from(home.clone())
        .join(".local/share/applications/com.markleaf.MarkLeaf.desktop");
    let desktop_source =
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources/icon/com.markleaf.MarkLeaf.desktop");
    if let Ok(template) = fs::read_to_string(&desktop_source) {
        let exe_line = format!("Exec={}", exe.display());
        let rendered = template
            .lines()
            .map(|line| if line.starts_with("Exec=") { exe_line.as_str() } else { line })
            .collect::<Vec<_>>()
            .join("\n");
        let needs_write = fs::read_to_string(&desktop_target)
            .map(|existing| existing != rendered)
            .unwrap_or(true);
        if needs_write && fs::write(&desktop_target, rendered).is_ok() {
            installed = true;
        }
    }

    if installed {
        // 刷新 icon theme 缓存(失败不致命;GTK 会在 theme 目录变化后自行重扫)。
        let _ = fs::create_dir_all(PathBuf::from(&home).join(".local/share/applications"));
        eprintln!("[desktop] 已安装 MarkLeaf 图标与 .desktop 到用户目录");
    }
}

/// 默认快捷键表 + 设置中的自定义映射(供窗口注册加速键)。
pub fn shortcuts(settings: &AppSettings) -> crate::ui::shortcuts::ShortcutTable {
    let mut table = crate::ui::shortcuts::default_table();
    table.apply_custom(&settings.shortcuts);
    table
}
