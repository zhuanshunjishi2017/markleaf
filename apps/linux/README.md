# MarkLeaf Linux 宿主

GTK4 + libadwaita + WebKitGTK 6.0 原生壳,与 Windows/macOS/Android/VS Code 端共享同一编辑内核(`packages/editor-web` dist 逐字节同源)。

## 功能(第一版)

- 可视化编辑与源码模式、工作区侧栏(树/搜索)、大纲、状态栏
- 多窗口 + 多标签(每标签常驻 WebView)、会话恢复、崩溃恢复(`.recovery.json` 共享格式)
- 原编码保留读写(BOM/GBK/UTF-16)、外部修改冲突检测
- 导出 HTML / 长图 PNG / 打印(打印对话框内可选另存为 PDF)
- 深浅色跟随、四语言(zh-Hans/zh-Hant/en-US/ja-JP)、快捷键自定义(settings.json)
- 自定义 scheme:`markleaf://`(页面)与 `markleaf-asset://`(文档图片)

**已知降级**(v1 显式声明,不静默):PDF 页眉页脚/页码/大纲书签;长图按高度分片(单文件整图可用);导出对话框的格式切换/纸张/边距与打印友好模式(浅色覆盖)——深色模式下打印将带深色底,请先切换浅色;Unity 全局菜单。

## 构建(开发机为 macOS 时经容器)

```bash
# 0) 容器运行时:brew install colima && colima start(或 Docker Desktop)
# 1) 装配内核产物(宿主机):构建 editor dist → 注入 Linux shim → 资产清单
apps/linux/script/prepare_resources.sh
# 2) 构建/测试/冒烟(容器,首次自动建镜像)
apps/linux/script/dev/container.sh build
apps/linux/script/dev/container.sh test
apps/linux/script/dev/container.sh clippy -- -D warnings
apps/linux/script/dev/container.sh smoke
# 3) 运行(容器内 xvfb;真机直接 cargo run)
apps/linux/script/dev/container.sh run bash -c \
  'Xvfb :97 -screen 0 1024x768x24 & sleep 1; DISPLAY=:97 cargo run --bin markleaf'
```

Linux 真机(Ubuntu 24.04+/Fedora 40+):

```bash
sudo apt install libgtk-4-dev libadwaita-1-dev libwebkitgtk-6.0-dev build-essential pkg-config
corepack pnpm install:editor-web && corepack pnpm build:editor-web
MARKLEAF_USE_BUILT_EDITOR_WEB=1 apps/linux/script/prepare_resources.sh
cd apps/linux && cargo run --release --bin markleaf
```

## 环境变量(容器/无头调试用,真机无需)

- `WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1`:容器无 user namespace 时 WebKit 沙箱豁免
- `WEBKIT_DISABLE_COMPOSITING_MODE=1` / `WEBKIT_DISABLE_DMABUF_RENDERER=1` / `GSK_RENDERER=cairo`:无 GPU 显示环境
- `MARKLEAF_SMOKE_STRICT_PIXEL=1`:冒烟强制像素断言(默认无 GPU 环境降级告警)
- `MARKLEAF_EDITOR_ROOT`:编辑器资源目录(smoke 用)

## 分发

- Flatpak:`apps/linux/script/flatpak/com.markleaf.MarkLeaf.yml`(org.gnome.Platform 48;沙箱权限 `--filesystem=home` 支撑工作区语义,理由见 appdata)
- AppImage:`apps/linux/script/appimage/build.sh`(实验性,ubuntu-24.04 栈捆绑)
- CI:`linux.yml`(push/PR)与 `release-linux.yml`(tag → Flatpak bundle + AppImage 挂 Release)

## 真机验收清单(发布前人工执行)

- [ ] GNOME 47+ 与 KDE 外观(HiDPI 缩放 1x/2x)
- [ ] Wayland 与 X11 会话各一次
- [ ] ibus 与 fcitx5 中文 IME(输入/选字/删除不丢字)
- [ ] 深浅色切换跟随系统
- [ ] 打开文件夹工作区:树/搜索/大纲、外部改动自动刷新
- [ ] 保存:GBK 文件按原编码写回、UTF-16 BOM 保留、冲突对话框
- [ ] 崩溃恢复:kill -9 后重启恢复未保存内容
- [ ] 打印对话框(可选另存为 PDF)产出正确分页
- [ ] 长图导出与 HTML 自包含导出(含图片内联)
- [ ] Flatpak 安装包:portal 文件对话框、工作区目录访问
- [ ] AppImage 在 Ubuntu 22.04/24.04、Fedora 41、Arch 冒烟
- [ ] 大文档(≥1MB)打开/编辑流畅
- [ ] 主题所需字体缺失时的回退提示
