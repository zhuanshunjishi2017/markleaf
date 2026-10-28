// GtkApplication 装配:单实例、文件打开、会话恢复/保存、设置持久化。
use std::cell::RefCell;
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
            *settings.borrow_mut() = load_settings(&settings_path());
            let resources = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources/editor");
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
                        for tab_path in &window_session.tabs {
                            let window_for_tab = window.clone();
                            window_for_tab.open_document_in_tab(&PathBuf::from(tab_path));
                        }
                        window.select_tab(window_session.active_tab);
                        window.present();
                        windows_activate.borrow_mut().push(window);
                    }
                }
                _ => {
                    let window = AppWindow::new(app, &context, settings.clone(), windows_activate.clone());
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
fn prompt_recovered_documents(window: &Rc<AppWindow>) {
    let entries = crate::data::recovery::list_recovery(&recovered_dir());
    for entry in entries {
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
            if let Some(path) = &path {
                window.open_document_recovered(path, markdown);
            }
            dialog.close();
        });
        dialog.connect_response(Some("discard"), move |dialog, _response| {
            crate::data::recovery::discard_recovery(&recovered_dir(), &entry_id_discard);
            dialog.close();
        });
        dialog.present();
    }
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

/// 默认快捷键表 + 设置中的自定义映射(供窗口注册加速键)。
pub fn shortcuts(settings: &AppSettings) -> crate::ui::shortcuts::ShortcutTable {
    let mut table = crate::ui::shortcuts::default_table();
    table.apply_custom(&settings.shortcuts);
    table
}
