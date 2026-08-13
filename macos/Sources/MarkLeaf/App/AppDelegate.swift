import AppKit
import WebKit

/// 应用代理：激活策略、主菜单、主窗口、应用图标、文件打开、生命周期。
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        SettingsService.shared.load()
        installAppIcon()
        // 文件关联：勾选即把 MarkLeaf 设为对应类型默认打开程序（取消还原）
        FileAssociationService.shared.apply(settings: SettingsService.shared.settings)

        NSApp.mainMenu = NativeMenuBuilder().build()

        // 配置完成后再创建唯一初始窗口；早期 Finder 文件只会作为初始加载意图缓存。
        AppWindowManager.shared.completeBootstrapAndEnsureInitialWindow()

        // 编辑器样式就绪后刷新样式/主题菜单；设置变更后广播到所有窗口
        AppWindowManager.shared.primarySession?.onStylesReady = {
            NativeMenuBuilder.refreshIfNeeded()
        }
        SettingsService.shared.onChange = {
            // 缩放等高频设置变更不重建菜单；样式/主题菜单由对应动作显式刷新。
            AppWindowManager.shared.applyPreferencesToAll()
        }

        DispatchQueue.main.async {
            NSApp.activate(ignoringOtherApps: true)
            self.checkPendingRecoveries()
        }

        // 自动化验证
        if let snapshotPath = EditorSession.argumentValue("--snapshot") {
            scheduleSnapshot(path: snapshotPath)
        }
        if let pdfPath = EditorSession.argumentValue("--pdf") {
            schedulePDF(path: pdfPath)
        }
        if let exportPath = EditorSession.argumentValue("--export-pdf") {
            scheduleExportPDF(path: exportPath)
        }
        if CommandLine.arguments.contains("--asset-test") {
            scheduleAssetTest()
        }
        if CommandLine.arguments.contains("--wheel-test") {
            scheduleWheelTest()
        }
        if CommandLine.arguments.contains("--open-prefs") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                AppWindowManager.shared.showPreferences()
                DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                    NSApp.terminate(nil)
                }
            }
        }
        if CommandLine.arguments.contains("--open-prefs-keep") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                AppWindowManager.shared.showPreferences()
            }
        }
        if CommandLine.arguments.contains("--source-selection-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.toggleSourceMode()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    guard let webView = s.webView else {
                        NSApp.terminate(nil)
                        return
                    }
                    let script = """
                    (function () {
                      var content = document.querySelector('.cm-content');
                      if (!content) return;
                      content.focus();
                      var rect = content.getBoundingClientRect();
                      function me(type, x, y, opts) {
                        content.dispatchEvent(new MouseEvent(type, Object.assign({
                          clientX: x, clientY: y, bubbles: true, cancelable: true, button: 0, buttons: 0, detail: 1
                        }, opts || {})));
                      }
                      // 模拟真实拖选：mousedown(buttons=1,detail=1) → mousemove(buttons=1) ×8 → mouseup
                      me('mousedown', rect.left + 80, rect.top + 30, { buttons: 1, detail: 1 });
                      for (var i = 1; i <= 8; i++) {
                        me('mousemove', rect.left + 80 + i * 40, rect.top + 30 + Math.floor(i / 2) * 26, { buttons: 1, detail: 1 });
                      }
                      me('mouseup', rect.left + 400, rect.top + 82, { buttons: 0, detail: 1 });
                    })();
                    """
                    webView.evaluateJavaScript(script) { _, _ in
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                            let check = """
                            JSON.stringify({
                              selLen: window.getSelection() ? window.getSelection().toString().length : -1,
                              bgCount: document.querySelectorAll('.ml-source-selection, .cm-selectionBackground').length,
                              bgColor: document.querySelector('.ml-source-selection, .cm-selectionBackground') ? getComputedStyle(document.querySelector('.ml-source-selection, .cm-selectionBackground')).backgroundColor : 'none',
                              textColor: document.querySelector('.ml-source-selection, .cm-selectionBackground') ? getComputedStyle(document.querySelector('.ml-source-selection, .cm-selectionBackground')).color : 'none',
                              rects: Array.from(document.querySelectorAll('.ml-source-selection, .cm-selectionBackground')).map(function (el) {
                                var r = el.getBoundingClientRect();
                                return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), display: getComputedStyle(el).display };
                              }),
                              layerDisplay: document.querySelector('.cm-selectionLayer') ? getComputedStyle(document.querySelector('.cm-selectionLayer')).display : 'no-layer'
                            })
                            """
                            webView.evaluateJavaScript(check) { result, _ in
                                AppLog.info("--source-selection-test: \(String(describing: result))")
                                webView.takeSnapshot(with: nil) { image, _ in
                                    if let image, let tiff = image.tiffRepresentation,
                                       let rep = NSBitmapImageRep(data: tiff),
                                       let png = rep.representation(using: .png, properties: [:]) {
                                        try? png.write(to: URL(fileURLWithPath: "/tmp/ml-src-selection.png"))
                                    }
                                    NSApp.terminate(nil)
                                }
                            }
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--src-partial-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "短行A 短行B 短行C 短行D。\n这是一段比较长的用于测试的中文文本行，用来验证源码模式下行内部分选区是否显示高亮背景。第二句话继续拉长直到换行。\n第三行。", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    s.toggleSourceMode()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                        guard let webView = s.webView else {
                            NSApp.terminate(nil)
                            return
                        }
                        let script = """
                        (function () {
                          var content = document.querySelector('.cm-content');
                          if (!content) return;
                          content.focus();
                          var lines = document.querySelectorAll('.cm-line');
                          if (!lines.length) return;
                          function me(type, x, y, opts) {
                            content.dispatchEvent(new MouseEvent(type, Object.assign({
                              clientX: x, clientY: y, bubbles: true, cancelable: true, button: 0, buttons: 0, detail: 1
                            }, opts || {})));
                          }
                          function drag(x1, y1, x2, y2, steps) {
                            me('mousedown', x1, y1, { buttons: 1, detail: 1 });
                            for (var i = 1; i <= steps; i++) {
                              me('mousemove', x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps, { buttons: 1, detail: 1 });
                            }
                            me('mouseup', x2, y2, { buttons: 0, detail: 1 });
                          }
                          function snap() {
                            var sel = window.getSelection();
                            var nativeRects = [];
                            if (sel && sel.rangeCount) {
                              nativeRects = Array.from(sel.getRangeAt(0).getClientRects()).map(function (b) {
                                return { x: Math.round(b.left), y: Math.round(b.top), w: Math.round(b.width), h: Math.round(b.height) };
                              });
                            }
                            return {
                              selLen: sel ? sel.toString().length : -1,
                              bgCount: document.querySelectorAll('.ml-source-selection').length + document.querySelectorAll('.cm-selectionBackground').length,
                              bgColor: (function (el) { return el ? getComputedStyle(el).backgroundColor : 'none'; })(document.querySelector('.ml-source-selection') || document.querySelector('.cm-selectionBackground')),
                              rects: Array.from(document.querySelectorAll('.ml-source-selection, .cm-selectionBackground')).map(function (el) {
                                var b = el.getBoundingClientRect();
                                return { x: Math.round(b.x), y: Math.round(b.y), w: Math.round(b.width), h: Math.round(b.height) };
                              }),
                              nativeRects: nativeRects
                            };
                          }
                          var r0 = lines[0].getBoundingClientRect(); // 短行（不换行）
                          var r1 = lines[1].getBoundingClientRect(); // 长行（可能换行）
                          window.__res = {};
                          // A: 短行内正向拖选
                          drag(r0.left + 40, r0.top + r0.height / 2, r0.left + 160, r0.top + r0.height / 2, 5);
                          window.__res.shortFwd = snap();
                          // B: 短行内反向拖选（右→左）
                          drag(r0.left + 200, r0.top + r0.height / 2, r0.left + 60, r0.top + r0.height / 2, 5);
                          window.__res.shortBack = snap();
                          // C: 短行整行三击
                          me('mousedown', r0.left + 40, r0.top + r0.height / 2, { buttons: 1, detail: 3 });
                          me('mouseup', r0.left + 40, r0.top + r0.height / 2, { buttons: 0, detail: 3 });
                          window.__res.shortTriple = snap();
                          // D: 长行内正向拖选（不跨换行）
                          drag(r1.left + 40, r1.top + r1.height / 2, r1.left + 200, r1.top + r1.height / 2, 5);
                          window.__res.longFwd = snap();
                          // E: 长行内反向拖选
                          drag(r1.left + 240, r1.top + r1.height / 2, r1.left + 80, r1.top + r1.height / 2, 5);
                          window.__res.longBack = snap();
                          // F: 长行整行三击
                          me('mousedown', r1.left + 40, r1.top + r1.height / 2, { buttons: 1, detail: 3 });
                          me('mouseup', r1.left + 40, r1.top + r1.height / 2, { buttons: 0, detail: 3 });
                          window.__res.longTriple = snap();
                        })();
                        """
                        webView.evaluateJavaScript(script) { _, _ in
                            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                                let check = "JSON.stringify({ res: window.__res, theme: getComputedStyle(document.documentElement).getPropertyValue('--theme-light'), bg: getComputedStyle(document.body).backgroundColor })"
                                webView.evaluateJavaScript(check) { result, _ in
                                    AppLog.info("--src-partial-test: \(String(describing: result))")
                                    webView.takeSnapshot(with: nil) { image, _ in
                                        if let image, let tiff = image.tiffRepresentation,
                                           let rep = NSBitmapImageRep(data: tiff),
                                           let png = rep.representation(using: .png, properties: [:]) {
                                            try? png.write(to: URL(fileURLWithPath: "/tmp/ml-src-partial.png"))
                                        }
                                        NSApp.terminate(nil)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--src-indent-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "a\tb\n\nnext", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    s.toggleSourceMode()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                        guard let webView = s.webView else {
                            NSApp.terminate(nil)
                            return
                        }
                        // tabSize=4 时测量 "a<TAB>" 渲染宽度
                        s.execute("setSourceIndent", text: "4")
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                            webView.evaluateJavaScript("JSON.stringify({ w4: (function(){ var line=document.querySelector('.cm-line'); if(!line) return -1; var w=document.createTreeWalker(line, NodeFilter.SHOW_TEXT); var n=w.nextNode(); if(!n) return -2; var r=document.createRange(); r.setStart(n,0); r.setEnd(n,Math.min(2,n.textContent.length)); return Math.round(r.getBoundingClientRect().width); })() })") { result, _ in
                                AppLog.info("--src-indent-test(4): \(String(describing: result))")
                                // tabSize=8 时再测
                                s.execute("setSourceIndent", text: "8")
                                DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                                    webView.evaluateJavaScript("JSON.stringify({ w8: (function(){ var line=document.querySelector('.cm-line'); if(!line) return -1; var w=document.createTreeWalker(line, NodeFilter.SHOW_TEXT); var n=w.nextNode(); if(!n) return -2; var r=document.createRange(); r.setStart(n,0); r.setEnd(n,Math.min(2,n.textContent.length)); return Math.round(r.getBoundingClientRect().width); })() })") { result2, _ in
                                        AppLog.info("--src-indent-test(8): \(String(describing: result2))")
                                        NSApp.terminate(nil)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--wheel-gate-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "wheel gate test", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    guard let webView = s.webView else {
                        NSApp.terminate(nil)
                        return
                    }
                    let fire = """
                    (function (source, delta, count) {
                      for (var i = 0; i < count; i++) {
                        var ev = new WheelEvent('wheel', { deltaY: delta, ctrlKey: source === 'pinch', metaKey: source === 'wheel', bubbles: true, cancelable: true });
                        window.dispatchEvent(ev);
                      }
                    })('pinch', -20, 5);
                    """
                    webView.evaluateJavaScript(fire) { _, _ in
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                            let z1 = s.zoomPercent
                            // 设置关掉 ⌘+滚轮，捏合已发过；再发 ⌘+滚轮应无效
                            SettingsService.shared.update { $0.ctrlWheelZoom = false }
                            let fireWheel = "(function(){ var ev = new WheelEvent('wheel', { deltaY: -120, metaKey: true, bubbles: true, cancelable: true }); window.dispatchEvent(ev); })()"
                            webView.evaluateJavaScript(fireWheel) { _, _ in
                                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                                    let z2 = s.zoomPercent
                                    // 打开设置后 ⌘+滚轮应生效
                                    SettingsService.shared.update { $0.ctrlWheelZoom = true }
                                    webView.evaluateJavaScript(fireWheel) { _, _ in
                                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                                            let z3 = s.zoomPercent
                                            let pinchOK = z1 > 100
                                            let wheelOffOK = z2 == z1
                                            let wheelOnOK = z3 > z1
                                            AppLog.info("--wheel-gate-test: pinch后=\(z1) 关设置后⌘滚轮=\(z2) 开设置后⌘滚轮=\(z3) → 捏合\(pinchOK ? "✓" : "✗") ⌘滚轮关闭\(wheelOffOK ? "✓" : "✗") ⌘滚轮开启\(wheelOnOK ? "✓" : "✗")")
                                            NSApp.terminate(nil)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--assoc-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                func current(_ uti: String) -> String {
                    LSCopyDefaultRoleHandlerForContentType(uti as CFString, .editor)?.takeRetainedValue() as String? ?? "nil"
                }
                let mdBefore = current("net.daringfireball.markdown")
                let txtBefore = current("public.plain-text")
                AppLog.info("--assoc-test: 初始 md=\(mdBefore) txt=\(txtBefore)")

                var on = AppSettings()
                on.associateMarkdownFiles = true
                on.associateTextFiles = true
                FileAssociationService.shared.apply(settings: on)
                DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                    let mdAfter = current("net.daringfireball.markdown")
                    let txtAfter = current("public.plain-text")
                    AppLog.info("--assoc-test: 绑定后 md=\(mdAfter) txt=\(txtAfter)（应均为 com.markleaf.app）")

                    var off = AppSettings()
                    off.associateMarkdownFiles = false
                    off.associateTextFiles = false
                    FileAssociationService.shared.apply(settings: off)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                        let mdRestored = current("net.daringfireball.markdown")
                        let txtRestored = current("public.plain-text")
                        AppLog.info("--assoc-test: 还原后 md=\(mdRestored) txt=\(txtRestored)（应还原为初始值）")
                        NSApp.terminate(nil)
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--url-image-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "test", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    // 直接插入在线图片 URL（模拟「插入来自互联网的图片」）
                    s.execute("insertImage", text: "https://picsum.photos/200/300\n在线图")
                    DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                        guard let webView = s.webView else {
                            NSApp.terminate(nil)
                            return
                        }
                        let check = "JSON.stringify({ src: document.querySelector('#editor img') ? document.querySelector('#editor img').getAttribute('src') : 'none', rendered: document.querySelector('#editor img') ? document.querySelector('#editor img').naturalWidth > 0 : false })"
                        webView.evaluateJavaScript(check) { result, _ in
                            AppLog.info("--url-image-test: \(String(describing: result))")
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--src-syntax-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "# 标题\n\n- 列表项\n\n```swift\nlet x = 1\n```", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    s.toggleSourceMode()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                        guard let webView = s.webView else {
                            NSApp.terminate(nil)
                            return
                        }
                        let script = """
                        (function () {
                          var content = document.querySelector('.cm-content');
                          if (!content) return;
                          content.focus();
                          var allTok = document.querySelectorAll('[class*="tok-"]').length;
                          var line0 = document.querySelector('.cm-line');
                          var lineHTML = line0 ? line0.innerHTML.slice(0, 300) : 'none';
                          var bodyHasTok = document.body.innerHTML.indexOf('tok-') >= 0;
                          return JSON.stringify({ allTok: allTok, bodyHasTok: bodyHasTok, line0: lineHTML });
                        })();
                        """
                        webView.evaluateJavaScript(script) { result, _ in
                            AppLog.info("--src-syntax-test: \(String(describing: result))")
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--export-theme-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "# 标题\n\n正文内容。", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    var options = ExportOptions()
                    options.format = "html"
                    options.style = "serif"
                    options.colorScheme = "colors-forest"
                    s.onExportComplete = { ok in
                        let path = "/tmp/ml-export-theme.html"
                        if ok, let html = try? String(contentsOfFile: path, encoding: .utf8) {
                            let hasForest = html.contains("1A221E") || html.contains("4A8A50")
                            AppLog.info("--export-theme-test: 成功=\(ok) 含森林主题色=\(hasForest)")
                        } else {
                            AppLog.info("--export-theme-test: 失败 ok=\(ok)")
                        }
                        NSApp.terminate(nil)
                    }
                    s.runExport(options: options, saveURL: URL(fileURLWithPath: "/tmp/ml-export-theme.html"))
                }
            }
        }
        if CommandLine.arguments.contains("--bold-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "hello world", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    s.executeInlineFormat("toggleBold")
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                        s.requestSnapshot { result in
                            if case .success(let md) = result {
                                let ok = md.trimmingCharacters(in: .whitespacesAndNewlines) == "**hello world**"
                                AppLog.info("--bold-test: \(md.replacingOccurrences(of: "\n", with: "⏎")) \(ok ? "✓整块加粗" : "✗")")
                            }
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--heading-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                let cases: [(String, String, String)] = [
                    ("# A", "promoteHeading", "A"),            // H1 → 段落
                    ("para", "promoteHeading", "# para"),      // 段落 → H1
                    ("- item", "promoteHeading", "# item"),    // 列表项 → H1（移出列表）
                    ("> quote", "promoteHeading", "# quote"),  // 引用 → H1（移出引用）
                    ("para", "demoteHeading", "para"),         // 段落降级 → 保持
                ]
                var index = 0
                func runNext() {
                    guard index < cases.count else {
                        NSApp.terminate(nil)
                        return
                    }
                    let (md, cmd, expect) = cases[index]
                    s.loadDocument(markdown: md, fileURL: nil)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                        s.execute(cmd)
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                            s.requestSnapshot { result in
                                if case .success(let got) = result {
                                    let cleanGot = got.trimmingCharacters(in: .whitespacesAndNewlines)
                                    let ok = cleanGot == expect
                                    AppLog.info("--heading-test [\(cmd)] \(md.replacingOccurrences(of: "\n", with: "⏎")) → \(cleanGot.replacingOccurrences(of: "\n", with: "⏎")) 期望 \(expect) \(ok ? "✓" : "✗")")
                                }
                                index += 1
                                runNext()
                            }
                        }
                    }
                }
                runNext()
            }
        }
        if CommandLine.arguments.contains("--remote-image-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                let md = "# 远程图片测试\n\n![npm](https://nodei.co/npm/source-map-js.png?downloads=true&downloadRank=true)"
                s.loadDocument(markdown: md, fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                    guard let webView = s.webView else {
                        NSApp.terminate(nil)
                        return
                    }
                    let script = """
                    JSON.stringify({
                      imgs: Array.from(document.querySelectorAll('#editor img')).map(function (img) {
                        return { src: img.getAttribute('src'), loaded: img.naturalWidth > 0, w: img.naturalWidth };
                      })
                    })
                    """
                    webView.evaluateJavaScript(script) { result, _ in
                        AppLog.info("--remote-image-test: \(String(describing: result))")
                        NSApp.terminate(nil)
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--selection-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) { [weak self] in
                guard let webView = self?.sessionWebView() else {
                    AppLog.error("--selection-test: webView 未就绪")
                    NSApp.terminate(nil)
                    return
                }
                let script = """
                (function () {
                  var el = document.querySelector('.ProseMirror');
                  if (!el) return;
                  el.focus();
                  // 用 execCommand 触发 ProseMirror 自己的选择同步
                  try { document.execCommand('selectAll'); } catch (e) {}
                })();
                """
                webView.evaluateJavaScript(script) { _, _ in
                    let check = "JSON.stringify({ len: window.getSelection() ? window.getSelection().toString().length : -1, deco: document.querySelectorAll('.markleaf-themed-selection').length })"
                    webView.evaluateJavaScript(check) { result, _ in
                        AppLog.info("--selection-test: 选区信息=\(String(describing: result))")
                    }
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                        webView.takeSnapshot(with: nil) { image, _ in
                            if let image, let tiff = image.tiffRepresentation,
                               let rep = NSBitmapImageRep(data: tiff),
                               let png = rep.representation(using: .png, properties: [:]) {
                                try? png.write(to: URL(fileURLWithPath: "/tmp/ml-selection.png"))
                                AppLog.info("--selection-test: 截图已保存")
                            }
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--workspace-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                let dir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("ml-ws-test")
                try? FileManager.default.removeItem(at: dir)
                try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                try? "a".write(to: dir.appendingPathComponent("a.md"), atomically: true, encoding: .utf8)
                let sub = dir.appendingPathComponent("sub")
                try? FileManager.default.createDirectory(at: sub, withIntermediateDirectories: true)
                try? "c".write(to: sub.appendingPathComponent("c.md"), atomically: true, encoding: .utf8)
                s.loadWorkspace(dir.path)
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
                    let before = s.workspaceTree.count
                    AppLog.info("--workspace-test: 树条目=\(before)")
                    s.setWorkspaceListMode(true)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                        let docs = s.workspaceDocuments
                        AppLog.info("--workspace-test: 列表模式文档数=\(docs.count)（期望 2：根 a.md + 子目录 c.md）名称=\(docs.map { $0.name })")
                        s.setWorkspaceListMode(false)
                        try? "b".write(to: dir.appendingPathComponent("b.md"), atomically: true, encoding: .utf8)
                        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                            let after = s.workspaceTree.count
                            AppLog.info("--workspace-test: 自动刷新后树条目=\(after)（期望 \(before + 1)）")
                            try? FileManager.default.removeItem(at: dir)
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--gutter-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.toggleSourceMode()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    s.zoomIn()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                        guard let webView = s.webView else {
                            NSApp.terminate(nil)
                            return
                        }
                        let script = "JSON.stringify({ gutter: document.querySelector('.cm-gutters') ? getComputedStyle(document.querySelector('.cm-gutters')).backgroundColor : 'none', sourceFont: getComputedStyle(document.documentElement).getPropertyValue('--ml-source-font-size').trim(), zoom: \(s.zoomPercent) })"
                        webView.evaluateJavaScript(script) { result, _ in
                            AppLog.info("--gutter-test: \(String(describing: result))")
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--sidebar-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    AppLog.error("--sidebar-test: 无会话")
                    NSApp.terminate(nil)
                    return
                }
                func placeholderVisible(in view: NSView) -> Bool? {
                    if view.identifier == SidebarView.emptyStateIdentifier {
                        return !view.isHidden
                    }
                    for sub in view.subviews {
                        if let found = placeholderVisible(in: sub) {
                            return found
                        }
                    }
                    return nil
                }
                let snapshot = { () -> String in
                    guard let window = s.webView?.window,
                          let visible = placeholderVisible(in: window.contentView ?? NSView()) else {
                        return "未找到占位文案"
                    }
                    return visible ? "可见" : "隐藏"
                }
                AppLog.info("--sidebar-test: 初始 visible=\(s.sidebarVisible) 占位=\(snapshot())")
                s.showOutlineTab()
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    AppLog.info("--sidebar-test: 切大纲后 tab=\(s.sidebarTabIndex) 占位=\(snapshot())")
                    s.showWorkspaceTab()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                        AppLog.info("--sidebar-test: 切回工作区后 tab=\(s.sidebarTabIndex) 占位=\(snapshot())（无文件夹时应可见）")
                        s.toggleSidebar()
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                            AppLog.info("--sidebar-test: 切侧栏后 visible=\(s.sidebarVisible)")
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--wheel-test") {
            scheduleWheelTest()
        }
        if CommandLine.arguments.contains("--open-prefs") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                AppWindowManager.shared.showPreferences()
                DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                    NSApp.terminate(nil)
                }
            }
        }
        if CommandLine.arguments.contains("--open-prefs-keep") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                AppWindowManager.shared.showPreferences()
            }
        }
        if CommandLine.arguments.contains("--source-selection-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.toggleSourceMode()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    guard let webView = s.webView else {
                        NSApp.terminate(nil)
                        return
                    }
                    let script = """
                    (function () {
                      var content = document.querySelector('.cm-content');
                      if (!content) return;
                      content.focus();
                      var rect = content.getBoundingClientRect();
                      function me(type, x, y, opts) {
                        content.dispatchEvent(new MouseEvent(type, Object.assign({
                          clientX: x, clientY: y, bubbles: true, cancelable: true, button: 0, buttons: 0, detail: 1
                        }, opts || {})));
                      }
                      // 模拟真实拖选：mousedown(buttons=1,detail=1) → mousemove(buttons=1) ×8 → mouseup
                      me('mousedown', rect.left + 80, rect.top + 30, { buttons: 1, detail: 1 });
                      for (var i = 1; i <= 8; i++) {
                        me('mousemove', rect.left + 80 + i * 40, rect.top + 30 + Math.floor(i / 2) * 26, { buttons: 1, detail: 1 });
                      }
                      me('mouseup', rect.left + 400, rect.top + 82, { buttons: 0, detail: 1 });
                    })();
                    """
                    webView.evaluateJavaScript(script) { _, _ in
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                            let check = """
                            JSON.stringify({
                              selLen: window.getSelection() ? window.getSelection().toString().length : -1,
                              bgCount: document.querySelectorAll('.ml-source-selection, .cm-selectionBackground').length,
                              bgColor: document.querySelector('.ml-source-selection, .cm-selectionBackground') ? getComputedStyle(document.querySelector('.ml-source-selection, .cm-selectionBackground')).backgroundColor : 'none',
                              textColor: document.querySelector('.ml-source-selection, .cm-selectionBackground') ? getComputedStyle(document.querySelector('.ml-source-selection, .cm-selectionBackground')).color : 'none',
                              rects: Array.from(document.querySelectorAll('.ml-source-selection, .cm-selectionBackground')).map(function (el) {
                                var r = el.getBoundingClientRect();
                                return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), display: getComputedStyle(el).display };
                              }),
                              layerDisplay: document.querySelector('.cm-selectionLayer') ? getComputedStyle(document.querySelector('.cm-selectionLayer')).display : 'no-layer'
                            })
                            """
                            webView.evaluateJavaScript(check) { result, _ in
                                AppLog.info("--source-selection-test: \(String(describing: result))")
                                webView.takeSnapshot(with: nil) { image, _ in
                                    if let image, let tiff = image.tiffRepresentation,
                                       let rep = NSBitmapImageRep(data: tiff),
                                       let png = rep.representation(using: .png, properties: [:]) {
                                        try? png.write(to: URL(fileURLWithPath: "/tmp/ml-src-selection.png"))
                                    }
                                    NSApp.terminate(nil)
                                }
                            }
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--bold-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.loadDocument(markdown: "hello world", fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    s.executeInlineFormat("toggleBold")
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                        s.requestSnapshot { result in
                            if case .success(let md) = result {
                                let ok = md.trimmingCharacters(in: .whitespacesAndNewlines) == "**hello world**"
                                AppLog.info("--bold-test: \(md.replacingOccurrences(of: "\n", with: "⏎")) \(ok ? "✓整块加粗" : "✗")")
                            }
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--heading-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                let cases: [(String, String, String)] = [
                    ("# A", "promoteHeading", "A"),            // H1 → 段落
                    ("para", "promoteHeading", "# para"),      // 段落 → H1
                    ("- item", "promoteHeading", "# item"),    // 列表项 → H1（移出列表）
                    ("> quote", "promoteHeading", "# quote"),  // 引用 → H1（移出引用）
                    ("para", "demoteHeading", "para"),         // 段落降级 → 保持
                ]
                var index = 0
                func runNext() {
                    guard index < cases.count else {
                        NSApp.terminate(nil)
                        return
                    }
                    let (md, cmd, expect) = cases[index]
                    s.loadDocument(markdown: md, fileURL: nil)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                        s.execute(cmd)
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                            s.requestSnapshot { result in
                                if case .success(let got) = result {
                                    let cleanGot = got.trimmingCharacters(in: .whitespacesAndNewlines)
                                    let ok = cleanGot == expect
                                    AppLog.info("--heading-test [\(cmd)] \(md.replacingOccurrences(of: "\n", with: "⏎")) → \(cleanGot.replacingOccurrences(of: "\n", with: "⏎")) 期望 \(expect) \(ok ? "✓" : "✗")")
                                }
                                index += 1
                                runNext()
                            }
                        }
                    }
                }
                runNext()
            }
        }
        if CommandLine.arguments.contains("--remote-image-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                let md = "# 远程图片测试\n\n![npm](https://nodei.co/npm/source-map-js.png?downloads=true&downloadRank=true)"
                s.loadDocument(markdown: md, fileURL: nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                    guard let webView = s.webView else {
                        NSApp.terminate(nil)
                        return
                    }
                    let script = """
                    JSON.stringify({
                      imgs: Array.from(document.querySelectorAll('#editor img')).map(function (img) {
                        return { src: img.getAttribute('src'), loaded: img.naturalWidth > 0, w: img.naturalWidth };
                      })
                    })
                    """
                    webView.evaluateJavaScript(script) { result, _ in
                        AppLog.info("--remote-image-test: \(String(describing: result))")
                        NSApp.terminate(nil)
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--selection-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) { [weak self] in
                guard let webView = self?.sessionWebView() else {
                    AppLog.error("--selection-test: webView 未就绪")
                    NSApp.terminate(nil)
                    return
                }
                let script = """
                (function () {
                  var el = document.querySelector('.ProseMirror');
                  if (!el) return;
                  el.focus();
                  // 用 execCommand 触发 ProseMirror 自己的选择同步
                  try { document.execCommand('selectAll'); } catch (e) {}
                })();
                """
                webView.evaluateJavaScript(script) { _, _ in
                    let check = "JSON.stringify({ len: window.getSelection() ? window.getSelection().toString().length : -1, deco: document.querySelectorAll('.markleaf-themed-selection').length })"
                    webView.evaluateJavaScript(check) { result, _ in
                        AppLog.info("--selection-test: 选区信息=\(String(describing: result))")
                    }
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                        webView.takeSnapshot(with: nil) { image, _ in
                            if let image, let tiff = image.tiffRepresentation,
                               let rep = NSBitmapImageRep(data: tiff),
                               let png = rep.representation(using: .png, properties: [:]) {
                                try? png.write(to: URL(fileURLWithPath: "/tmp/ml-selection.png"))
                                AppLog.info("--selection-test: 截图已保存")
                            }
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--workspace-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                let dir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("ml-ws-test")
                try? FileManager.default.removeItem(at: dir)
                try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                try? "a".write(to: dir.appendingPathComponent("a.md"), atomically: true, encoding: .utf8)
                let sub = dir.appendingPathComponent("sub")
                try? FileManager.default.createDirectory(at: sub, withIntermediateDirectories: true)
                try? "c".write(to: sub.appendingPathComponent("c.md"), atomically: true, encoding: .utf8)
                s.loadWorkspace(dir.path)
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
                    let before = s.workspaceTree.count
                    AppLog.info("--workspace-test: 树条目=\(before)")
                    s.setWorkspaceListMode(true)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                        let docs = s.workspaceDocuments
                        AppLog.info("--workspace-test: 列表模式文档数=\(docs.count)（期望 2：根 a.md + 子目录 c.md）名称=\(docs.map { $0.name })")
                        s.setWorkspaceListMode(false)
                        try? "b".write(to: dir.appendingPathComponent("b.md"), atomically: true, encoding: .utf8)
                        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) {
                            let after = s.workspaceTree.count
                            AppLog.info("--workspace-test: 自动刷新后树条目=\(after)（期望 \(before + 1)）")
                            try? FileManager.default.removeItem(at: dir)
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
        if CommandLine.arguments.contains("--gutter-test") {
            DispatchQueue.main.asyncAfter(deadline: .now() + 4.0) {
                guard let s = AppWindowManager.shared.primarySession else {
                    NSApp.terminate(nil)
                    return
                }
                s.toggleSourceMode()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                    s.zoomIn()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                        guard let webView = s.webView else {
                            NSApp.terminate(nil)
                            return
                        }
                        let script = "JSON.stringify({ gutter: document.querySelector('.cm-gutters') ? getComputedStyle(document.querySelector('.cm-gutters')).backgroundColor : 'none', sourceFont: getComputedStyle(document.documentElement).getPropertyValue('--ml-source-font-size').trim(), zoom: \(s.zoomPercent) })"
                        webView.evaluateJavaScript(script) { result, _ in
                            AppLog.info("--gutter-test: \(String(describing: result))")
                            NSApp.terminate(nil)
                        }
                    }
                }
            }
        }
    }

    // MARK: - 自动化：⌘/Ctrl+滚轮缩放

    private func scheduleWheelTest() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 6.0) { [weak self] in
            guard let webView = self?.sessionWebView() else {
                AppLog.error("--wheel-test: webView 未就绪")
                NSApp.terminate(nil)
                return
            }
            AppLog.info("--wheel-test: 先模拟触控板捏合(10×小delta)，再模拟 ⌘+滚轮")
            let script = """
            (function () {
              // 触控板捏合：ctrlKey + 连续小 delta → 连续平滑
              for (var i = 0; i < 10; i++) {
                var pinch = new WheelEvent('wheel', { deltaY: -10, ctrlKey: true, bubbles: true, cancelable: true });
                window.dispatchEvent(pinch);
              }
              // ⌘+滚轮：metaKey + 大 delta → 离散跳档
              var wheel = new WheelEvent('wheel', { deltaY: -120, metaKey: true, bubbles: true, cancelable: true });
              window.dispatchEvent(wheel);
              // macOS 真实滚轮是小 delta 事件：6×+5（阈值12 → 2 档，不得直落 50%）
              for (var i = 0; i < 6; i++) {
                var small = new WheelEvent('wheel', { deltaY: 5, metaKey: true, bubbles: true, cancelable: true });
                window.dispatchEvent(small);
              }
            })();
            """
            webView.evaluateJavaScript(script) { _, error in
                if let error {
                    AppLog.error("--wheel-test 注入失败: \(error.localizedDescription)")
                }
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 2.0) {
                let percent = AppWindowManager.shared.primarySession?.zoomPercent ?? -1
                AppLog.info("--wheel-test 完成: zoomPercent=\(percent)（期望 110 = 捏合连续125 → 滚轮1档150 → 小delta滚轮2档110）")
                NSApp.terminate(nil)
            }
        }
    }

    // MARK: - 自动化：完整 PDF 导出流水线（exportDocument → exportContent → PDFGenerator）

    private func scheduleExportPDF(path: String) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 6.0) { [weak self] in
            guard let session = self?.primarySession() else {
                AppLog.error("--export-pdf: 无会话")
                NSApp.terminate(nil)
                return
            }
            session.onExportComplete = { success in
                AppLog.info("--export-pdf 完成: \(success ? "成功" : "失败")")
                NSApp.terminate(nil)
            }
            var options = ExportOptions()
            options.format = "pdf"
            options.style = session.currentStyleId
            session.runExport(options: options, saveURL: URL(fileURLWithPath: path))
        }
    }

    // MARK: - 自动化：图片资源服务（assets.local 重写 → markleaf-asset 读取）

    private func scheduleAssetTest() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 6.0) { [weak self] in
            guard let webView = self?.sessionWebView() else {
                AppLog.error("--asset-test: webView 未就绪")
                NSApp.terminate(nil)
                return
            }
            AppLog.info("--asset-test: 注入图片探测")

            var imagePath = ResourceLocator.stylesDirectory?
                .deletingLastPathComponent().deletingLastPathComponent()
                .appendingPathComponent("App.png").path ?? "/tmp/nonexistent.png"
            if !FileManager.default.fileExists(atPath: imagePath) {
                imagePath = "/tmp/nonexistent.png"
            }
            let encoded = imagePath.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? imagePath
            let script = """
            (function () {
              function probe(tag, src) {
                var img = new Image();
                img.src = src;
                img.onload = function () {
                  window.webkit.messageHandlers.markleaf.postMessage({
                    protocolVersion: 1, type: 'error', documentId: 'asset-test', revision: 0,
                    payload: { message: 'PROBE_' + tag + '_LOADED:' + img.naturalWidth + 'x' + img.naturalHeight + ':' + img.currentSrc }
                  });
                };
                img.onerror = function () {
                  window.webkit.messageHandlers.markleaf.postMessage({
                    protocolVersion: 1, type: 'error', documentId: 'asset-test', revision: 0,
                    payload: { message: 'PROBE_' + tag + '_FAILED:' + img.currentSrc }
                  });
                };
                document.body.appendChild(img);
              }
              // 1) 经 shim 重写路径（assets.local → markleaf-asset）
              probe('REWRITE', 'https://assets.local/image?path=' + encodeURIComponent('\(encoded)'));
              // 2) 直接 markleaf-asset（隔离测试 handler）
              probe('DIRECT', 'markleaf-asset://image?path=' + encodeURIComponent('\(encoded)'));
            })();
            """
            webView.evaluateJavaScript(script) { _, error in
                if let error {
                    AppLog.error("--asset-test 注入失败: \(error.localizedDescription)")
                }
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 5.0) {
                AppLog.info("--asset-test 结束")
                NSApp.terminate(nil)
            }
        }
    }

    private func primarySession() -> EditorSession? {
        AppWindowManager.shared.primarySession
    }

    private var terminationRequestInProgress = false

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard !terminationRequestInProgress else { return .terminateLater }
        terminationRequestInProgress = true
        AppWindowManager.shared.requestApplicationTermination { [weak self, weak sender] allowed in
            self?.terminationRequestInProgress = false
            sender?.reply(toApplicationShouldTerminate: allowed)
        }
        return .terminateLater
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }

    func applicationWillTerminate(_ notification: Notification) {
        // 正常退出：清理本进程的恢复快照（崩溃遗留的留给下次恢复）
        RecoveryService.shared.deleteOwnFiles()
        // 对齐 Windows 1.1.3：退出时自动清理 7 天前的日志
        AppLog.cleanupOldLogs(olderThanDays: 7)
    }

    /// 启动时检测崩溃遗留快照（对应 C# RecoverUnsavedFiles）。
    func checkPendingRecoveries() {
        let pending = RecoveryService.pendingRecoveries()
        guard !pending.isEmpty else { return }
        AppLog.info("发现 \(pending.count) 个待恢复快照")
        DispatchQueue.main.async {
            // 统一走 AppWindowManager（持有控制器，保证按钮可用）
            AppWindowManager.shared.showRecoveryDialog()
        }
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
        true
    }

    // MARK: - 文件关联打开（对应 Info.plist CFBundleDocumentTypes / 双击打开）

    func application(_ application: NSApplication, open urls: [URL]) {
        AppWindowManager.shared.openExternalDocuments(urls)
    }

    // MARK: - 自动化验证

    private func sessionWebView() -> WKWebView? {
        AppWindowManager.shared.primarySession?.webView
    }

    private func scheduleSnapshot(path: String) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 6.0) { [weak self] in
            guard let webView = self?.sessionWebView() else {
                AppLog.error("--snapshot: webView 未就绪")
                NSApp.terminate(nil)
                return
            }
            let diagScript = """
            JSON.stringify({
              title: document.title,
              readyState: document.readyState,
              hasBridge: !!(window.chrome && window.chrome.webview && window.chrome.webview.postMessage),
              chromeType: typeof window.chrome,
              bodyLength: document.body ? document.body.innerHTML.length : -1,
              scriptCount: document.scripts ? document.scripts.length : -1,
              styleSheetCount: document.styleSheets ? document.styleSheets.length : -1,
              resources: performance.getEntriesByType('resource').map(function (r) { return r.name; }).slice(0, 12),
              editorHTML: document.getElementById('editor') ? document.getElementById('editor').innerHTML.slice(0, 120) : 'none',
              scrollbarColor: getComputedStyle(document.documentElement).scrollbarColor,
              colorScheme: getComputedStyle(document.documentElement).colorScheme,
              probe: window.__probe || 'pending'
            })
            """
            let probeScript = """
            (function () {
              var s = document.createElement('script');
              s.src = 'native-shim.js';
              s.onload = function () { window.__probe = 'loaded:' + (!!(window.chrome && window.chrome.webview)); };
              s.onerror = function (e) { window.__probe = 'error'; };
              document.head.appendChild(s);
            })();
            """
            webView.evaluateJavaScript(probeScript) { _, _ in }
            webView.evaluateJavaScript(diagScript) { result, error in
                if let error {
                    AppLog.error("--snapshot 页面诊断失败: \(error.localizedDescription)")
                } else if let result {
                    AppLog.info("--snapshot 页面状态: \(String(describing: result))")
                }
            }
            // 滚动一小段让 overlay 滚动条显现，再截图验证颜色
            webView.evaluateJavaScript("window.scrollBy(0, 120); void 0;")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { [weak self] in
                self?.captureSnapshot(webView: webView, path: path)
            }
        }
    }

    private func captureSnapshot(webView: WKWebView, path: String) {
        webView.takeSnapshot(with: nil) { image, error in
            guard let image else {
                AppLog.error("--snapshot 失败: \(error?.localizedDescription ?? "未知错误")")
                NSApp.terminate(nil)
                return
            }
            guard let tiff = image.tiffRepresentation,
                  let rep = NSBitmapImageRep(data: tiff),
                  let png = rep.representation(using: .png, properties: [:]) else {
                AppLog.error("--snapshot 编码失败")
                NSApp.terminate(nil)
                return
            }
            do {
                try png.write(to: URL(fileURLWithPath: path))
                AppLog.info("--snapshot 已保存: \(path)")
            } catch {
                AppLog.error("--snapshot 写入失败: \(error.localizedDescription)")
            }
            NSApp.terminate(nil)
        }
    }

    private func schedulePDF(path: String) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 6.0) { [weak self] in
            guard let webView = self?.sessionWebView() else {
                AppLog.error("--pdf: webView 未就绪")
                NSApp.terminate(nil)
                return
            }
            webView.createPDF(configuration: WKPDFConfiguration()) { result in
                switch result {
                case .success(let data):
                    do {
                        try data.write(to: URL(fileURLWithPath: path))
                        AppLog.info("--pdf 已保存: \(path) (\(data.count) bytes)")
                    } catch {
                        AppLog.error("--pdf 写入失败: \(error.localizedDescription)")
                    }
                case .failure(let error):
                    AppLog.error("--pdf 生成失败: \(error.localizedDescription)")
                }
                NSApp.terminate(nil)
            }
        }
    }

    // MARK: - 应用图标

    private func installAppIcon() {
        let fm = FileManager.default
        var candidates: [URL] = []
        if let bundleResources = Bundle.main.resourceURL {
            candidates.append(bundleResources.appendingPathComponent("AppIcon.icns"))
            candidates.append(bundleResources.appendingPathComponent("App.png"))
        }
        if let editorWebDir = ResourceLocator.editorWebDirectory {
            let parent = editorWebDir.deletingLastPathComponent()
            candidates.append(parent.appendingPathComponent("AppIcon.icns"))
            candidates.append(parent.appendingPathComponent("App.png"))
        }

        for candidate in candidates where fm.fileExists(atPath: candidate.path) {
            if let image = NSImage(contentsOf: candidate) {
                NSApp.applicationIconImage = image
                return
            }
        }
        AppLog.info("未找到应用图标资源")
    }
}
