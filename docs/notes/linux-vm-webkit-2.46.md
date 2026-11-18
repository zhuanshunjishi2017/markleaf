# Linux VM 白屏排查记录（结论：应用布局 bug，非 WebKit 缺陷）

## 根因（2026-11-18 确认）
AdwTabView 在 GtkBox 中默认不纵向扩展，WebView 报告的最小高度为 0，
编辑区被分配 0 高度——文档已载入内核但无空间可画，表现为白屏。
佐证：WebProcess 日志 `Failed to create GBM buffer of size 688x0`（高度 0）。
修复：`tab_view.set_vexpand(true)`（910566b6）。

## 排查弯路（教训）
- 排查初期把 xwd 截图当作用户屏幕真相，多次把陈帧/局部帧误读为渲染结果；
- "WebKitGTK 2.52 Skia 环境缺陷"假设由 Xvfb 裁决实验强化（Xvfb 下同样白，
  但当时未意识到白因同样是 0 高度布局，与合成器无关）；
- 期间执行的 WebKitGTK 2.52→2.46.6 降级并非白屏的修复必需项，但 2.46.6
  在该 VM 上首帧渲染更快，暂保留（hold 锁定）。

## 降级操作记录（保留备查）
VM 上脚本与 deb：/tmp/webkit-downgrade/（重启即失，可按本文档重建）。
- 主包：libwebkitgtk-6.0-4 / libjavascriptcoregtk-6.0-1 = 2.46.6-1~deb12u1
  （Debian snapshot: bookworm-security 20250212T143810Z）
- soname 并存依赖：libicu72 libjpeg62-turbo libxml2(2.9, .so.2) libwoff1
  libavif15 libmanette-0.2-0 libdav1d6 libsvtav1enc1 libgav1-1 librav1e0
  libyuv0 libabsl20220623t64（noble pool + Debian snapshot 混源）
- 版本锁定：两包 hold；升回 2.52.6：解除 hold 后 apt install 即可
