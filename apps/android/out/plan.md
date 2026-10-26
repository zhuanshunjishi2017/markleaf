# MarkLeaf Android 宿主 — 可观察项契约

> 本文件满足 video2code 插件 plan-before-code 钩子的前置要求（它拦截所有
> `app/src/**` 写入）。Android 宿主不是视频复刻任务，真正的设计文档与实现
> 计划在 docs/superpowers/specs 与 docs/superpowers/plans（已提交）。此处以
> 同样的 [S]/[D] 可观察项格式摘录 Android 宿主的可验证行为，供逐任务审计。

## M1 骨架（Task 1-7）

[S1] `./gradlew assembleDebug` 退出码 0，产出 app-debug.apk（实测 9.6MB→含资产后约 16MB）
[S2] APK 内含 assets/editor/index.html + kernel 全部分包 + native-shim.js（hostPlatform='Android'）
[S3] 应用启动后 WebView 加载 file:///android_asset/editor/index.html，前端发送 ready
[S4] 宿主在 ready 后依序发送 applyStyles → stylesApplied → loadDocument（内置欢迎文档）
[S5] 欢迎文档在 WebView 内渲染出标题/表格/代码块（模拟器人工验收）

## M2 编辑闭环（Task 8-9）

[S6] SAF 打开 .md → loadDocument 载入，dirtyChanged 驱动标签脏标记
[S7] 保存：requestSnapshot → snapshot → 原编码写回 → markSaved
[S8] 外部修改冲突 → SaveConflict 分支（覆盖/另存）
[S9] onPause 写恢复快照；启动时发现快照弹恢复对话框

## M3 完整壳（Task 10、12）

[S10] assets.local 图片经 WebViewClient 拦截返回文档目录图片字节
[S11] 侧栏树/列表/大纲可用；outlineChanged 驱动大纲跳转 scrollToPosition
[S12] 标签上限 6 个活跃 WebView，LRU 关闭最旧；onTrimMemory 只留当前
[S13] contextMenuRequested → DropdownMenu 于换算坐标弹出，命令执行经 command 消息
[S14] 深色模式切换触发 applyStyles 重发；四语言 L10n 加载

## M4 导出（Task 11）

[S15] HTML 导出：前端 exportDocument → 自包含 HTML → SAF CreateDocument 落盘
[S16] PDF 导出：PrintManager + createPrintDocumentAdapter → 选定 URI 写入
[S17] 导出对话框状态机（格式/文件名/纸张/边距/打印友好）

## M5 打磨（Task 12-13）

[S18] onRenderProcessGone → 销毁重建 + RecoveryStore 重载（不白屏）
[S19] Android 下 CodeMirror inputStyle='contenteditable'，中文 IME 合成不丢字（模拟器人工）
[S20] 2MB 文档打开：主线程占用 <500ms（StrictMode/日志计时验证）

## 审计记录

- 每任务完成后逐条核对上述 [S] 项，结论写入 commit message（Reviewed-by: self-audit 段）
- 环境实测：JDK21 + platform-36 + build-tools 36；AGP 8.7.3（对 compileSdk 36 有测试上限警告，非阻塞）
