import java.security.MessageDigest

// 把根仓 packages/editor-web/dist 同步进 APK assets，并生成资产清单。
// dist 是构建产物：缺失时直接失败并提示先跑根仓构建，禁止手改同步结果。

val editorDistDir = layout.projectDirectory
    .dir("../../../packages/editor-web/dist")
val editorAssetsDir = layout.buildDirectory.dir("generated/editorAssets/editor")
val stylesDir = layout.projectDirectory
    .dir("../../../packages/styles")

// 配置期解析出的 File，供执行期使用（兼容配置缓存，不捕获脚本对象）。
val assetsDirProvider = editorAssetsDir.map { it.asFile }

val syncEditorAssets by tasks.registering(Sync::class) {
    group = "markleaf"
    description = "同步 editor-web dist 到 Android assets"

    doFirst {
        val index = editorDistDir.file("index.html").asFile
        check(index.exists()) {
            "editor-web dist 不存在：请先在仓库根目录执行 pnpm build:editor-web（预期 $index）"
        }
        // Sync 的对账在任务执行时进行；历史残留（如旧构建的 " 2" 副本）在此一并清掉。
        editorAssetsDir.get().asFile.deleteRecursively()
    }

    from(editorDistDir) {
        exclude("native-shim.js")
        exclude("**/* 2.*", "** 2")
    }
    into(editorAssetsDir)

    // 文档主题样式：base.css（排版基础）+ 全部颜色主题 + 常用排版样式。
    // 宿主 applyStyles 载荷需要它们定义 --theme-*/--ml-* 变量；
    // 颜色主题与排版样式列表见 StyleAssets（首选项页可选）。
    from(stylesDir) {
        include("base.css")
        include("colors-*.css")
        include("serif.css", "sans.css", "minimal.css", "notebook.css", "magazine.css")
        into("styles")
    }
}

val writeAssetManifest by tasks.registering {
    group = "markleaf"
    description = "生成 synced editor 资产的 SHA-256 清单"
    dependsOn(syncEditorAssets)
    inputs.dir(editorAssetsDir)

    doLast {
        val assetsDir = assetsDirProvider.get()
        val output = File(assetsDir, "asset-manifest.json")
        val digest = MessageDigest.getInstance("SHA-256")
        val entries = assetsDir.walkTopDown()
            .filter { it.isFile && it.name != "asset-manifest.json" }
            .sortedBy { it.relativeTo(assetsDir).path }
            .associate { file ->
                val relative = file.relativeTo(assetsDir).path.replace('\\', '/')
                relative to digest.digest(file.readBytes()).joinToString("") { "%02x".format(it) }
            }
        output.writeText(entries.entries.joinToString(",", "{", "}") { (path, hash) ->
            "\"${path}\":\"${hash}\""
        })
    }
}

// Android 版 native-shim.js：chrome.webview polyfill，postMessage 走 markleafHost JS 桥。
// 与 macOS shim 同构（EditorWeb/native-shim），仅宿主名与 hostPlatform 不同。
val writeNativeShim by tasks.registering {
    group = "markleaf"
    description = "生成 Android 版 native-shim.js"
    dependsOn(syncEditorAssets)
    inputs.file(editorAssetsDir.map { it.file("index.html") })

    doLast {
        val assetsDir = assetsDirProvider.get()
        File(assetsDir, "native-shim.js").writeText(
            """
            (function () {
              'use strict';
              if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
                return;
              }
              var listeners = [];
              var webview = {
                hostPlatform: 'Android',
                postMessage: function (message) {
                  window.markleafHost.postMessage(JSON.stringify(message));
                },
                postMessageWithAdditionalObjects: function (message) {
                  window.markleafHost.postMessage(JSON.stringify(message));
                },
                addEventListener: function (type, listener) {
                  if (type === 'message' && typeof listener === 'function') {
                    listeners.push(listener);
                  }
                }
              };
              try {
                Object.defineProperty(window, 'chrome', { value: window.chrome || {}, writable: true, configurable: true });
                Object.defineProperty(window.chrome, 'webview', { value: webview, writable: true, configurable: true });
              } catch (e) {
                window.chrome = window.chrome || {};
                window.chrome.webview = webview;
              }
              window.addEventListener('message', function (event) {
                var data = event && event.data;
                if (data && typeof data === 'object' && typeof data.protocolVersion === 'number') {
                  for (var i = 0; i < listeners.length; i++) {
                    try { listeners[i]({ data: data }); } catch (e) { /* ignore */ }
                  }
                }
              });
            })();
            """.trimIndent()
        )
        // index.html 注入 shim（幂等）
        val index = File(assetsDir, "index.html")
        if (!index.readText().contains("native-shim.js")) {
            val html = index.readText()
                .replace("</head>", "<script src=\"native-shim.js\"></script></head>")
            index.writeText(html)
        }
    }
}

tasks.named("preBuild") {
    dependsOn(writeAssetManifest, writeNativeShim)
}
