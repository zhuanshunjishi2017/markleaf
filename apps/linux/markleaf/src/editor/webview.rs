// EditorWebView:WebKitWebView 封装——桥注册、页面加载、缩放。
// glib 对象可克隆(Clone 语义 = 引用共享);消息回调经 connect_message
// 在构造后注册,便于桥/宿主以共享状态模式接线(规避构造期循环引用)。
use std::path::Path;

use webkit::prelude::WebViewExt;
use webkit::{UserContentManager, WebView};

pub use webkit::WebContext;

use crate::bridge::messages::{EditorMessage, HostMessage};

/// script message handler 名(与 native-shim.js 的 markleafHost 对齐)。
const HANDLER_NAME: &str = "markleafHost";

/// 创建并配置共享 WebContext:自定义 scheme 注册 + 拼写检查
/// (webkitgtk-6.0 将拼写检查从 Settings 移到 WebContext,由系统 Enchant 词典支撑;
/// 内核 main.ts 对非 Android 宿主启用 spellCheck)。
pub fn build_context(editor_root: &Path) -> WebContext {
    // glib 对象的 Default 约定返回 Option;WebKitGTK 的 WebContext 构建不会失败。
    let context = WebContext::default().expect("WebContext 构建失败");
    context.set_spell_checking_enabled(true);
    super::scheme::register_schemes(&context, editor_root);
    context
}

/// 每标签一个常驻 WebView(spec 决策 D3);页面 URI 与 macOS markleaf://editor 同构。
#[derive(Clone)]
pub struct EditorWebView {
    webview: WebView,
    // UserContentManager 由 WebView 引用但不拥有;持有以保证信号处理器存活。
    _content_manager: UserContentManager,
}

impl EditorWebView {
    /// 页面 URI 与 macOS markleaf://editor 同构。
    pub const PAGE_URI: &'static str = "markleaf://editor/index.html";

    pub fn new(context: &WebContext) -> Self {
        let content_manager = UserContentManager::new();
        content_manager.register_script_message_handler(HANDLER_NAME, None::<&str>);

        let webview = WebView::builder()
            .web_context(context)
            .user_content_manager(&content_manager)
            .build();

        Self {
            webview,
            _content_manager: content_manager,
        }
    }

    /// 注册编辑器→宿主消息回调(shim 走字符串传输,这里解出 JSON 文本再解析)。
    /// 同一 WebView 可注册多个处理器(逐个触发);按消息类型过滤的便捷封装
    /// 见 [`connect_on_message_type`]。
    pub fn connect_message(&self, handler: impl Fn(EditorMessage) + 'static) {
        self._content_manager
            .connect_script_message_received(Some(HANDLER_NAME), move |_manager, value| {
                // shim 传 JS 字符串;to_json(0) 得到 JSON 字符串字面量,再解出原文。
                let text = value
                    .to_json(0)
                    .and_then(|quoted| serde_json::from_str::<String>(&quoted).ok())
                    .unwrap_or_default();
                match crate::bridge::messages::parse_editor_message(&text) {
                    Ok(message) => handler(message),
                    Err(err) => eprintln!("[bridge] 消息解析失败:{err}"),
                }
            });
    }

    /// 按消息类型注册处理器(如 ready 握手)。
    pub fn connect_on_message_type(&self, message_type: crate::bridge::messages::EditorMessageType, handler: impl Fn() + 'static) {
        self.connect_message(move |message| {
            if message.message_type == message_type {
                handler();
            }
        });
    }

    pub fn widget(&self) -> &WebView {
        &self.webview
    }

    pub fn load_editor_page(&self) {
        self.webview.load_uri(Self::PAGE_URI);
    }

    pub fn set_zoom_level(&self, level: f64) {
        self.webview.set_zoom_level(level);
    }

    /// 宿主→前端:与 macOS/Android 同构的 window.postMessage(JSON.parse('…')) 注入。
    pub fn post_to_editor(&self, message: &HostMessage) {
        let json = serde_json::to_string(message).expect("HostMessage 序列化");
        let script = format!(
            "window.postMessage(JSON.parse('{}'), '*')",
            crate::bridge::escape_for_js_literal(&json)
        );
        self.webview.evaluate_javascript(
            &script,
            None::<&str>,
            None::<&str>,
            None::<&gtk::gio::Cancellable>,
            |_| {},
        );
    }
}
