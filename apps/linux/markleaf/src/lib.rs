// MarkLeaf Linux 宿主共享库:桥与编辑器 WebView 封装。
// bin(markleaf)与冒烟(smoke)共同引用;GTK 类型在此 crate 内使用,不对外承诺 ABI。
pub mod app;
pub mod bridge;
pub mod data;
pub mod documents;
pub mod editor;
pub mod l10n;
pub mod ui;
pub mod workspace;
