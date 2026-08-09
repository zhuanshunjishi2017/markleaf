# MarkLeaf macOS Startup Action Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all three startup actions deterministic, path-safe, testable, and isolated from the user's real settings during automation.

**Architecture:** A pure `StartupActionResolver` converts preferences and path availability into a value-type `StartupPlan`. `AppWindowManager` executes that plan once, while `EditorSession` routes every initial load through the same entry point. `SettingsService` accepts an isolated Application Support root through `MARKLEAF_APP_SUPPORT_DIR`.

**Tech Stack:** Swift 5.9, AppKit, WebKit, Foundation, Swift Package Manager, XCTest.

## Global Constraints

- Minimum platform remains macOS 13.
- Explicit Finder or CLI file paths take precedence over startup preferences.
- Invalid remembered paths never trigger a blocking startup alert and are not deleted.
- `newDocument` loads empty Markdown.
- Production settings remain under `~/Library/Application Support/MarkLeaf` when `MARKLEAF_APP_SUPPORT_DIR` is absent.

---

### Task 1: Pure startup decision model

**Files:**
- Modify: `macos/Package.swift`
- Create: `macos/Tests/MarkLeafTests/StartupActionResolverTests.swift`
- Create: `macos/Sources/MarkLeaf/Services/StartupActionResolver.swift`

**Interfaces:**
- Consumes: `AppSettings.StartupAction`, remembered paths, an optional explicit file, and two path predicates.
- Produces: `StartupPlan` and `StartupActionResolver.resolve(action:lastFolder:lastFile:explicitFile:isDirectory:isFile:)`.

- [ ] **Step 1: Add the XCTest target and failing tests**

Add this test target to `Package.swift`:

```swift
.testTarget(name: "MarkLeafTests", dependencies: ["MarkLeaf"], path: "Tests/MarkLeafTests")
```

Create table-driven tests with literal expectations for: explicit-file precedence across all three actions; blank new document; valid/invalid and nil last workspace; and all four workspace/file validity combinations including both remembered values being nil. Use this helper shape:

```swift
private func resolve(
    _ action: AppSettings.StartupAction,
    folder: String? = nil,
    file: String? = nil,
    explicit: String? = nil,
    validFolders: Set<String> = [],
    validFiles: Set<String> = []
) -> StartupPlan {
    StartupActionResolver.resolve(
        action: action, lastFolder: folder, lastFile: file, explicitFile: explicit,
        isDirectory: validFolders.contains, isFile: validFiles.contains)
}
```

Assert these exact results:

```swift
XCTAssertEqual(resolve(.newDocument), .init(operation: .newDocument, notice: nil))
XCTAssertEqual(resolve(.openLastWorkspace, folder: "/w", validFolders: ["/w"]),
               .init(operation: .openWorkspace("/w"), notice: nil))
XCTAssertEqual(resolve(.openLastWorkspace, folder: "/missing"),
               .init(operation: .newDocument, notice: .missingWorkspace))
XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/w", file: "/w/a.md",
                       validFolders: ["/w"], validFiles: ["/w/a.md"]),
               .init(operation: .openWorkspaceAndFile(workspace: "/w", file: "/w/a.md"), notice: nil))
XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/w", file: "/missing", validFolders: ["/w"]),
               .init(operation: .openWorkspace("/w"), notice: .missingFile))
XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/missing", file: "/a.md", validFiles: ["/a.md"]),
               .init(operation: .openFile("/a.md"), notice: .missingWorkspace))
XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/missing", file: "/missing.md"),
               .init(operation: .newDocument, notice: .missingWorkspaceAndFile))
XCTAssertEqual(resolve(.newDocument, explicit: "/requested.md"),
               .init(operation: .openExplicitFile("/requested.md"), notice: nil))
```

- [ ] **Step 2: Run RED**

Run: `swift test --package-path macos --filter StartupActionResolverTests`

Expected: compilation fails because the resolver types do not exist.

- [ ] **Step 3: Implement the minimal resolver**

```swift
import Foundation

struct StartupPlan: Equatable {
    enum Operation: Equatable {
        case newDocument
        case openExplicitFile(String)
        case openWorkspace(String)
        case openFile(String)
        case openWorkspaceAndFile(workspace: String, file: String)
    }
    enum Notice: Equatable { case missingWorkspace, missingFile, missingWorkspaceAndFile }
    let operation: Operation
    let notice: Notice?
}

enum StartupActionResolver {
    static func resolve(
        action: AppSettings.StartupAction, lastFolder: String?, lastFile: String?,
        explicitFile: String?, isDirectory: (String) -> Bool, isFile: (String) -> Bool
    ) -> StartupPlan {
        if let explicitFile { return .init(operation: .openExplicitFile(explicitFile), notice: nil) }
        switch action {
        case .newDocument:
            return .init(operation: .newDocument, notice: nil)
        case .openLastWorkspace:
            guard let lastFolder, isDirectory(lastFolder) else {
                return .init(operation: .newDocument, notice: .missingWorkspace)
            }
            return .init(operation: .openWorkspace(lastFolder), notice: nil)
        case .openLastWorkspaceAndFiles:
            let folder = lastFolder.flatMap { isDirectory($0) ? $0 : nil }
            let file = lastFile.flatMap { isFile($0) ? $0 : nil }
            switch (folder, file) {
            case let (.some(folder), .some(file)):
                return .init(operation: .openWorkspaceAndFile(workspace: folder, file: file), notice: nil)
            case let (.some(folder), .none):
                return .init(operation: .openWorkspace(folder), notice: .missingFile)
            case let (.none, .some(file)):
                return .init(operation: .openFile(file), notice: .missingWorkspace)
            case (.none, .none):
                return .init(operation: .newDocument, notice: .missingWorkspaceAndFile)
            }
        }
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run: `swift test --package-path macos --filter StartupActionResolverTests && swift test --package-path macos`

Expected: focused and full suites pass with zero failures.

- [ ] **Step 5: Commit**

```bash
git add macos/Package.swift macos/Tests/MarkLeafTests/StartupActionResolverTests.swift macos/Sources/MarkLeaf/Services/StartupActionResolver.swift
git commit -m "test(macos): define deterministic startup plans"
```

---

### Task 2: Execute startup plans once and remove prototype content

**Files:**
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Test: `macos/Tests/MarkLeafTests/StartupActionResolverTests.swift`

**Interfaces:**
- Consumes: `StartupPlan` from Task 1.
- Produces: `AppWindowManager.performStartupAction(for:explicitFile:) -> Bool`.

- [ ] **Step 1: Establish the focused regression baseline**

Run: `swift test --package-path macos --filter StartupActionResolverTests`

Expected: all resolver tests from Task 1 pass before integration changes.

- [ ] **Step 2: Replace `performStartupAction()` with a thin executor**

The method returns `false` after the initial request has already been consumed. On the first call, resolve using `FileManager.fileExists(atPath:isDirectory:)`, log the chosen plan, and execute:

```swift
case .newDocument: session.newDocument()
case .openExplicitFile(let path), .openFile(let path): session.openDocument(at: URL(fileURLWithPath: path))
case .openWorkspace(let path): session.loadWorkspace(path); session.newDocument()
case .openWorkspaceAndFile(let workspace, let file):
    session.loadWorkspace(workspace)
    session.openDocument(at: URL(fileURLWithPath: file))
```

Apply one of these localized non-blocking status keys after execution when a notice exists:

```text
上次工作区不可用，已打开可用内容
上次文件不可用，已打开可用内容
上次工作区和文件均不可用，已新建文档
```

- [ ] **Step 3: Route initial loading through the executor**

Replace `runInitialLoad()` branching with:

```swift
let explicitPath = pendingInitialOpenPath ?? Self.argumentValue("--open")
if useStartupAction || explicitPath != nil {
    if !AppWindowManager.shared.performStartupAction(for: self, explicitFile: explicitPath) {
        newDocument()
    }
    return
}
newDocument()
```

Remove `sampleMarkdown`; the current repository has no named diagnostic that references it.

- [ ] **Step 4: Add English and Traditional Chinese translations**

Translate the three keys in both existing dictionaries, then build the module so duplicate dictionary keys or missing syntax fail immediately.

- [ ] **Step 5: Verify and commit**

Run: `swift test --package-path macos && swift build --package-path macos`

Expected: all tests pass; build exits 0 with no new warnings.

```bash
git add macos/Sources/MarkLeaf/App/AppWindowManager.swift macos/Sources/MarkLeaf/Services/EditorSession.swift macos/Sources/MarkLeaf/Services/L10n.swift macos/Tests/MarkLeafTests/StartupActionResolverTests.swift
git commit -m "fix(macos): make startup recovery path-safe"
```

---

### Task 3: Isolate settings and make first save reliable

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/AppSettings.swift`
- Create: `macos/Tests/MarkLeafTests/SettingsServiceIsolationTests.swift`

**Interfaces:**
- Consumes: optional `MARKLEAF_APP_SUPPORT_DIR` in an environment dictionary.
- Produces: internal `SettingsService.init(environment:)` and reliable first save.

- [ ] **Step 1: Write a failing real-filesystem test**

Create a unique temporary root; initialize `SettingsService(environment: ["MARKLEAF_APP_SUPPORT_DIR": root.path])`; save `.openLastWorkspace`; initialize a second service with the same environment; load it; assert the value and `root/MarkLeaf/settings.json` exist. Use `defer` for cleanup.

- [ ] **Step 2: Run RED**

Run: `swift test --package-path macos --filter SettingsServiceIsolationTests`

Expected: compilation fails because the initializer is absent, or the first-save file assertion fails.

- [ ] **Step 3: Implement environment selection and first-save fallback**

Store an `applicationSupportRoot`. Use the non-empty environment override when present; otherwise use Foundation's user Application Support directory. Build `settingsURL` under `<root>/MarkLeaf/settings.json`. Replace the save operation with:

```swift
if FileManager.default.fileExists(atPath: settingsURL.path) {
    _ = try FileManager.default.replaceItemAt(settingsURL, withItemAt: temp)
} else {
    try FileManager.default.moveItem(at: temp, to: settingsURL)
}
```

- [ ] **Step 4: Verify GREEN and commit**

Run: `swift test --package-path macos --filter SettingsServiceIsolationTests && swift test --package-path macos && swift build --package-path macos`

Expected: persistence test, full suite, and build all pass.

```bash
git add macos/Sources/MarkLeaf/Services/AppSettings.swift macos/Tests/MarkLeafTests/SettingsServiceIsolationTests.swift
git commit -m "fix(macos): isolate automation settings storage"
```

---

### Task 4: End-to-end isolated startup verification

**Files:**
- Verify: `macos/dist/MarkLeaf.app`
- Verify: `/Applications/MarkLeaf.app`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: fresh evidence for all startup behaviors without changing real settings.

- [ ] **Step 1: Record checksum and modification time for the real settings JSON**
- [ ] **Step 2: Run `macos/script/build_and_run.sh --verify` and require successful resource build, signing, launch, and process check**
- [ ] **Step 3: With unique isolated roots, exercise all three actions plus valid/invalid workspace/file combinations and explicit `--open` precedence; inspect window title, workspace, status text, and `/tmp/markleaf-app.log`**
- [ ] **Step 4: Recheck the real settings checksum and modification time; require exact equality with Step 1**
- [ ] **Step 5: Install the verified bundle and confirm normal launch no longer reports `/tmp/markleaf_pdf_test.md`**
