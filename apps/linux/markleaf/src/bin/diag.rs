// 渲染诊断(实机):在真实显示会话上加载两个页面并快照分析像素。
// 页 1:纯红 data URL(load-changed Finished 触发)——判别 WebKit 基本绘制能力;
// 页 2:真实 markleaf://editor 页面 + loadDocument(DocumentLoaded 触发)——判别编辑器页。
// 输出每页的采样色数与主色,定位"整页画不出"还是"编辑器页特有"。
use std::cell::RefCell;
use std::path::PathBuf;
use std::rc::Rc;

use gdk::prelude::TextureExt;
use gdk::prelude::TextureExtManual;
use gtk::glib;
use gtk::prelude::*;
use markleaf::bridge::messages::{EditorMessage, EditorMessageType, HostMessage};
use markleaf::editor::webview::{build_context, EditorWebView};
use webkit::prelude::WebViewExt;
use webkit::{LoadEvent, SnapshotOptions, SnapshotRegion};

const DEADLINE_SECONDS: u32 = 60;

fn editor_root() -> PathBuf {
    if let Ok(root) = std::env::var("MARKLEAF_EDITOR_ROOT") {
        return PathBuf::from(root);
    }
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("resources/editor")
}

fn analyze(texture: &gdk::Texture, label: &str) {
    let (width, height) = (texture.width(), texture.height());
    let stride = (width as usize) * 4;
    let mut pixels = vec![0u8; stride * (height as usize)];
    texture.download(&mut pixels, stride);
    let mut distinct: std::collections::HashMap<[u8; 4], usize> = std::collections::HashMap::new();
    let samples = 12;
    for row in 0..samples {
        for col in 0..samples {
            let y = ((height as usize - 1) * row / (samples - 1)).min(height as usize - 1);
            let x = ((width as usize - 1) * col / (samples - 1)).min(width as usize - 1);
            let offset = y * stride + x * 4;
            let pixel: [u8; 4] = pixels[offset..offset + 4].try_into().expect("RGBA8");
            *distinct.entry(pixel).or_insert(0) += 1;
        }
    }
    let kinds = distinct.len();
    let mut top: Vec<_> = distinct.into_iter().collect();
    top.sort_by(|a, b| b.1.cmp(&a.1));
    let top_colors: Vec<String> = top
        .iter()
        .take(4)
        .map(|(p, n)| format!("rgba({},{},{},{})x{}", p[0], p[1], p[2], p[3], n))
        .collect();
    println!(
        "[diag] {label}: {width}x{height} 采样 {kinds} 种;TOP: {}",
        top_colors.join("  ")
    );
}

fn main() {
    let root = editor_root();
    gtk::init().expect("gtk init");
    let main_loop = glib::MainLoop::new(None, false);
    let context = build_context(&root);

    let window = gtk::Window::builder()
        .title("Render Diag")
        .default_width(800)
        .default_height(600)
        .build();

    let webview = EditorWebView::new(&context);
    window.set_child(Some(webview.widget()));
    window.present();

    let stage = Rc::new(RefCell::new(0u8));

    {
        let capture = webview.clone();
        let receiver = webview.clone();
        let main_loop = main_loop.clone();
        let stage = stage.clone();
        receiver
            .widget()
            .connect_load_changed(move |_view, event| {
                if event != LoadEvent::Finished || *stage.borrow() != 0 {
                    return;
                }
                *stage.borrow_mut() = 1;
                let page_webview = capture.clone();
                let main_loop = main_loop.clone();
                glib::timeout_add_local_once(std::time::Duration::from_millis(1000), move || {
                    let widget = page_webview.widget().clone();
                    widget.snapshot(
                        SnapshotRegion::Visible,
                        SnapshotOptions::empty(),
                        None::<&gtk::gio::Cancellable>,
                        move |outcome| match outcome {
                            Ok(texture) => {
                                analyze(&texture, "红页(data URL)");
                                page_webview.load_editor_page();
                            }
                            Err(err) => {
                                eprintln!("[diag] 红页快照失败:{err}");
                                main_loop.quit();
                            }
                        },
                    );
                });
            });
    }

    {
        let capture = webview.clone();
        let receiver = webview.clone();
        let stage = stage.clone();
        let main_loop = main_loop.clone();
        receiver.connect_message(move |message| {
            if message.message_type != EditorMessageType::DocumentLoaded
                || *stage.borrow() != 1
            {
                return;
            }
            *stage.borrow_mut() = 2;
            let webview = capture.clone();
            let main_loop = main_loop.clone();
            glib::timeout_add_local_once(std::time::Duration::from_millis(1200), move || {
                let webview = webview.clone();
                let main_loop = main_loop.clone();
                webview.widget().snapshot(
                    SnapshotRegion::Visible,
                    SnapshotOptions::empty(),
                    None::<&gtk::gio::Cancellable>,
                    move |outcome| match outcome {
                        Ok(texture) => {
                            analyze(&texture, "编辑器页");
                            main_loop.quit();
                        }
                        Err(err) => {
                            eprintln!("[diag] 编辑器页快照失败:{err}");
                            main_loop.quit();
                        }
                    },
                );
            });
        });
    }

    {
        let timeout_loop = main_loop.clone();
        glib::timeout_add_local(std::time::Duration::from_secs(DEADLINE_SECONDS as u64), move || {
            eprintln!("[diag] 死线到达");
            timeout_loop.quit();
            glib::ControlFlow::Break
        });
    }

    // 页 2 的 loadDocument 在 DocumentLoaded 之外还需要先 applyStyles;这里
    // 用固定延时替代握手:load_editor_page 后 2.5s 发 applyStyles+loadDocument。
    {
        let webview = webview.clone();
        glib::timeout_add_local_once(std::time::Duration::from_millis(2500), move || {
            let apply = HostMessage::apply_styles("diag-doc", serde_json::json!({}));
            webview.post_to_editor(&apply);
            let webview = webview.clone();
            glib::timeout_add_local_once(std::time::Duration::from_millis(300), move || {
                let load = HostMessage::load_document(
                    "diag-doc",
                    serde_json::json!({
                        "markdown": "# 诊断标题\n\n**加粗文本**用于像素检验\n\n| A | B |\n| - | - |\n| 1 | 2 |",
                        "documentType": "markdown",
                        "readOnly": true,
                        "initialDirty": false,
                        "scrollTop": 0,
                        "restoreViewState": false,
                    }),
                );
                webview.post_to_editor(&load);
            });
        });
    }

    webview.widget().load_html(
        "<body style=\"background:#ff0000\"><h1 style=\"color:white;font-size:48px\">RED TEST</h1></body>",
        Some("about:diag-red"),
    );
    main_loop.run();
    println!("[diag] DONE");
}
