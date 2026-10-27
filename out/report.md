# MarkLeaf Android 宿主 — 交付报告

**日期**: 2026-10-27
**分支**: master（bc1a22f6..bc1a22f6+，全部已推送）
**设计/计划**: docs/superpowers/specs/2026-10-27-android-host-design.md · docs/superpowers/plans/2026-10-27-android-host.md

## Shipped（已完成）

### 构建与基础设施
- Gradle 工程（AGP 8.7.3 / Kotlin 2.0.21 / minSdk 26 / targetSdk 36）+ wrapper + CI（android.yml：push 构建+单测）
- editor-web dist 同步任务（syncEditorAssets：拷贝 + SHA-256 manifest + Android native-shim 注入 + " N" 副本排除）

### 编辑器宿主核心
- EditorBridge：protocol.ts JSON 协议的 Kotlin 实现（window.postMessage 注入 + markleafHost JS 接口回传 + ready 握手缓存 + stale 过滤含 Windows ConfirmedRevision bug 修正）
- 协议消息模型：HostMessage 12 类 / EditorMessage 28 类全部具名（14/14 契约测试）
- 主题样式链路：ready → applyStyles（base.css + colors-default-light + serif 按序拼接，变量与排版规则完整）——修复表格无边框/代码块无背景/公式框缺失
- 拼写检查：Android 可视化模式关闭（EditorCreationOptions.spellCheck，红线干扰中文混排）
- 代码块：语言标签锚定（:has(.markleaf-code-block-controls) position:relative）、复制按钮 SVG 图标（跨平台无字体依赖）、触屏 32px 点击面积
- 渲染进程崩溃恢复（onRenderProcessGone → 销毁重建 + 会话重置）

### 文档与导出
- DocumentSource 抽象 + SAF 实现（列表/读/写/改名/删除/创建；编码探测 UTF-8/BOM/UTF-16/GBK 对齐桌面）
- DocumentController（打开/保存/外部修改冲突检测/恢复快照，DocumentControllerTest 7/7）
- 导出：HTML（CreateDocument 落盘）+ PDF（PrintManager + createPrintDocumentAdapter，A4 横竖）；文件名清洗对齐 Windows 规则

### UI
- Compose 壳：标签栏（打开/保存/导出/文件夹）、抽屉侧栏（工作区树/大纲）、状态栏（格式化字数/行列）
- 设置 DataStore（主题/编码默认值/最近文件上限10去重）
- 公式浮层触屏适配（源码框 pre-wrap 不折叠、工具条换行不溢出）

## 验证状态

- JVM 测试 75/75 绿（Robolectric：协议 14、会话 9、桥 6、TabState 5、Settings 3、编码 8、Ref 2、Recovery 3、Factory 3、Controller 7、Resolver 7、Export 7）
- 前端套件 109/109 绿（三桌面端无回归）
- 模拟器实测（Medium Phone API 37 arm64）：M1 握手与欢迎文档渲染通过（截图）

## Deferred（待用户模拟器验收 / 后续版本）

| 项 | 状态 | 说明 |
|---|---|---|
| S6-S9 SAF 打开/保存/冲突/恢复端到端 | 代码完成 | 需用户在模拟器实操（授权文件夹→编辑→保存→杀进程恢复） |
| S10 图片实机加载 | 代码完成 | 需含图片的文档验证 |
| S11 侧栏实机交互 | 代码完成 | 需授权文件夹后有内容 |
| S13 右键菜单弹出接线 | 组件就绪，MainActivity 弹出待接 | 下一轮 |
| S14 深色模式 applyStyles 重发 | 待接线 | 下一轮 |
| S15/S16 导出端到端 | 代码完成 | 需用户实操 |
| S18 渲染进程崩溃实测 | 代码完成 | 需模拟器注入验证 |
| S19 IME 人工验收 | 代码完成 | 需用户中文输入实测 |
| S20 2MB 性能计时 | 未测 | 需模拟器 |
| 公式重复渲染行 / 语言角标窄屏 | 部分修复 | KaTeX MathML 副本在 Chromium 155+ 的渲染为上游兼容问题 |

## 已知根因记录（供后续参考）

1. file:// 页面 ES module 必被 CORS 拦 → 页面/资源统一走 https://assets.local 拦截
2. ready 消息携带前端自生成占位 id → stale 过滤需豁免 ready 并采纳前端 id
3. applyStyles 载荷缺失 → 全部 CSS 变量装饰失效（衬线字体的生效掩盖了 base.css 缺失）
4. Android WebView 画 contenteditable 焦点 outline / 拼写红线 → spellCheck=false + outline 抑制
5. macOS 重复 " N" 副本混入 dist → build:editor-web 前置 rm -rf + sync 排除 + doFirst 清空
