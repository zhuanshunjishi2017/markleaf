import AppKit
import WebKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

func waitUntil(_ condition: () -> Bool, seconds: TimeInterval = 12) -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    while !condition(), Date() < deadline {
        RunLoop.main.run(until: Date().addingTimeInterval(0.02))
    }
    return condition()
}

final class RuntimeMessages: NSObject, WKScriptMessageHandler {
    var messages: [[String: Any]] = []
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        if let body = message.body as? [String: Any] { messages.append(body) }
    }
    func received(_ type: String) -> Bool { messages.contains { $0["type"] as? String == type } }
}

_ = NSApplication.shared
let root = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let recorder = RuntimeMessages()
let configuration = WKWebViewConfiguration()
// Raw web dist intentionally has no native platform bridge. Test it with the
// very same shim used by the app, without repackaging/killing a running app.
if let shim = ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_SHIM"] {
    configuration.userContentController.addUserScript(WKUserScript(source: try! String(contentsOfFile: shim, encoding: .utf8), injectionTime: .atDocumentStart, forMainFrameOnly: true))
}
if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_RESUMABLE_TRACE"] == "1" {
    configuration.userContentController.addUserScript(WKUserScript(source: "window.__markleafTypographyTrace=true", injectionTime: .atDocumentStart, forMainFrameOnly: true))
}
if CommandLine.arguments.count > 2, ["typography", "table-width"].contains(CommandLine.arguments[2]) {
    // Standalone test binaries have no app bundle and WebKit can keep their
    // document hidden. A native timer pumps frames so hidden-page JS timer
    // throttling cannot stall verification. DOM geometry remains real WebKit.
    configuration.userContentController.addUserScript(WKUserScript(source: """
    {let next=0;const frames=new Map();window.requestAnimationFrame=f=>{frames.set(++next,f);return next};window.cancelAnimationFrame=id=>frames.delete(id);window.__pumpTypographyFrame=()=>{const batch=[...frames.values()];frames.clear();batch.forEach(f=>f(performance.now()))}}
    """, injectionTime: .atDocumentStart, forMainFrameOnly: true))
}
configuration.setURLSchemeHandler(EditorSchemeHandler(root: root), forURLScheme: "markleaf")
configuration.userContentController.add(recorder, name: "markleaf")
configuration.userContentController.addUserScript(WKUserScript(source: """
window.__runtimeErrors = [];
window.addEventListener('error', e => window.__runtimeErrors.push({message:e.message, file:e.filename, line:e.lineno, resource:e.target?.src}), true);
window.addEventListener('unhandledrejection', e => window.__runtimeErrors.push({rejection:String(e.reason),stack:e.reason?.stack}));
window.addEventListener('securitypolicyviolation', e => window.__runtimeErrors.push({directive:e.violatedDirective,blocked:e.blockedURI}));
""", injectionTime: .atDocumentStart, forMainFrameOnly: true))
let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 900, height: 650), configuration: configuration)
var typographyWindow: NSWindow?
var typographyFrameTimer: Timer?
if CommandLine.arguments.count > 2, ["typography", "table-width"].contains(CommandLine.arguments[2]) {
    // A detached WKWebView can suspend requestAnimationFrame indefinitely.
    typographyWindow = NSWindow(contentRect: NSRect(x: 40, y: 40, width: 1100, height: 800), styleMask: [.titled, .closable], backing: .buffered, defer: false)
    typographyWindow?.contentView = webView
    typographyWindow?.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
    var pumping = false
    typographyFrameTimer = Timer.scheduledTimer(withTimeInterval: 0.016, repeats: true) { _ in
        guard !pumping else { return }
        pumping = true
        webView.evaluateJavaScript("window.__pumpTypographyFrame?.()") { _, _ in pumping = false }
    }
}
webView.load(URLRequest(url: URL(string: "markleaf://editor/index.html")!))

func evaluate(_ script: String) -> Any? {
    var finished = false
    var value: Any?
    webView.evaluateJavaScript(script) { result, error in
        value = result
        if let error { print("JavaScript evaluation: \(error)") }
        finished = true
    }
    expect(waitUntil { finished }, "JavaScript evaluation must complete")
    return value
}

if !waitUntil({ recorder.received("ready") }) {
    print("Startup diagnostics: \(evaluate("JSON.stringify({errors:window.__runtimeErrors,bridge:typeof window.chrome?.webview,html:document.body?.innerHTML})") ?? "unavailable")")
    expect(false, "packaged editor must send a real ready message through WKWebView")
}
print("PASS: packaged editor ready")

func send(_ type: String, payload: [String: Any] = [:]) {
    let message: [String: Any] = ["protocolVersion": 1, "type": type, "documentId": "runtime-test", "revision": 0, "requestId": UUID().uuidString, "payload": payload]
    let data = try! JSONSerialization.data(withJSONObject: message)
    _ = evaluate("window.postMessage(\(String(data: data, encoding: .utf8)!), '*')")
}
send("loadDocument", payload: ["markdown": "# Runtime check\n\nOriginal paragraph.\n", "readOnly": false])
expect(waitUntil { recorder.received("documentLoaded") }, "host document must load through the native shim")
expect((evaluate("document.querySelector('#editor h1')?.textContent") as? String) == "Runtime check", "visual editor must render the loaded Markdown heading")
send("localizeFindBar", payload: ["find": "Find", "alertNote": "Note"])
send("command", payload: ["command": "setLanguage", "text": "en"])
send("command", payload: ["command": "findText", "text": "Original\t0\t0"])
expect(waitUntil { recorder.received("findResult") }, "native find must work without HTML search controls")
let found = recorder.messages.last { $0["type"] as? String == "findResult" }?["payload"] as? [String: Any]
expect(found?["total"] as? Int == 1, "native find must return the actual match count")
send("command", payload: ["command": "replaceAll", "text": "Original\tReplaced\t0\t0"])
expect(waitUntil { recorder.messages.contains { ($0["payload"] as? [String: Any])?["replaced"] as? Int == 1 } }, "native replacement must work without HTML search controls")
send("command", payload: ["command": "findClose"])
recorder.messages.removeAll()
send("command", payload: ["command": "pasteMarkdown", "text": "**Native edit**"])
expect(waitUntil { recorder.received("commandResult") }, "edit command must produce an actual result")
send("requestSnapshot")
expect(waitUntil { recorder.received("snapshot") }, "edited document must return a snapshot")
let snapshot = recorder.messages.last { $0["type"] as? String == "snapshot" }?["payload"] as? [String: Any]
expect((snapshot?["markdown"] as? String)?.contains("**Native edit**") == true, "snapshot must contain the real edit")
expect((snapshot?["markdown"] as? String)?.contains("Replaced") == true, "snapshot must include the native replacement")
print("PASS: document load, visual render, native find/replace, edit and snapshot round trip")

if CommandLine.arguments.count > 2, CommandLine.arguments[2] == "typography" {
    let sample = "这是一段用于检查中文与英文混排时的全局断行效果。MarkLeaf 正在尝试处理整段最优断行，而不是只把字符填满一行。观察这里的中英边界、标点、数字与英文单词能否保持自然间距。The quick brown fox jumps over the lazy dog."
    let chinese = String(repeating: "中文排版需要保持自然的字距与断行效果。", count: 6)
    let mixed = String(repeating: "中文**English**中文[AV ffi](https://example.com)中文12345。", count: 3)
    let markdown = sample + "\n\n" + chinese + "\n\n中文 **English** 中文 *AV ffi* 中文。\n\n" + mixed + "\n"
    send("loadDocument", payload: ["markdown": markdown, "readOnly": false])
    expect(waitUntil { (evaluate("document.querySelector('#editor p')?.textContent") as? String)?.contains("全局断行效果") == true }, "typography sample must load")
    expect((evaluate("document.querySelectorAll('.markleaf-typography-v2').length") as? Int) == 0, "projection must start disabled")
    _ = evaluate("""
    window.__typographyNonfinalLineIds=p=>{
      const ids=[...new Set([...p.querySelectorAll('[data-markleaf-typography-line-id]')].map(e=>e.dataset.markleafTypographyLineId))];
      const groups=new Map();for(const id of ids){const segment=id.split(':').slice(0,2).join(':');if(!groups.has(segment))groups.set(segment,[]);groups.get(segment).push(id)}
      return [...groups.values()].flatMap(lines=>lines.slice(0,-1));
    }
    """)
    if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_HOST_GEOMETRY"] == "1" {
        // Match the native host's real stylesheet and visual-variable bridge.
        // Fixed paragraph widths/font sizes in the matrix bypass both paths.
        let styles = ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_STYLES"].map { URL(fileURLWithPath: $0) } ?? root.deletingLastPathComponent().appendingPathComponent("Styles")
        let base = try! String(contentsOf: styles.appendingPathComponent("base.css"), encoding: .utf8)
        let sans = try! String(contentsOf: styles.appendingPathComponent("sans.css"), encoding: .utf8)
        send("applyStyles", payload: ["baseCss": base, "styles": [["id": "sans", "css": sans]], "activeStyle": "sans"])
        if let path = ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_SAMPLE"] {
            send("loadDocument", payload: ["markdown": try! String(contentsOfFile: path, encoding: .utf8), "readOnly": false])
        }
        let sourceBeforeHostLayout = evaluate("JSON.stringify(document.querySelector('.ProseMirror').editor.getJSON())") as? String
        func visualScale(_ scale: Double) {
            _ = evaluate("window.__markleafApplyVisualVariables({lineHeight:'1.6',fontSize:'\(16 * scale)px',maxWidth:'\(820 * scale)px',sourceFontSize:'\(14 * scale)px',sourceFontFamily:'monospace',cjkLanguage:'zh-Hans',visualCjkAutoSpacing:true,ignoreMaxWidth:false,usePointerAnchor:true,anchorX:300,anchorY:300})")
        }
        func state() -> String {
            evaluate("JSON.stringify({width:document.querySelector('.ProseMirror').getBoundingClientRect().width,scrollY,paragraphs:[...document.querySelectorAll('#editor p')].map(p=>[p.className,p.getBoundingClientRect().height,p.querySelectorAll('.markleaf-typography-break').length,getComputedStyle(p).fontSize])})") as? String ?? "unavailable"
        }
        let qualityAudit = ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_QUALITY_AUDIT"] == "1"
        if qualityAudit { typographyWindow?.setContentSize(NSSize(width: 1000, height: 742)) }
        visualScale(qualityAudit ? 1.38 : 1.75)
        send("command", payload: ["command": "setTypographyProjection", "text": "1"])
        expect(waitUntil { recorder.received("typographyProjectionChanged") }, "host geometry must produce diagnostics")
        if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_RESUMABLE_TRACE"] != "1" {
            expect(!recorder.received("typographyTrace"), "detailed typography trace must be disabled unless explicitly requested")
        }
        print("Host baseline: \(state())")
        if qualityAudit {
            print("QUALITY_AUDIT " + (evaluate("""
            JSON.stringify([...document.querySelectorAll('#editor p')].map((p,index)=>{
              const box=p.getBoundingClientRect(),range=document.createRange();range.selectNodeContents(p);
              const lines=[];for(const r of range.getClientRects()){
                if(r.width<=0||r.height<=0)continue;
                let line=lines.find(l=>Math.abs(l.y-(r.top+r.bottom)/2)<8);
                if(!line){line={y:(r.top+r.bottom)/2,left:r.left,right:r.right};lines.push(line)}
                else{line.left=Math.min(line.left,r.left);line.right=Math.max(line.right,r.right)}
              }
              return {index,lead:p.textContent.slice(0,24),class:p.className,width:box.width,font:getComputedStyle(p).fontSize,
                lines:lines.map(l=>({leftGap:l.left-box.left,rightGap:box.right-l.right}))};
            }))
            """) as? String ?? "unavailable"))
            print("QUALITY_DIAGNOSTICS \(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.suffix(1))")
            if let audit = ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_CANDIDATE_AUDIT_JS"] {
                _ = evaluate(try! String(contentsOfFile: audit, encoding: .utf8))
                _ = evaluate("window.__candidateAuditDone=false;window.__candidateAudit().then(r=>{window.__candidateAuditResult=r;window.__candidateAuditDone=true}).catch(e=>{window.__candidateAuditError=String(e)+' '+e.stack;window.__candidateAuditDone=true})")
                expect(waitUntil({ evaluate("window.__candidateAuditDone") as? Bool == true }, seconds: 180), "candidate audit must terminate")
                print("CANDIDATE_AUDIT " + (evaluate("JSON.stringify({result:window.__candidateAuditResult,error:window.__candidateAuditError})") as? String ?? "unavailable"))
                expect(evaluate("window.__candidateAuditError===undefined") as? Bool == true, "candidate audit must not throw")
            }
            if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_MIXED_FALLBACK_ACCEPTANCE"] == "1" {
                // Isolate the fallback renderer. Wider mixed-script capacity
                // now makes this text projectable at these widths in production.
                send("command", payload: ["command": "setTypographyProjection", "text": "0"])
                _ = evaluate("window.__mixedFallbackAutoSpacing=document.documentElement.classList.contains('markleaf-cjk-autospace');document.documentElement.classList.add('markleaf-cjk-autospace')")
                _ = evaluate("import('./kernel/editor-core.js').then(core=>{window.__mixedFallbackCore=core})")
                expect(waitUntil { evaluate("!!window.__mixedFallbackCore") as? Bool == true }, "fallback renderer module must load")
                for width in [934, 898, 820, 600] {
                    _ = evaluate("""
                    { const style=document.getElementById('mixed-fallback-test-style')??document.head.appendChild(document.createElement('style'));
                      style.id='mixed-fallback-test-style';
                      style.textContent='#editor .ProseMirror p {width:\(width)px !important;max-width:none !important;font-size:24.96px !important;}';
                      const editor=document.querySelector('.ProseMirror').editor,core=window.__mixedFallbackCore;
                      editor.commands.setContent({type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'中文English'.repeat(39)+'。'}]}]});
                      core.registerTypographySnapshots(editor,true,core.extractProjectionSnapshots(editor.view,core.getTypographyProjectionRevision(editor),0),[]); }
                    """)
                    expect(waitUntil { evaluate("document.querySelector('#editor p')?.classList.contains('markleaf-typography-quality-fallback')") as? Bool == true }, "isolated fallback renderer must remain active at width \(width)")
                    let before = evaluate("JSON.stringify(document.querySelector('.ProseMirror').editor.getJSON())") as? String
                    let boundarySpacing = evaluate("""
                    (()=>{const p=document.querySelector('#editor p'),host=document.createElement('div');
                      host.dataset.typographyMeasure='true';host.className='ProseMirror';
                      host.style.cssText='position:fixed;left:-100000px;top:0;visibility:hidden;width:max-content';
                      const clone=p.cloneNode(true);clone.style.setProperty('width','max-content','important');
                      clone.style.setProperty('white-space','pre','important');host.append(clone);document.querySelector('#editor').append(host);
                      try{const spaced=clone.getBoundingClientRect().width;
                        clone.style.setProperty('text-autospace','no-autospace','important');
                        clone.querySelectorAll('*').forEach(el=>{el.style.setProperty('text-autospace','no-autospace','important');el.style.setProperty('padding-left','0','important');el.style.setProperty('padding-inline-end','0','important')});
                        const gain=spaced-clone.getBoundingClientRect().width;
                        return gain/(77*parseFloat(getComputedStyle(p).fontSize)*0.125);
                      }finally{host.remove()}
                    })()
                    """) as? Double ?? 0
                    expect(boundarySpacing >= 0.95 && boundarySpacing <= 1.05, "mixed fallback must retain all in-line boundary spacing, got ratio \(boundarySpacing) at width \(width)")
                    let leftGap = evaluate("""
                    (()=>{const p=document.querySelector('#editor p'),left=p.getBoundingClientRect().left,lines=[];
                      const walker=document.createTreeWalker(p,NodeFilter.SHOW_TEXT);
                      while(walker.nextNode()){const n=walker.currentNode;for(let i=0;i<n.length;i++){
                        const r=document.createRange();r.setStart(n,i);r.setEnd(n,i+1);const b=r.getBoundingClientRect();
                        if(!b.width)continue;let line=lines.find(l=>Math.abs(l.top-b.top)<2);
                        if(!line){line={top:b.top,left:b.left};lines.push(line)}else line.left=Math.min(line.left,b.left);
                      }}return Math.max(...lines.map(l=>Math.abs(l.left-left)));
                    })()
                    """) as? Double ?? Double.infinity
                    expect(leftGap <= 0.5, "mixed fallback must trim boundary spacing at line starts, got \(leftGap) CSS px at width \(width)")
                    let beforeGeometry = state(), count = recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count
                    for _ in 0..<40 { RunLoop.main.run(until: Date().addingTimeInterval(0.05)) }
                    expect(state() == beforeGeometry, "mixed fallback geometry must remain stable")
                    expect(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count == count, "mixed fallback must not republish while idle")
                    expect(before == evaluate("JSON.stringify(document.querySelector('.ProseMirror').editor.getJSON())") as? String, "mixed fallback must preserve source JSON")
                    print("PASS: mixed fallback width \(width), line-start gap \(leftGap) CSS px, boundary spacing ratio \(boundarySpacing), source preserved and idle two seconds")
                }
                expect((evaluate("window.__runtimeErrors.length") as? Int) == 0, "mixed fallback must not raise runtime errors")
                _ = evaluate("document.documentElement.classList.toggle('markleaf-cjk-autospace',window.__mixedFallbackAutoSpacing)")
                send("command", payload: ["command": "setTypographyProjection", "text": "1"])
            }
            if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_RESUMABLE_TRACE"] == "1" {
                let traces = recorder.messages.filter { $0["type"] as? String == "typographyTrace" }
                let data = try! JSONSerialization.data(withJSONObject: traces, options: [.sortedKeys])
                print("QUALITY_TRACE \(String(data: data, encoding: .utf8)!)")
            }
            if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_CANDIDATE_FIX_ACCEPTANCE"] == "1" {
                // This scope repairs candidate acquisition only. Keep the full
                // four-paragraph acceptance below unchanged: punctuation and
                // English still require separately approved policy work.
                let states = evaluate("[...document.querySelectorAll('#editor p')].slice(0,5).map(p=>p.className)") as? [String] ?? []
                expect(states.count == 5 && states.prefix(3).allSatisfy { $0.contains("markleaf-typography-v2") }, "mixed and control paragraphs must adopt measured projection")
                expect(states.count == 5 && states.suffix(2).allSatisfy { $0.contains("markleaf-typography-quality-fallback") }, "infeasible punctuation/English must retain explicit fallback, not relaxed spacing")
                let residual = evaluate("""
                Math.max(0,...[...document.querySelectorAll('p.markleaf-typography-v2')].flatMap(p=>
                  window.__typographyNonfinalLineIds(p).map(id=>{const rects=[...p.querySelectorAll('[data-markleaf-typography-line-id]')].filter(e=>e.dataset.markleafTypographyLineId===id).map(e=>e.getBoundingClientRect());return Math.abs(Math.max(...rects.map(r=>r.right))-Math.min(...rects.map(r=>r.left))-p.getBoundingClientRect().width)})
                ))
                """) as? Double ?? Double.infinity
                expect(residual <= 0.5, "candidate repair must pass actual editor DOM residual checks")
                let before = state(), count = recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count
                for _ in 0..<40 {
                    RunLoop.main.run(until: Date().addingTimeInterval(0.05))
                    expect(state() == before, "candidate repair geometry must not oscillate after completion")
                }
                expect(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count == count, "candidate repair must not republish while idle")
                expect(sourceBeforeHostLayout != nil && sourceBeforeHostLayout == evaluate("JSON.stringify(document.querySelector('.ProseMirror').editor.getJSON())") as? String, "candidate repair must preserve source JSON")
                expect((evaluate("window.__runtimeErrors.length") as? Int) == 0, "candidate repair must not raise runtime errors")
                print("PASS: candidate coverage repair; mixed adopted, original spacing/fallback retained, residual \(residual) CSS px, source preserved, geometry/publications idle two seconds")
            }
            if ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_RESUMABLE_ACCEPTANCE"] == "1" {
                let classes = evaluate("[0,1,3,4].map(i=>document.querySelectorAll('#editor p')[i]?.classList.contains('markleaf-typography-v2')??false)") as? [Bool] ?? []
                let residual = evaluate("""
                Math.max(0,...[...document.querySelectorAll('p.markleaf-typography-v2')].flatMap(p=>{
                  return window.__typographyNonfinalLineIds(p).map(id=>{const r=[...p.querySelectorAll('[data-markleaf-typography-line-id]')].filter(e=>e.dataset.markleafTypographyLineId===id).map(e=>e.getBoundingClientRect());return Math.abs(Math.max(...r.map(r=>r.right))-Math.min(...r.map(r=>r.left))-p.getBoundingClientRect().width)})
                }))
                """) as? Double ?? Double.infinity
                expect(residual <= 0.5, "projected nonfinal lines must remain within 0.5 CSS px")
                let count = recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count
                let deadline = Date().addingTimeInterval(2)
                while Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.02)) }
                expect(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count == count, "completed geometry must remain idle for two seconds")
                print("Screenshot-width adopted states: \(classes); projected residual \(residual); two-second idle convergence PASS")
                expect(classes.count == 4 && classes.allSatisfy { $0 }, "all four screenshot paragraphs must complete measured projection; fallback is not visual acceptance")
                print("PASS: resumable screenshot-width projection and idle convergence; residual \(residual)")
            }
            exit(0)
        }
        for mode in ["width", "zoom"] {
            var samples: [String] = []
            var inputs: [Double] = []
            var beforeFinalInput = 0
            for step in 0..<8 {
                if step == 7 { beforeFinalInput = recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count }
                if mode == "width" {
                    typographyWindow?.setContentSize(NSSize(width: 1000 - step * 24, height: 800))
                } else {
                    visualScale([1.5, 1.25, 1.0, 1.25, 1.5, 1.75, 2.0, 1.75][step])
                }
                for _ in 0..<3 {
                    samples.append(state())
                    RunLoop.main.run(until: Date().addingTimeInterval(0.02))
                }
                inputs.append(evaluate(mode == "width" ? "document.querySelector('.ProseMirror').getBoundingClientRect().width" : "parseFloat(getComputedStyle(document.querySelector('#editor p')).fontSize)") as? Double ?? -1)
            }
            expect(Set(inputs).count > 1 && inputs.allSatisfy { $0 > 0 }, "host \(mode) must actually change DOM geometry")
            expect(waitUntil { recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count > beforeFinalInput }, "final host \(mode) environment must terminate")
            print("Host \(mode) active samples: \(samples)")
            var settled: [String] = []
            let settledCount = recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count
            let deadline = Date().addingTimeInterval(2)
            while Date() < deadline {
                settled.append(state())
                RunLoop.main.run(until: Date().addingTimeInterval(0.02))
            }
            expect(Set(settled).count == 1, "host \(mode) must not oscillate after completion")
            expect(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.count == settledCount, "host \(mode) must not keep publishing after completion")
            print("Host \(mode) settled samples: \(settled)")
            print("Host \(mode) diagnostics: \(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.suffix(12))")
        }
        let trace = recorder.messages.filter { $0["type"] as? String == "typographyTrace" }
        let traceData = try! JSONSerialization.data(withJSONObject: trace, options: [.sortedKeys])
        print("HOST_TRACE_JSON \(String(data: traceData, encoding: .utf8)!)")
        print("PASS: WK host width/zoom inputs changed and geometry/publications remained stable for two seconds; not APP compositor acceptance")
        exit(0)
    }
    let cases = [320, 600, 900].flatMap { width in
        [16, 20, 24].flatMap { size in
            ["system-ui", "serif"].flatMap { family in
                [false, true].map { dark in (width, size, family, dark) }
            }
        }
    }
    let resizeOnly = ProcessInfo.processInfo.environment["MARKLEAF_RUNTIME_RESIZE"] == "1"
    for (width, fontSize, family, dark) in cases where !resizeOnly || (width == 600 && fontSize == 16 && family == "system-ui" && !dark) {
        // Color-only changes intentionally reuse the completed geometry and
        // need not emit another layout diagnostic.
        if !dark { recorder.messages.removeAll { $0["type"] as? String == "typographyProjectionChanged" } }
        _ = evaluate("""
        (()=>{let style=document.querySelector('#typography-runtime-style');if(!style){style=document.createElement('style');style.id='typography-runtime-style';document.head.append(style)}
        style.textContent='#editor p{width:\(width)px;max-width:none;font-size:\(fontSize)px;font-family:\(family);padding:0;border:0}';
        document.querySelector('#editor .ProseMirror').style.width='\(width)px';
        document.documentElement.style.setProperty('--bg-primary','\(dark ? "#202124" : "#ffffff")');
        document.documentElement.style.setProperty('--text-primary','\(dark ? "#e8eaed" : "#24292f")');
        document.documentElement.style.colorScheme='\(dark ? "dark" : "light")';
        window.__typographyRanges=[];
        if(!window.__typographyRangeRead){window.__typographyRangeRead=Range.prototype.getClientRects;Range.prototype.getClientRects=function(){const rects=window.__typographyRangeRead.call(this);window.__typographyRanges.push({text:this.toString(),rects:[...rects].map(r=>({left:r.left,right:r.right,top:r.top,height:r.height}))});return rects}}
        })()
        """)
        send("command", payload: ["command": "setTypographyProjection", "text": "1"])
        if !waitUntil({ recorder.received("typographyProjectionChanged") }) {
            print("Typography missing diagnostics: \(evaluate("JSON.stringify({errors:window.__runtimeErrors,visibility:document.visibilityState,p:document.querySelector('#editor p')?.outerHTML})") ?? "no js")")
            print("Recent host messages: \(recorder.messages.suffix(8))")
            expect(false, "typography diagnostics must arrive")
        }
        let diagnostics = recorder.messages.last { $0["type"] as? String == "typographyProjectionChanged" }?["payload"] as? [String: Any]
        // A correct single frame is insufficient: projection must settle rather
        // than alternate between native wrapping and the measured plan.
        var motion: [String] = []
        for step in 0..<40 {
            if step == 5 { _ = evaluate("window.scrollTo(0,400)") }
            if step == 15 { _ = evaluate("window.scrollTo(0,0)") }
            motion.append(evaluate("JSON.stringify([...document.querySelectorAll('#editor p')].map(p=>[p.className,p.getBoundingClientRect().height,p.querySelectorAll('.markleaf-typography-break').length]))") as? String ?? "unavailable")
            RunLoop.main.run(until: Date().addingTimeInterval(0.02))
        }
        let states = Set(motion.dropFirst(10)).count
        if states != 1 { print("Unstable typography: \(motion)") }
        expect(states == 1, "paragraph geometry must settle without repeated line flicker")
        print("Typography width=\(width) size=\(fontSize) family=\(family) dark=\(dark) diagnostics: \(diagnostics ?? [:])")
        if fontSize == 16 { expect((diagnostics?["plannedParagraphs"] as? Int ?? 0) > 0, "baseline must retain genuinely projected paragraphs") }
        let geometry = evaluate("""
        JSON.stringify([...document.querySelectorAll('p.markleaf-typography-v2')].map(p => {
          return window.__typographyNonfinalLineIds(p).map(id => {const r=[...p.querySelectorAll('[data-markleaf-typography-line-id]')].filter(e=>e.dataset.markleafTypographyLineId===id).map(e=>e.getBoundingClientRect());return Math.abs(Math.max(...r.map(r=>r.right))-Math.min(...r.map(r=>r.left))-\(width));});
        }).flat())
        """) as? String ?? "[]"
        let errors = (try? JSONSerialization.jsonObject(with: Data(geometry.utf8))) as? [Double] ?? []
        if errors.isEmpty || errors.contains(where: { $0 > 0.5 }) { print("Typography range diagnostics: \(evaluate("JSON.stringify(window.__typographyRanges.slice(-20))") ?? "none")") }
        if (diagnostics?["lineBreaks"] as? Int ?? 0) > 0 { expect(!errors.isEmpty, "nonfinal projected lines must have real geometry") }
        expect(errors.allSatisfy { $0 <= 0.5 }, "real nonfinal line residuals must stay within 0.5 CSS px: \(geometry)")
        if (diagnostics?["plannedParagraphs"] as? Int ?? 0) == 0 {
            let reasons = diagnostics?["reasons"] as? [String: Any] ?? [:]
            expect(!reasons.isEmpty, "native fallback must have an explicit quality or budget reason")
            expect((evaluate("document.querySelectorAll('.markleaf-typography-v2').length") as? Int) == 0, "fallback must not retain stale projections")
        }
        if width == 900, fontSize == 16, family == "system-ui", !dark, CommandLine.arguments.count > 3, !CommandLine.arguments[3].isEmpty {
            var finished = false
            webView.takeSnapshot(with: nil) { image, error in
                expect(error == nil, "WKWebView snapshot must succeed")
                let png = image?.tiffRepresentation.flatMap { NSBitmapImageRep(data: $0)?.representation(using: .png, properties: [:]) }
                expect(png != nil, "WKWebView snapshot must contain image data")
                try! png!.write(to: URL(fileURLWithPath: CommandLine.arguments[3])); finished = true
            }
            expect(waitUntil { finished }, "WKWebView snapshot must complete")
        }
        recorder.messages.removeAll { $0["type"] as? String == "snapshot" }
        send("requestSnapshot")
        expect(waitUntil { recorder.received("snapshot") }, "projected document must return snapshot")
        let snapshot = recorder.messages.last { $0["type"] as? String == "snapshot" }?["payload"] as? [String: Any]
        expect((snapshot?["markdown"] as? String)?.contains(sample) == true, "projection must preserve source text")
    }
    if resizeOnly {
        var masks: [String] = []
        recorder.messages.removeAll { $0["type"] as? String == "typographyProjectionChanged" }
        for width in [592, 584, 576, 568, 560, 552, 544, 552, 560, 568, 584, 620] {
            _ = evaluate("document.querySelector('#editor .ProseMirror').style.width='\(width)px';document.querySelector('#typography-runtime-style').textContent='#editor p{width:\(width)px;max-width:none;font-size:16px;font-family:system-ui;padding:0;border:0}';window.dispatchEvent(new Event('resize'))")
            for _ in 0..<2 {
                RunLoop.main.run(until: Date().addingTimeInterval(0.02))
                masks.append(evaluate("[...document.querySelectorAll('#editor p')].map(p=>p.classList.contains('markleaf-typography-v2')?'P':'N').join('')") as? String ?? "unavailable")
            }
        }
        let switches = zip(masks, masks.dropFirst()).filter { $0 != $1 }.count
        print("Live resize projection masks: \(masks), switches=\(switches)")
        expect(masks.allSatisfy { $0 == "NNNN" }, "every paragraph must stay native throughout continuous resizing")
        expect(!recorder.received("typographyProjectionChanged"), "resize burst must not publish intermediate measured layouts")
        if !waitUntil({ recorder.received("typographyProjectionChanged") && (evaluate("document.querySelectorAll('.markleaf-typography-v2').length") as? Int ?? 0) > 0 }) {
            print("Resize final diagnostics: \(recorder.messages.last { $0["type"] as? String == "typographyProjectionChanged" } ?? [:])")
            expect(false, "projection must resume after resizing settles")
        }
        let residual = evaluate("""
        Math.max(0,...[...document.querySelectorAll('p.markleaf-typography-v2')].flatMap(p=>{
          const ids=[...new Set([...p.querySelectorAll('[data-markleaf-typography-line-id]')].map(e=>e.dataset.markleafTypographyLineId))];
          return ids.slice(0,-1).map(id=>{const r=[...p.querySelectorAll('[data-markleaf-typography-line-id]')].filter(e=>e.dataset.markleafTypographyLineId===id).map(e=>e.getBoundingClientRect());return Math.abs(Math.max(...r.map(r=>r.right))-Math.min(...r.map(r=>r.left))-620)})
        }))
        """) as? Double ?? .infinity
        expect(residual <= 0.5, "resize must publish the final width, not merely retain an old plan")
        print("Final-width residual \(residual) CSS px; recent diagnostics: \(recorder.messages.filter { $0["type"] as? String == "typographyProjectionChanged" }.suffix(4))")
        expect(switches == 0, "continuous resize must not repeatedly alternate native and projected paragraphs")
        print("PASS: continuous resize stayed native, then restored measured projection at the final width")
    }
    send("command", payload: ["command": "setTypographyProjection", "text": "0"])
    expect(waitUntil { (evaluate("document.querySelectorAll('.markleaf-typography-v2').length") as? Int) == 0 }, "disabling projection must remove geometry")
    expect((evaluate("window.__runtimeErrors.length") as? Int) == 0, "typography must not cause unhandled runtime errors")
    print(resizeOnly ? "PASS: real WKWebView live-resize regression" : "PASS: real WKWebView typography residuals, default-off, toggle and source preservation: 36 cases (320/600/900 CSS px, 16/20/24 px text, system-ui/serif, light/dark)")
}


if CommandLine.arguments.count > 2, CommandLine.arguments[2] == "table-width" {
    let baseCss = try! String(contentsOf: root.deletingLastPathComponent().appendingPathComponent("Styles/base.css"), encoding: .utf8)
    _ = evaluate("{const style=document.createElement('style');style.textContent=\(String(data: try! JSONSerialization.data(withJSONObject: baseCss, options: [.fragmentsAllowed]), encoding: .utf8)!);document.head.append(style);void 0}")
    let tableMarkdown = "<!-- markleaf:table-layout {\"version\":1,\"shape\":[2,2,2],\"width\":400,\"columns\":[180,220],\"rows\":{\"2\":[210,190]}} -->\n| A | B |\n| --- | --- |\n| first | second |\n| third | fourth |"
    send("loadDocument", payload: ["markdown": tableMarkdown, "readOnly": false])
    expect(waitUntil { (evaluate("document.querySelectorAll('table.markleaf-table-manual').length") as? Int) == 1 }, "manual table must render")
    _ = evaluate("""
    window.__tableWidths=()=>[...document.querySelector('table.markleaf-table-manual').rows].map(r=>[...r.cells].map(c=>c.getBoundingClientRect().width));
    window.__tableLayout=()=>JSON.parse(document.querySelector('table.markleaf-table-manual').dataset.markleafTableLayout);
    window.__tableDrag=(row,dx,cancel=false)=>{
      const c=document.querySelector('table.markleaf-table-manual').rows[row].cells[0],r=c.getBoundingClientRect();
      c.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,button:0,pointerId:7,clientX:r.right,clientY:r.top+5}));
      if(cancel)document.dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,key:'Escape'}));
      document.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,cancelable:true,button:0,pointerId:7,clientX:r.right+dx,clientY:r.top+5}));
    };void 0;
    """)
    print("Table initial geometry: \(evaluate("JSON.stringify(window.__tableWidths())") ?? "unavailable")")
    let alignment = evaluate("Math.abs(window.__tableWidths()[0][0]-180)<1 && Math.abs(window.__tableWidths()[2][0]-210)<1") as? Bool
    expect(alignment == true, "WebKit must honor independent row widths")
    _ = evaluate("window.__tableDrag(1,20)")
    expect(waitUntil { (evaluate("window.__tableLayout().columns[0]") as? Double ?? 0) > 199 }, "whole-column drag must commit")
    expect((evaluate("Math.abs(window.__tableLayout().rows['2'][0]-230)<1") as? Bool) == true, "whole drag moves existing local boundaries")
    send("command", payload: ["command": "undo"])
    expect(waitUntil { (evaluate("window.__tableLayout().columns[0]") as? Int) == 180 }, "one undo restores drag")
    _ = evaluate("""
    window.__outerStart=()=>{const c=document.querySelector('table.markleaf-table-manual').rows[1].cells[1],r=c.getBoundingClientRect();window.__outerX=r.right;c.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,button:0,pointerId:7,clientX:r.right,clientY:r.top+5}));};
    window.__outerMove=dx=>document.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,cancelable:true,pointerId:7,clientX:window.__outerX+dx}));
    window.__outerEnd=dx=>document.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,cancelable:true,button:0,pointerId:7,clientX:window.__outerX+dx}));
    window.__outerStart();window.__outerMove(20);void 0
    """)
    expect(waitUntil { (evaluate("window.__tableLayout().width") as? Int) == 420 }, "outer edge must preview the total width")
    _ = evaluate("window.__outerMove(30);void 0")
    expect(waitUntil { (evaluate("window.__tableLayout().width") as? Int) == 430 }, "outer preview must use the initial coordinate scale")
    _ = evaluate("window.__outerEnd(30);void 0")
    expect((evaluate("window.__tableLayout().columns[0]===180 && window.__tableLayout().rows['2'][1]===220") as? Bool) == true, "outer resize preserves earlier columns and updates local final columns")
    send("command", payload: ["command": "undo"])
    expect(waitUntil { (evaluate("window.__tableLayout().width") as? Int) == 400 }, "outer edge must undo as one gesture")
    _ = evaluate("{const c=document.querySelector('table.markleaf-table-manual').rows[0].cells[0],range=document.createRange();range.selectNodeContents(c);const selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);document.dispatchEvent(new Event('selectionchange'));void 0}")
    _ = evaluate("window.__tableDrag(0,25)")
    expect(waitUntil { (evaluate("window.__tableLayout().rows['0']?.[0]") as? Double ?? 0) > 204 }, "selected-cell drag changes only its row")
    expect((evaluate("window.__tableLayout().columns[0]") as? Int) == 180, "selected-cell resize keeps default column widths")
    _ = evaluate("window.__tableDrag(0,20,true)")
    expect((evaluate("window.__tableLayout().rows['0'][0]") as? Double ?? 0) < 206, "Escape cancels resize")
    send("command", payload: ["command": "fitTableContents"])
    expect(waitUntil { (evaluate("Object.keys(window.__tableLayout().rows).length") as? Int) == 0 }, "content fit clears local overrides")
    send("command", payload: ["command": "distributeTableColumns"])
    expect((evaluate("Math.abs(window.__tableWidths()[0][0]-window.__tableWidths()[0][1])<1") as? Bool) == true, "distributed columns have equal rendered width")
    recorder.messages.removeAll { $0["type"] as? String == "snapshot" }
    send("requestSnapshot")
    expect(waitUntil { recorder.received("snapshot") }, "layout snapshot must arrive")
    let saved = recorder.messages.last { $0["type"] as? String == "snapshot" }?["payload"] as? [String: Any]
    let markdown = saved?["markdown"] as? String ?? ""
    expect(markdown.contains("markleaf:table-layout"), "layout must persist to Markdown")
    send("loadDocument", payload: ["markdown": markdown, "readOnly": false])
    expect(waitUntil { (evaluate("document.querySelectorAll('table.markleaf-table-manual').length") as? Int) == 1 }, "saved table must reopen with layout")
    _ = evaluate("document.querySelector('#editor .ProseMirror').style.width='90px'")
    expect((evaluate("document.querySelector('.markleaf-table-wrapper').scrollWidth >= document.querySelector('.markleaf-table-wrapper').clientWidth") as? Bool) == true, "narrow table remains contained")
    send("loadDocument", payload: ["markdown": tableMarkdown, "readOnly": false])
    expect(waitUntil { (evaluate("window.__tableLayout().columns[0]") as? Int) == 180 }, "restore wide table for scale verification")
    for scale in [0.8, 1.25, 2.0] {
        _ = evaluate("document.querySelector('#editor .ProseMirror').style.width='600px';document.querySelector('#editor .ProseMirror').style.zoom='\(scale)';void 0")
        let before = evaluate("window.__tableLayout().columns[0]") as? Double ?? 0
        _ = evaluate("window.__tableDrag(1,\(10 * scale));void 0")
        print("Scale \(scale), before \(before), after \(evaluate("JSON.stringify(window.__tableLayout())") ?? "nil"), geometry \(evaluate("JSON.stringify(window.__tableWidths())") ?? "nil")")
        expect((evaluate("Math.abs(window.__tableLayout().columns[0]-\(before + 10))<1") as? Bool) == true, "scaled pointer coordinates must preserve logical widths")
    }
    _ = evaluate("document.querySelector('#editor .ProseMirror').style.zoom='1';void 0")
    for scale in [0.8, 1.25, 2.0] {
        _ = evaluate("document.querySelector('#editor .ProseMirror').style.width='1000px';window.__markleafApplyVisualVariables({visualScale:\(scale),fontSize:'\(16 * scale)px',maxWidth:'\(820 * scale)px',lineHeight:'1.6',sourceFontSize:'14px',sourceFontFamily:'monospace',cjkLanguage:'zh-Hans',visualCjkAutoSpacing:false,ignoreMaxWidth:true});void 0")
        expect((evaluate("Math.abs(document.querySelector('table').getBoundingClientRect().width/window.__tableLayout().width-\(scale))<.02") as? Bool) == true, "native font-based zoom must scale manual table width")
        let before = evaluate("window.__tableLayout().columns[0]") as? Double ?? 0
        _ = evaluate("window.__tableDrag(1,\(10 * scale));void 0")
        expect((evaluate("Math.abs(window.__tableLayout().columns[0]-\(before + 10))<1") as? Bool) == true, "native zoom drag must store logical widths")
    }
    _ = evaluate("document.documentElement.style.setProperty('--ml-table-scale','1');void 0")

    send("loadDocument", payload: ["markdown": tableMarkdown, "readOnly": false])
    _ = evaluate("document.querySelector('#editor .ProseMirror').style.width='300px';window.__outerStart();window.__outerEnd(-30);void 0")
    expect((evaluate("Math.abs(window.__tableLayout().width-270)<1 && Math.abs(document.querySelector('table').getBoundingClientRect().width-270)<1") as? Bool) == true, "compressed tables must shrink from their actual visible width")
    send("loadDocument", payload: ["markdown": tableMarkdown.replacingOccurrences(of: "[180,220]", with: "[40,360]"), "readOnly": false])
    _ = evaluate("document.querySelector('#editor .ProseMirror').style.width='200px';void 0")
    expect((evaluate("Math.min(...window.__tableWidths().flat())>=39.5 && document.querySelector('.markleaf-table-wrapper').scrollWidth>document.querySelector('.markleaf-table-wrapper').clientWidth") as? Bool) == true, "asymmetric cells must remain at least 40px and scroll in narrow view")
    send("command", payload: ["command": "resetTableWidths"])
    expect(waitUntil { (evaluate("document.querySelectorAll('table.markleaf-table-manual').length") as? Int) == 0 }, "reset restores automatic table")
    expect((evaluate("window.__runtimeErrors.length") as? Int) == 0, "table resizing must not produce frontend errors")
    let fitMarkdown = "<!-- markleaf:table-layout {\"version\":1,\"shape\":[3,3,3],\"width\":600,\"columns\":[200,200,200],\"rows\":{\"2\":[210,190,200]}} -->\n| A | B | C |\n| --- | --- | --- |\n| A deliberately long phrase requiring more width | x | third |\n| q | notice | third |"
    send("loadDocument", payload: ["markdown": fitMarkdown, "readOnly": false])
    _ = evaluate("document.querySelector('.ProseMirror').style.width='1000px';void 0")
    _ = evaluate("""
    window.__doubleBoundary=(row,collapse=false)=>{
      const c=document.querySelector('table').rows[row].cells[0],r=c.getBoundingClientRect();
      for(let count=1;count<=2;count++){
        c.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,button:0,pointerId:9,clientX:r.right,clientY:r.top+5}));
        c.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,cancelable:true,button:0,detail:count,clientX:r.right,clientY:r.top+5}));
        document.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,cancelable:true,button:0,pointerId:9,clientX:r.right+(count===1?1:0),clientY:r.top+5}));
        c.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,button:0,detail:count,clientX:r.right,clientY:r.top+5}));
        c.dispatchEvent(new MouseEvent('click',{bubbles:true,button:0,detail:count,clientX:r.right,clientY:r.top+5}));
        if(count===1&&collapse){window.getSelection().collapse(c,0);document.querySelector('.ProseMirror').editor.commands.setTextSelection(3);}
      }
      document.querySelector('.ProseMirror').dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true,button:0,detail:2,clientX:r.right,clientY:r.top+5}));
    };window.__doubleBoundary(1);void 0
    """)
    expect((evaluate("window.__tableLayout().columns[0]>201 && window.__tableLayout().columns[2]===200 && window.__tableLayout().width===600") as? Bool) == true, "double-click must fit only the adjacent columns to their content")
    send("command", payload: ["command": "undo"])
    expect(waitUntil { (evaluate("window.__tableLayout().columns[0]") as? Int) == 200 }, "double-click must undo in one step despite click jitter")
    _ = evaluate("{const c=document.querySelector('table').rows[2].cells[0],range=document.createRange();range.selectNodeContents(c);window.getSelection().removeAllRanges();window.getSelection().addRange(range);document.dispatchEvent(new Event('selectionchange'));window.__doubleBoundary(2,true);void 0}")
    expect((evaluate("window.__tableLayout().columns[0]===200 && window.__tableLayout().rows['2'][0]<209 && window.__tableLayout().rows['2'][2]===200") as? Bool) == true, "selected row fitting must preserve the first click intent and use its own content")
    send("command", payload: ["command": "undo"])
    expect((evaluate("window.__tableLayout().rows['2'][0]===210") as? Bool) == true, "local double-click fitting must undo in one step")
    let offsetMarkdown = "<!-- markleaf:table-layout {\"version\":1,\"shape\":[3,3,3,3],\"width\":680,\"columns\":[160,320,200],\"rows\":{\"2\":[80,400,200]}} -->\n| 阶段 | 任务 | 验收 |\n| --- | --- | --- |\n| 需求确认 | 明确整列与局部单元格的拖动规则 | 已确认 |\n| 设计审查 | 这一行的边界与其他行错开 | 观察独立边界 |\n| 实现验证 | **完整选中此格全部内容**，然后拖动右侧竖线 | 检查其他行 |"
    send("loadDocument", payload: ["markdown": offsetMarkdown, "readOnly": false])
    _ = evaluate("document.querySelector('.ProseMirror').style.width='650px';window.__markleafApplyVisualVariables({visualScale:1.5,fontSize:'24px',maxWidth:'1230px',lineHeight:'1.6',sourceFontSize:'14px',sourceFontFamily:'monospace',cjkLanguage:'zh-Hans',visualCjkAutoSpacing:false,ignoreMaxWidth:true});window.getSelection().removeAllRanges();document.querySelector('.ProseMirror').editor.commands.setTextSelection(3);window.__doubleBoundary(1);void 0")
    expect((evaluate("(()=>{const t=document.querySelector('table'),l=window.__tableLayout();return !l.rows['2'] && t.offsetWidth<=651 && t.rows[2].cells[0].offsetWidth>60 && l.columns[2]===200 && l.width===680})()") as? Bool) == true, "auto-fit must not collapse an offset row or inflate the table at 150% zoom")
    send("command", payload: ["command": "undo"])
    expect((evaluate("window.__tableLayout().rows['2'][0]===80") as? Bool) == true, "offset row auto-fit must undo in one step")
    _ = evaluate("window.__markleafApplyVisualVariables({visualScale:1,fontSize:'16px',maxWidth:'820px',lineHeight:'1.6',sourceFontSize:'14px',sourceFontFamily:'monospace',cjkLanguage:'zh-Hans',visualCjkAutoSpacing:false,ignoreMaxWidth:true});void 0")
    print("PASS: real WebKit adjacent-column double-click fitting, local selection, jitter and undo")
    send("loadDocument", payload: ["markdown": tableMarkdown, "readOnly": false])
    _ = evaluate("""
    window.__exportHtml=null;import('./kernel/export-html.js').then(m=>m.generateExportHtml({rawBodyHtml:document.querySelector('table').outerHTML,resolved:{rootClass:'markleaf-document',css:''},format:'pdf',baseCss:document.querySelector('style').textContent,editorLoc:{}})).then(html=>window.__exportHtml=html);void 0
    """)
    expect(waitUntil { (evaluate("typeof window.__exportHtml==='string'") as? Bool) == true }, "export HTML must be generated")
    typographyWindow?.setContentSize(NSSize(width: 260, height: 700))
    _ = evaluate("const exported=window.__exportHtml;document.open();document.write(exported);document.close();void 0")
    expect((evaluate("(()=>{const t=document.querySelector('table'),r=t.getBoundingClientRect(),body=document.querySelector('.markleaf-document').getBoundingClientRect();return r.right<=body.right+1 && t.style.minWidth==='0px' && Math.abs(t.rows[0].cells[0].getBoundingClientRect().width/r.width-.45)<.02 && Math.abs(t.rows[2].cells[0].getBoundingClientRect().width/r.width-.525)<.02})()") as? Bool) == true, "PDF layout must fit a narrow page and preserve row proportions")
    var pdfFinished = false
    var pdfBytes = 0
    webView.createPDF(configuration: WKPDFConfiguration()) { result in
        if case .success(let data) = result { pdfBytes = data.count }
        pdfFinished = true
    }
    expect(waitUntil { pdfFinished }, "PDF generation must finish")
    expect(pdfBytes > 500, "manual-width table must produce a real PDF")
    print("PASS: real WKWebView table width rendering, whole/local drag, cancellation, undo, content fit and save/reopen")
}
