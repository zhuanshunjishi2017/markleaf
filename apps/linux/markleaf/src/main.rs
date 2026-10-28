// MarkLeaf Linux 宿主入口。M1 骨架:最小 GtkApplication 空窗。
// 应用 id 与 Android 端 com.markleaf.app 同域(见 spec §11)。
use gtk::glib;
use gtk::prelude::*;

const APP_ID: &str = "com.markleaf.MarkLeaf";
const APP_VERSION: &str = env!("CARGO_PKG_VERSION");

fn main() -> glib::ExitCode {
    // 无显示环境下的 --version 探测不需要初始化 GTK。
    if std::env::args().any(|arg| arg == "--version") {
        println!("markleaf {APP_VERSION}");
        return glib::ExitCode::SUCCESS;
    }

    let app = adw::Application::builder().application_id(APP_ID).build();
    app.connect_activate(|app| {
        let window = adw::ApplicationWindow::builder()
            .application(app)
            .title("MarkLeaf")
            .default_width(200)
            .default_height(200)
            .build();
        window.present();
    });
    app.run()
}
