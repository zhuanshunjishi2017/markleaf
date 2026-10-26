# MarkLeaf for Android

MarkLeaf 的 Android 宿主（第四端）：查看 + 轻编辑 + 导出的 Markdown 编辑器，
复用桌面端的 `editor-web` 渲染内核（ProseMirror 可视化编辑 + CodeMirror 源码模式）。

## 架构

- **Kotlin + Jetpack Compose 壳**：标签栏、侧栏（工作区/大纲）、状态栏、设置、导出对话框
- **每标签一个 WebView**：装载 `packages/editor-web/dist`（与 macOS/Windows/VS Code 逐字节同源）
- **EditorBridge**：`protocol.ts` JSON 消息协议的 Kotlin 实现（`window.postMessage` 注入 + `markleafHost` JS 接口回传）
- **SAF 文档源**：单文件 + 授权文件夹（`DocumentSource` 抽象，远程源预留接口）
- **导出**：HTML（前端 export-html 分包）+ PDF（PrintManager）

设计文档：`docs/superpowers/specs/2026-10-27-android-host-design.md`
实现计划：`docs/superpowers/plans/2026-10-27-android-host.md`

## 构建顺序（先内核后壳）

```bash
# 1. 构建共享前端（仓库根目录）
corepack pnpm --dir packages/editor-core install --frozen-lockfile
corepack pnpm --dir packages/editor-web install --frozen-lockfile
corepack pnpm --dir packages/editor-core build:renderer
corepack pnpm --dir packages/editor-web build

# 2. 构建 Android（需要 JDK 17+ 与 Android SDK，见 local.properties / ANDROID_HOME）
cd apps/android
./gradlew assembleDebug          # 产物 app/build/outputs/apk/debug/app-debug.apk
./gradlew testDebugUnitTest      # JVM 单测（Robolectric）
```

`sync-editor-assets` Gradle 任务会把 `packages/editor-web/dist` 拷入 APK
`assets/editor/` 并生成 SHA-256 清单（`asset-manifest.json`）；dist 缺失时构建
失败并提示先跑第 1 步。

## 环境要求

- JDK 17+（工具链验证过 21）
- Android SDK：platform 36 + build-tools 36（`sdkmanager "platforms;android-36" "build-tools;36.0.0" platform-tools`）
- `apps/android/local.properties`：`sdk.dir=<SDK 路径>`（或设 `ANDROID_HOME`）
- minSdk 26 / targetSdk 36

## 发布

- `./gradlew assembleRelease`（当前 debug 签名占位；正式分发前替换签名配置）
- CI（`.github/workflows/android.yml`）：push 时构建 + 单测，release tag 上传 APK artifact

## 已知限制（第一版）

- PDF 页眉/页脚由系统打印框架决定，桌面端自定义 {page}/{pages} 页眉页脚暂缺
- 远程文档源（SMB/WebDAV）未实现（`DocumentSource` 接口已预留）
- 标签拖拽排序、外接键盘快捷键管理未实现
