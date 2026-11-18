# Linux 开发环境：WebKitGTK 降级 2.46.6（VM 白屏规避）

## 问题
WebKitGTK 2.52（Skia 渲染架构）在 VMware SVGA 等无硬件加速环境输出全白屏：
协议消息链正常（ready/documentLoaded/字数统计全部到达），仅像素不上屏。
Xvfb+llvmpipe 独立裁决实验复现，排除 mutter/VMware 合成因素。
上游报告：docs/upstream/2026-11-17-webkitgtk-2.52-skia-blank-vm.md

## 方案
降级 libwebkitgtk-6.0-4 / libjavascriptcoregtk-6.0-1 到 2.46.6-1~deb12u1
（Debian bookworm-security 构建，GTK4 API 同源）。

## 安装脚本
VM 上的一键脚本与全部 deb 保留在 /tmp/webkit-downgrade/（重启即失，可重建）。
依赖链（除主包外均因 soname 差异与系统新版共存）：
- libicu72 / libjpeg62-turbo / libxml2(2.9) / libwoff1 / libavif15 / libmanette-0.2-0
- libdav1d6 / libsvtav1enc1 / libgav1-1 / librav1e0 / libyuv0 / libabsl20220623t64

deb 来源：
- 主包+JSC+libxml2+dav1d: snapshot.debian.org（bookworm-security 时间戳 20250212T143810Z 等）
- 小依赖: Ubuntu noble pool（mirrors.aliyun.com）

## 版本锁定（防 apt upgrade 升回 2.52）
libwebkitgtk-6.0-4 hold + libjavascriptcoregtk-6.0-1 hold

## 验证
2.46.6 下 test-doc.md（标题/加粗/列表/代码块）完整渲染（2026-11-18）。
