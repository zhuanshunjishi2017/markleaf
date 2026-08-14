import AppKit

// MarkLeaf for macOS — 纯 AppKit 入口（CommandLineTools 环境无 SwiftUIMacros 插件，SwiftUI 宏不可用；
// AppKit 亦是与 Windows WinForms 外壳最直接的对应实现）。
let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
