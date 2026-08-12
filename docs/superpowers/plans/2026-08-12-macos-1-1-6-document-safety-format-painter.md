# macOS 1.1.6 Document Safety, Multi-Window, and Format Painter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship MarkLeaf for macOS 1.1.6 with safe document close/replace/quit behavior, configurable external-file window routing, a one-shot format painter, and four-language Markdown changelogs.

**Architecture:** Keep document safety decisions in one testable Swift coordinator and let `EditorSession` provide the native AppKit sheets and save operations. Route external files through `AppWindowManager`, deduplicate standardized URLs before opening, and validate replacement content before disposing the current document. Keep format-painter state entirely in EditorWeb, with AppKit limited to command routing and command availability.

**Tech Stack:** Swift 5.9, AppKit, WebKit, SwiftPM/XCTest, TypeScript 5.9, Tiptap 3.29.2/ProseMirror, Vitest 4, Bash app-bundle packaging.

## Global Constraints

- Target macOS 13 or newer and preserve the current SwiftPM/AppKit architecture.
- `外部文件打开方式` defaults to `始终在新窗口中打开`; a missing persisted key must decode as `.newWindow`.
- External routing applies only to Finder/Open With/Dock/app-sent files, not the explicit in-app `在新窗口中打开…` command.
- Compare open files using standardized, symlink-resolved file URLs; activate an existing window instead of opening a duplicate.
- Validate replacement file content before prompting, saving, discarding, or replacing the current document.
- All close, current-window replacement, and application-quit decisions use the same asynchronous document-disposition coordinator.
- Untitled modified documents are never auto-saved and always use a TextEdit-style AppKit sheet followed by `NSSavePanel` when requested.
- Format painter is one-shot, appears only in the native Format and editor context menus, has no default shortcut, and is unavailable in source mode.
- Format painter copies only paragraph/heading 1–6 plus bold, italic, underline, strike, and inline code; it never changes text, links, images, lists, tables, themes, fonts, or colors.
- Display languages remain exactly `zh-Hans`, `zh-Hant`, `en`, and `ja`.
- Replace `changelog.txt` with four complete `.md` histories for versions 1.1.3 through 1.1.6; missing requested resources fall back to `zh-Hans`.
- Set both bundle version fields and the About fallback to exactly `1.1.6`.
- Preserve the already completed recovery-dialog localization, Outline/Workspace styling and first-click selection fixes, and Preferences language-refresh page/frame preservation.
- Include the committed `c8020bf` fix: dropping a Workspace item back into its existing sidebar folder is a silent no-op, while genuine missing-source and invalid-target errors still surface.
- Run Swift tests with `--disable-sandbox`; the existing synthetic AppKit click test may remain the sole sandbox-only failure and must not be changed.

## File Map

- `macos/Sources/MarkLeaf/Services/AppSettings.swift`: typed external-file mode and tolerant persistence.
- `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`: File-page popup and persistence.
- `macos/Sources/MarkLeaf/Services/DocumentDisposition.swift`: shared policy, choices, asynchronous coordinator, and sequential quit queue.
- `macos/Sources/MarkLeaf/Services/EditorSession.swift`: prepared-document loading (including pending initial content), completion-based save APIs, native disposition sheets, and coordinator integration.
- `macos/Sources/MarkLeaf/Views/EditorWindowController.swift`: prepared initial-document forwarding and asynchronous close gate.
- `macos/Sources/MarkLeaf/App/AppDelegate.swift`: multi-URL external open entry point and deferred AppKit termination.
- `macos/Sources/MarkLeaf/App/AppWindowManager.swift`: preflighted window creation, external routing, duplicate activation, sequential quit, changelog selection, and About version.
- `macos/Sources/MarkLeaf/Services/StartupBootstrapState.swift`: preserve every cold-start URL while consuming only the first in the initial window.
- `macos/Sources/MarkLeaf/Services/ChangelogResource.swift`: language normalization and fallback candidates.
- `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`: Format-menu item, validation, and native command routing.
- `macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift`: context-menu format-painter item and availability.
- `macos/Sources/MarkLeaf/Services/L10n.swift`: four-language UI strings.
- `src/EditorWeb/src/format-painter.ts`: source eligibility, capture, apply, cancel, and one-shot state machine.
- `src/EditorWeb/src/editor.ts`: expose format-painter eligibility in command state.
- `src/EditorWeb/src/main.ts`: own the controller and connect command, selection, Escape, source mode, and document-load events.
- `macos/Changelog/changelog.{zh-Hans,zh-Hant,en,ja}.md`: localized full release histories.
- `macos/script/build_and_run.sh`: bundle all changelogs and write version 1.1.6.
- Swift tests: settings/preferences, disposition, termination, routing/bootstrap, menus/localization, changelog, and release metadata.
- EditorWeb tests: `src/EditorWeb/tests/format-painter.test.ts`.

---

### Task 1: External-File Opening Preference and Localization

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/AppSettings.swift:1-130`
- Modify: `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift:31-38, 110-124, 234-285, 362-377`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Modify: `macos/Tests/MarkLeafTests/SettingsRigidityTests.swift`
- Modify: `macos/Tests/MarkLeafTests/L10nJapaneseTests.swift`
- Create: `macos/Tests/MarkLeafTests/ExternalFileOpenPreferenceTests.swift`

**Interfaces:**
- Produces: `enum ExternalFileOpenMode: String, Codable, CaseIterable { case newWindow; case currentWindow }`.
- Produces: `AppSettings.externalFileOpenMode: ExternalFileOpenMode` with `.newWindow` default and missing-key fallback.
- Produces: `ExternalFileOpenPreferenceModel.titles(language:)`, `selectedIndex(for:)`, and `mode(at:)` for UI and deterministic tests.
- Consumes: existing `SettingsService.update`, `L10n.translate`, and the File-page `NSGridView` form builder.

- [ ] **Step 1: Write failing persistence and preference-model tests**

Add these tests to `SettingsRigidityTests.swift`:

```swift
func testExternalFileOpenModeDefaultsToNewWindowWhenKeyIsMissing() throws {
    let data = Data("{\"schemaVersion\":3}".utf8)
    let settings = try JSONDecoder().decode(AppSettings.self, from: data)
    XCTAssertEqual(settings.externalFileOpenMode, .newWindow)
}

func testExternalFileOpenModeRoundTripsCurrentWindow() throws {
    var settings = AppSettings()
    settings.externalFileOpenMode = .currentWindow
    let decoded = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
    XCTAssertEqual(decoded.externalFileOpenMode, .currentWindow)
}
```

Create `ExternalFileOpenPreferenceTests.swift`:

```swift
import XCTest
@testable import MarkLeaf

final class ExternalFileOpenPreferenceTests: XCTestCase {
    func testPreferenceOrderAndSelectionMatchPersistedValues() {
        XCTAssertEqual(ExternalFileOpenPreferenceModel.selectedIndex(for: .newWindow), 0)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.selectedIndex(for: .currentWindow), 1)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.mode(at: 0), .newWindow)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.mode(at: 1), .currentWindow)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.mode(at: -1), .newWindow)
    }

    func testPreferenceCopyIsLocalizedInAllDisplayLanguages() {
        let expected: [String: [String]] = [
            "zh-Hans": ["始终在新窗口中打开", "在当前窗口中打开"],
            "zh-Hant": ["永遠在新視窗中開啟", "在目前視窗中開啟"],
            "en": ["Always Open in New Window", "Open in Current Window"],
            "ja": ["常に新規ウィンドウで開く", "現在のウィンドウで開く"],
        ]
        for (language, titles) in expected {
            XCTAssertEqual(ExternalFileOpenPreferenceModel.titles(language: language), titles)
        }
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "zh-Hans"), "外部文件打开方式")
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "zh-Hant"), "外部檔案開啟方式")
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "en"), "When Opening External Files")
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "ja"), "外部ファイルを開く方法")
    }
}
```

- [ ] **Step 2: Run the focused Swift tests and verify RED**

Run:

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-settings/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-settings/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-settings/scratch \
  --filter 'SettingsRigidityTests|ExternalFileOpenPreferenceTests'
```

Expected: compilation fails because `ExternalFileOpenMode`, `externalFileOpenMode`, and `ExternalFileOpenPreferenceModel` do not exist.

- [ ] **Step 3: Add the typed setting and pure popup model**

Add before `AppSettings`:

```swift
enum ExternalFileOpenMode: String, Codable, CaseIterable {
    case newWindow
    case currentWindow
}

enum ExternalFileOpenPreferenceModel {
    static let orderedModes = ExternalFileOpenMode.allCases

    static func titles(language: String) -> [String] {
        [
            L10n.translate("始终在新窗口中打开", language: language),
            L10n.translate("在当前窗口中打开", language: language),
        ]
    }

    static func selectedIndex(for mode: ExternalFileOpenMode) -> Int {
        orderedModes.firstIndex(of: mode) ?? 0
    }

    static func mode(at index: Int) -> ExternalFileOpenMode {
        orderedModes.indices.contains(index) ? orderedModes[index] : .newWindow
    }
}
```

In `AppSettings.init(from:)`, decode the new key immediately after `saveOnDocumentSwitch`; in stored properties, add it beside the other File settings:

```swift
externalFileOpenMode = try container.decodeIfPresent(
    ExternalFileOpenMode.self,
    forKey: .externalFileOpenMode
) ?? .newWindow
```

```swift
var externalFileOpenMode = ExternalFileOpenMode.newWindow
```

- [ ] **Step 4: Add and persist the File-page popup**

Add `private let externalFileOpenModePopup = NSPopUpButton()`. Populate it from `ExternalFileOpenPreferenceModel.titles(language: settings.displayLanguage)`, select the persisted mode, include it in the `controls` array, and place this row after `启动操作`:

```swift
.field(L10n.t("外部文件打开方式"), externalFileOpenModePopup),
```

Persist it in `controlChanged()`:

```swift
settings.externalFileOpenMode = ExternalFileOpenPreferenceModel.mode(
    at: externalFileOpenModePopup.indexOfSelectedItem
)
```

Add these exact translations to all three non-Simplified-Chinese tables:

| Key | zh-Hant | en | ja |
|---|---|---|---|
| `外部文件打开方式` | `外部檔案開啟方式` | `When Opening External Files` | `外部ファイルを開く方法` |
| `始终在新窗口中打开` | `永遠在新視窗中開啟` | `Always Open in New Window` | `常に新規ウィンドウで開く` |
| `在当前窗口中打开` | `在目前視窗中開啟` | `Open in Current Window` | `現在のウィンドウで開く` |

Replace the brittle Japanese key-count assertion in `L10nJapaneseTests.testJapaneseCoversAllEnglishKeys` with exact key-set parity, so every later localization task can add keys without manually updating a release-specific number:

```swift
func testJapaneseCoversExactlyTheEnglishKeys() {
    XCTAssertEqual(L10n.translationKeys(for: "ja"), L10n.translationKeys(for: "en"))
}
```

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: all selected tests pass.

- [ ] **Step 6: Commit the preference**

```bash
git add macos/Sources/MarkLeaf/Services/AppSettings.swift \
  macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift \
  macos/Sources/MarkLeaf/Services/L10n.swift \
  macos/Tests/MarkLeafTests/SettingsRigidityTests.swift \
  macos/Tests/MarkLeafTests/L10nJapaneseTests.swift \
  macos/Tests/MarkLeafTests/ExternalFileOpenPreferenceTests.swift
git commit -m "feat(macos): configure external file opening"
```

---

### Task 2: Unified Document-Disposition Coordinator and Safe Save APIs

**Files:**
- Create: `macos/Sources/MarkLeaf/Services/DocumentDisposition.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift:760-900`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Create: `macos/Tests/MarkLeafTests/DocumentDispositionTests.swift`

**Interfaces:**
- Produces: `DocumentDispositionReason`, `DocumentDispositionResult`, `SavedDocumentChoice`, `UntitledDocumentChoice`, and `DocumentDispositionDecision`.
- Produces: `DocumentDispositionPolicy.decision(isDirty:hasFileURL:reason:settings:)`.
- Produces: `DocumentDispositionCoordinator.request(_:settings:saveExisting:saveAs:presentSavedPrompt:presentUntitledPrompt:completion:) -> Bool` and `isInProgress`.
- Produces: `EditorSession.requestDisposition(for:completion:) -> Bool`, `saveDocument(completion:)`, and `saveDocumentAs(completion:)`.
- Consumes: Task 1 `AppSettings`, existing `requestSnapshot`, and existing localized save-error reporting.

- [ ] **Step 1: Write failing policy and coordinator tests**

Create table-driven tests with the exact expected decisions:

```swift
import XCTest
@testable import MarkLeaf

final class DocumentDispositionTests: XCTestCase {
    func testPolicyCoversCleanSavedAndUntitledDocuments() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        settings.saveOnDocumentSwitch = false

        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: false, hasFileURL: false, reason: .closeWindow, settings: settings
        ), .proceed)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings
        ), .promptSaved)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .terminateApplication, settings: settings
        ), .promptSaved)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .replaceDocument, settings: settings
        ), .promptSaved)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: false, reason: .replaceDocument, settings: settings
        ), .promptUntitled)
    }

    func testPolicyUsesTheReasonSpecificAutoSaveSetting() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        settings.saveOnDocumentSwitch = false
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings
        ), .autoSave)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .terminateApplication, settings: settings
        ), .autoSave)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .replaceDocument, settings: settings
        ), .promptSaved)

        settings.autoSaveEnabled = false
        settings.saveOnDocumentSwitch = true
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .replaceDocument, settings: settings
        ), .autoSave)
    }

    func testUntitledNeverAutoSaves() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        settings.saveOnDocumentSwitch = true
        for reason in DocumentDispositionReason.allCases {
            XCTAssertEqual(DocumentDispositionPolicy.decision(
                isDirty: true, hasFileURL: false, reason: reason, settings: settings
            ), .promptUntitled)
        }
    }
}
```

Add coordinator tests that retain the real completion closures and drive every asynchronous branch. Use this representative saved-document test, then repeat the same shape with literal expected results for saved discard/cancel, auto-save success/failure, untitled save-as success/cancellation, and untitled delete:

```swift
func testSavedPromptSaveWaitsForSuccessfulWriteBeforeProceeding() {
    var settings = AppSettings()
    settings.autoSaveEnabled = false
    let coordinator = DocumentDispositionCoordinator()
    var savedChoice: ((SavedDocumentChoice) -> Void)?
    var saveCompletion: ((Bool) -> Void)?
    var results: [DocumentDispositionResult] = []

    XCTAssertTrue(coordinator.request(
        isDirty: true,
        hasFileURL: true,
        reason: .closeWindow,
        settings: settings,
        saveExisting: { saveCompletion = $0 },
        saveAs: { XCTFail("saved document must not use Save As"); $0(false) },
        presentSavedPrompt: { savedChoice = $0 },
        presentUntitledPrompt: { XCTFail("saved document must not show untitled prompt") },
        completion: { results.append($0) }
    ))

    savedChoice?(.save)
    XCTAssertTrue(results.isEmpty)
    saveCompletion?(true)
    XCTAssertEqual(results, [.proceed])
    XCTAssertFalse(coordinator.isInProgress)
}

func testReentrantRequestIsRejectedUntilTheFirstPromptFinishes() {
    var settings = AppSettings()
    settings.autoSaveEnabled = false
    let coordinator = DocumentDispositionCoordinator()
    var firstPrompt: ((SavedDocumentChoice) -> Void)?
    var results: [DocumentDispositionResult] = []

    XCTAssertTrue(coordinator.request(
        isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
        saveExisting: { $0(true) }, saveAs: { $0(true) },
        presentSavedPrompt: { firstPrompt = $0 },
        presentUntitledPrompt: { _ in },
        completion: { results.append($0) }
    ))
    XCTAssertFalse(coordinator.request(
        isDirty: false, hasFileURL: false, reason: .closeWindow, settings: settings,
        saveExisting: { $0(true) }, saveAs: { $0(true) },
        presentSavedPrompt: { _ in }, presentUntitledPrompt: { _ in },
        completion: { results.append($0) }
    ))

    firstPrompt?(.cancel)
    XCTAssertEqual(results, [.cancel])
}
```

The remaining branch tests must assert these literal outcomes: saved `.discard → .proceed`, saved `.cancel → .cancel`, auto-save `true → .proceed`, auto-save `false → .cancel`, untitled `.saveAs` plus save completion `true → .proceed`, untitled `.saveAs` plus completion `false → .cancel`, and untitled `.delete → .proceed`. In each branch, route any unexpected prompt or save closure to `XCTFail`.

- [ ] **Step 2: Run the disposition tests and verify RED**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-disposition/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-disposition/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-disposition/scratch \
  --filter DocumentDispositionTests
```

Expected: compilation fails because the disposition types do not exist.

- [ ] **Step 3: Implement the pure policy and one-request-at-a-time coordinator**

Create `DocumentDisposition.swift` with these public-to-module types and mappings:

```swift
import Foundation

enum DocumentDispositionReason: CaseIterable {
    case closeWindow
    case replaceDocument
    case terminateApplication
}

enum DocumentDispositionResult: Equatable {
    case proceed
    case cancel
}

enum SavedDocumentChoice { case save, discard, cancel }
enum UntitledDocumentChoice { case saveAs, delete, cancel }
enum DocumentDispositionDecision: Equatable { case proceed, autoSave, promptSaved, promptUntitled }

enum DocumentDispositionPolicy {
    static func decision(
        isDirty: Bool,
        hasFileURL: Bool,
        reason: DocumentDispositionReason,
        settings: AppSettings
    ) -> DocumentDispositionDecision {
        guard isDirty else { return .proceed }
        guard hasFileURL else { return .promptUntitled }
        switch reason {
        case .replaceDocument:
            return settings.saveOnDocumentSwitch ? .autoSave : .promptSaved
        case .closeWindow, .terminateApplication:
            return settings.autoSaveEnabled ? .autoSave : .promptSaved
        }
    }
}

final class DocumentDispositionCoordinator {
    private var activeRequestID: UUID?
    var isInProgress: Bool { activeRequestID != nil }

    @discardableResult
    func request(
        isDirty: Bool,
        hasFileURL: Bool,
        reason: DocumentDispositionReason,
        settings: AppSettings,
        saveExisting: @escaping (@escaping (Bool) -> Void) -> Void,
        saveAs: @escaping (@escaping (Bool) -> Void) -> Void,
        presentSavedPrompt: @escaping (@escaping (SavedDocumentChoice) -> Void) -> Void,
        presentUntitledPrompt: @escaping (@escaping (UntitledDocumentChoice) -> Void) -> Void,
        completion: @escaping (DocumentDispositionResult) -> Void
    ) -> Bool {
        guard !isInProgress else { return false }
        let requestID = UUID()
        activeRequestID = requestID
        let finish: (DocumentDispositionResult) -> Void = { [weak self] result in
            guard self?.activeRequestID == requestID else { return }
            self?.activeRequestID = nil
            completion(result)
        }
        switch DocumentDispositionPolicy.decision(
            isDirty: isDirty,
            hasFileURL: hasFileURL,
            reason: reason,
            settings: settings
        ) {
        case .proceed:
            finish(.proceed)
        case .autoSave:
            saveExisting { finish($0 ? .proceed : .cancel) }
        case .promptSaved:
            presentSavedPrompt { choice in
                switch choice {
                case .save: saveExisting { finish($0 ? .proceed : .cancel) }
                case .discard: finish(.proceed)
                case .cancel: finish(.cancel)
                }
            }
        case .promptUntitled:
            presentUntitledPrompt { choice in
                switch choice {
                case .saveAs: saveAs { finish($0 ? .proceed : .cancel) }
                case .delete: finish(.proceed)
                case .cancel: finish(.cancel)
                }
            }
        }
        return true
    }
}
```

The request UUID is required: a stale save/prompt callback from an earlier request must not be able to finish a later request after the coordinator becomes active again. Add a regression test that completes request A, starts request B, invokes A's retained callback a second time, and asserts that B remains active until B's own callback completes.

- [ ] **Step 4: Refactor `EditorSession` saves into completion APIs**

Keep menu behavior source-compatible by defaulting completion to `nil`:

```swift
func saveDocument(completion: ((Bool) -> Void)? = nil) {
    if let url = documentURL {
        writeCurrentDocument(to: url, completion: completion)
    } else {
        saveDocumentAs(completion: completion)
    }
}

func saveDocumentAs(completion: ((Bool) -> Void)? = nil) {
    let panel = NSSavePanel()
    panel.title = L10n.t("保存 Markdown 文档")
    panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
    panel.nameFieldStringValue = documentURL?.lastPathComponent ?? L10n.t("未命名.md")
    guard let window = webView?.window else { completion?(false); return }
    panel.beginSheetModal(for: window) { [weak self] response in
        guard response == .OK, let url = panel.url else { completion?(false); return }
        self?.writeCurrentDocument(to: url, completion: completion)
    }
}
```

Remove `confirmDiscardOrSave`. Add one `DocumentDispositionCoordinator` property and implement `requestDisposition(for:completion:)`. Its `saveExisting` closure must guard `documentURL`, its `saveAs` closure must call the completion-based panel, and both native prompts must attach to `webView.window`.

Saved-document sheet:

```swift
alert.messageText = L10n.f("是否保存对“%@”的修改？", windowTitle)
alert.informativeText = L10n.t("如果不保存，您的更改将会丢失。")
alert.addButton(withTitle: L10n.t("保存"))
alert.addButton(withTitle: L10n.t("取消"))
alert.addButton(withTitle: L10n.t("不保存"))
```

Untitled-document sheet:

```swift
alert.messageText = L10n.t("是否保留这个新文档？")
alert.informativeText = L10n.t("如果不保存，这个文档将被删除。")
alert.addButton(withTitle: L10n.t("保存…"))
alert.addButton(withTitle: L10n.t("取消"))
alert.addButton(withTitle: L10n.t("删除"))
```

Map first/second/third buttons respectively to `.save`/`.cancel`/`.discard` and `.saveAs`/`.cancel`/`.delete`. If there is no host window, finish with `.cancel`; never silently proceed.

- [ ] **Step 5: Add exact sheet-copy translations**

| Key | zh-Hant | en | ja |
|---|---|---|---|
| `如果不保存，您的更改将会丢失。` | `如果不儲存，您的更改將會遺失。` | `Your changes will be lost if you don’t save them.` | `保存しない場合、変更内容は失われます。` |
| `是否保留这个新文档？` | `是否保留這個新文件？` | `Do you want to keep this new document?` | `この新規書類を残しますか？` |
| `如果不保存，这个文档将被删除。` | `如果不儲存，這個文件將被刪除。` | `This document will be deleted if you don’t save it.` | `保存しない場合、この書類は削除されます。` |
| `保存…` | `儲存…` | `Save…` | `保存…` |
| `删除` | `刪除` | `Delete` | `削除` |

- [ ] **Step 6: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all policy and coordinator tests pass.

- [ ] **Step 7: Commit the shared disposition flow**

```bash
git add macos/Sources/MarkLeaf/Services/DocumentDisposition.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Sources/MarkLeaf/Services/L10n.swift \
  macos/Tests/MarkLeafTests/DocumentDispositionTests.swift
git commit -m "feat(macos): unify unsaved document handling"
```

---

### Task 3: Window Close Gate and Deferred Application Quit

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/DocumentDisposition.swift`
- Modify: `macos/Sources/MarkLeaf/Views/EditorWindowController.swift:325-340`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift:1-80`
- Modify: `macos/Sources/MarkLeaf/App/AppDelegate.swift:1008-1025`
- Create: `macos/Tests/MarkLeafTests/ApplicationTerminationCoordinatorTests.swift`

**Interfaces:**
- Consumes: Task 2 `EditorSession.requestDisposition(for:completion:)`.
- Produces: `SequentialDocumentDispositionQueue.run(_:completion:)` where each request asynchronously returns `DocumentDispositionResult`.
- Produces: `AppWindowManager.requestApplicationTermination(completion:)`.
- Produces: `EditorWindowController.windowShouldClose(_:)` with a one-time final-close bypass.

- [ ] **Step 1: Write failing sequential-termination tests**

Create tests for all-success, cancel-in-middle, and save-failure-as-cancel:

```swift
import XCTest
@testable import MarkLeaf

final class ApplicationTerminationCoordinatorTests: XCTestCase {
    func testSequentialQueueProcessesEveryWindowInOrder() {
        var visited: [Int] = []
        let requests = [1, 2, 3].map { value in
            { (finish: @escaping (DocumentDispositionResult) -> Void) in
                visited.append(value)
                finish(.proceed)
            }
        }
        SequentialDocumentDispositionQueue.run(requests) { result in
            XCTAssertEqual(result, .proceed)
            XCTAssertEqual(visited, [1, 2, 3])
        }
    }

    func testSequentialQueueStopsAtFirstCancellation() {
        var visited: [Int] = []
        let requests: [SequentialDocumentDispositionQueue.Request] = [
            { visited.append(1); $0(.proceed) },
            { visited.append(2); $0(.cancel) },
            { visited.append(3); $0(.proceed) },
        ]
        SequentialDocumentDispositionQueue.run(requests) { result in
            XCTAssertEqual(result, .cancel)
            XCTAssertEqual(visited, [1, 2])
        }
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-quit/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-quit/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-quit/scratch \
  --filter ApplicationTerminationCoordinatorTests
```

Expected: compilation fails because `SequentialDocumentDispositionQueue` does not exist.

- [ ] **Step 3: Implement the sequential queue**

Append to `DocumentDisposition.swift`:

```swift
enum SequentialDocumentDispositionQueue {
    typealias Request = (@escaping (DocumentDispositionResult) -> Void) -> Void

    static func run(
        _ requests: [Request],
        completion: @escaping (DocumentDispositionResult) -> Void
    ) {
        func advance(_ index: Int) {
            guard requests.indices.contains(index) else { completion(.proceed); return }
            requests[index] { result in
                switch result {
                case .proceed: advance(index + 1)
                case .cancel: completion(.cancel)
                }
            }
        }
        advance(0)
    }
}
```

- [ ] **Step 4: Gate window close through the shared coordinator**

Add `private var allowsNextClose = false` to `EditorWindowController`. Implement:

```swift
func windowShouldClose(_ sender: NSWindow) -> Bool {
    if allowsNextClose {
        allowsNextClose = false
        return true
    }
    guard !session.isDocumentDispositionInProgress else { return false }
    _ = session.requestDisposition(for: .closeWindow) { [weak self, weak sender] result in
        guard result == .proceed, let self, let sender else { return }
        self.allowsNextClose = true
        sender.performClose(nil)
    }
    return false
}
```

Expose `EditorSession.isDocumentDispositionInProgress` as a read-only computed property backed by Task 2’s coordinator. Leave cleanup in `windowWillClose`, so recovery files and watchers are removed only after approval.

- [ ] **Step 5: Add manager and AppDelegate deferred termination**

In `AppWindowManager` snapshot the current controller order and convert it to requests:

```swift
func requestApplicationTermination(completion: @escaping (Bool) -> Void) {
    let requests = windowControllers.map { controller in
        { (finish: @escaping (DocumentDispositionResult) -> Void) in
            let started = controller.session.requestDisposition(
                for: .terminateApplication,
                completion: finish
            )
            if !started { finish(.cancel) }
        }
    }
    SequentialDocumentDispositionQueue.run(requests) { result in
        completion(result == .proceed)
    }
}
```

Treat an already-active close/replacement disposition as a cancelled quit request. Never leave AppKit waiting for a completion when `requestDisposition` returns `false`.

In `AppDelegate`, add `private var terminationRequestInProgress = false` and implement:

```swift
func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
    guard !terminationRequestInProgress else { return .terminateLater }
    terminationRequestInProgress = true
    AppWindowManager.shared.requestApplicationTermination { [weak self, weak sender] allowed in
        self?.terminationRequestInProgress = false
        sender?.reply(toApplicationShouldTerminate: allowed)
    }
    return .terminateLater
}
```

Do not move cleanup out of `applicationWillTerminate`; it must run only after AppKit receives an allowed reply.

- [ ] **Step 6: Run tests and verify GREEN**

Run the command from Step 2, then also run `--filter DocumentDispositionTests`. Expected: both suites pass.

- [ ] **Step 7: Commit close and quit safety**

```bash
git add macos/Sources/MarkLeaf/Services/DocumentDisposition.swift \
  macos/Sources/MarkLeaf/Views/EditorWindowController.swift \
  macos/Sources/MarkLeaf/App/AppWindowManager.swift \
  macos/Sources/MarkLeaf/App/AppDelegate.swift \
  macos/Tests/MarkLeafTests/ApplicationTerminationCoordinatorTests.swift
git commit -m "feat(macos): guard close and quit with save prompts"
```

---

### Task 4: External Multi-File Routing, Duplicate Avoidance, and Preflight Reads

**Files:**
- Create: `macos/Sources/MarkLeaf/Services/IncomingFileRoutingPolicy.swift`
- Create: `macos/Sources/MarkLeaf/Services/IncomingFileRouter.swift`
- Modify: `macos/Sources/MarkLeaf/Services/StartupBootstrapState.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift:780-815, 1200-1235`
- Modify: `macos/Sources/MarkLeaf/Views/EditorWindowController.swift:50-65`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift:25-45, 315-340`
- Modify: `macos/Sources/MarkLeaf/App/AppDelegate.swift:1038-1044`
- Create: `macos/Tests/MarkLeafTests/IncomingFileRoutingPolicyTests.swift`
- Modify: `macos/Tests/MarkLeafTests/StartupIntegrationStateTests.swift`

**Interfaces:**
- Consumes: Task 1 `ExternalFileOpenMode` and Task 2 `.replaceDocument` disposition.
- Produces: `PreparedDocument.read(from:)`, `EditorSession.openDocument(at:)` that reads before disposition, and `EditorSession.openInitialDocument(prepared:)` that bypasses the one-time startup-action resolver.
- Produces: `IncomingFileRoutingPolicy.action(mode:eventIndex:hasActiveEditor:hasOpenDuplicate:)` for post-bootstrap events.
- Produces: `IncomingFileRouter.route(urls:mode:activeEditor:openDocuments:activateExisting:replaceActive:createWindow:)` as a side-effect-injected post-bootstrap integration boundary.
- Produces: `AppWindowManager.openExternalDocuments(_:)` for the full URL array.
- Produces: `StartupBootstrapState.cacheIncomingDocumentsIfNeeded(_:)` and completion containing the initial path plus additional paths.
- Produces: `AppWindowManager.newWindow(preparedDocument:)`, which creates a window only after the file has been read successfully.

- [ ] **Step 1: Write routing and bootstrap tests**

Create `IncomingFileRoutingPolicyTests.swift`:

```swift
import XCTest
@testable import MarkLeaf

final class IncomingFileRoutingPolicyTests: XCTestCase {
    func testDuplicateAlwaysActivatesExistingWindow() {
        for mode in ExternalFileOpenMode.allCases {
            XCTAssertEqual(IncomingFileRoutingPolicy.action(
                mode: mode, eventIndex: 0, hasActiveEditor: true,
                hasOpenDuplicate: true
            ), .activateExisting)
        }
    }

    func testNewWindowModeCreatesAWindowAfterBootstrap() {
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .newWindow, eventIndex: 0, hasActiveEditor: true,
            hasOpenDuplicate: false
        ), .createWindow)
    }

    func testCurrentWindowModeReplacesOnlyFirstURL() {
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .currentWindow, eventIndex: 0, hasActiveEditor: true,
            hasOpenDuplicate: false
        ), .replaceActive)
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .currentWindow, eventIndex: 1, hasActiveEditor: true,
            hasOpenDuplicate: false
        ), .createWindow)
    }

    func testCurrentWindowModeCreatesWhenNoEditorExists() {
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .currentWindow, eventIndex: 0, hasActiveEditor: false,
            hasOpenDuplicate: false
        ), .createWindow)
    }
}
```

In the same file, add a router integration test that proves observable ordering without constructing AppKit windows:

```swift
func testRouterActivatesDuplicateReplacesFirstAndCreatesRemainingWindow() {
    let duplicate = URL(fileURLWithPath: "/workspace/already.md")
    let replacement = URL(fileURLWithPath: "/workspace/replace.md")
    let additional = URL(fileURLWithPath: "/workspace/additional.md")
    var actions: [String] = []

    IncomingFileRouter.route(
        urls: [duplicate, replacement, additional],
        mode: .currentWindow,
        activeEditor: true,
        openDocuments: [duplicate],
        activateExisting: { actions.append("activate:\($0.path)") },
        replaceActive: { actions.append("replace:\($0.path)") },
        createWindow: { actions.append("create:\($0.path)") }
    )

    XCTAssertEqual(actions, [
        "activate:/workspace/already.md",
        "create:/workspace/replace.md",
        "create:/workspace/additional.md",
    ])
}
```

This event intentionally demonstrates that `eventIndex` is based on the original Finder array: activating a duplicate first does not promote the second URL into the current-window replacement slot. Add a separate literal test with `[replacement, additional]` expecting `replace` then `create`.

Add an event-local duplicate test; the second occurrence must be ignored even though `openDocuments` is only the snapshot from before routing began:

```swift
func testRouterCollapsesDuplicateURLsInsideOneFinderEvent() {
    let file = URL(fileURLWithPath: "/workspace/repeated.md")
    var actions: [String] = []

    IncomingFileRouter.route(
        urls: [file, file],
        mode: .newWindow,
        activeEditor: true,
        openDocuments: [],
        activateExisting: { actions.append("activate:\($0.path)") },
        replaceActive: { actions.append("replace:\($0.path)") },
        createWindow: { actions.append("create:\($0.path)") }
    )

    XCTAssertEqual(actions, ["create:/workspace/repeated.md"])
}
```

Replace the single-path bootstrap expectations with:

```swift
func testColdStartCachesEveryFinderFileWithoutOverwritingTheFirst() {
    var state = StartupBootstrapState()
    XCTAssertTrue(state.cacheIncomingDocumentsIfNeeded(["/a.md", "/b.md", "/c.md"]))
    XCTAssertEqual(state.complete(), .createInitialWindow(
        documentPath: "/a.md",
        additionalDocumentPaths: ["/b.md", "/c.md"]
    ))
}

func testColdStartCollapsesDuplicateFinderPathsInTheirFirstSeenOrder() {
    var state = StartupBootstrapState()
    XCTAssertTrue(state.cacheIncomingDocumentsIfNeeded(["/a.md", "/a.md", "/b.md"]))
    XCTAssertEqual(state.complete(), .createInitialWindow(
        documentPath: "/a.md",
        additionalDocumentPaths: ["/b.md"]
    ))
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-routing/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-routing/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-routing/scratch \
  --filter 'IncomingFileRoutingPolicyTests|StartupIntegrationStateTests'
```

Expected: compilation fails for the new policy and bootstrap API.

- [ ] **Step 3: Implement the pure routing decision**

Create:

```swift
enum IncomingFileRouteAction: Equatable {
    case activateExisting
    case replaceActive
    case createWindow
}

enum IncomingFileRoutingPolicy {
    static func action(
        mode: ExternalFileOpenMode,
        eventIndex: Int,
        hasActiveEditor: Bool,
        hasOpenDuplicate: Bool
    ) -> IncomingFileRouteAction {
        if hasOpenDuplicate { return .activateExisting }
        if mode == .currentWindow && eventIndex == 0 && hasActiveEditor { return .replaceActive }
        return .createWindow
    }
}
```

Create `IncomingFileRouter.swift` as the only post-bootstrap loop. Keep the original array index authoritative, ignore non-file URLs, collapse event-local duplicates, and compare against the pre-event open-document snapshot:

```swift
import Foundation

enum IncomingFileRouter {
    static func normalized(_ url: URL) -> URL {
        url.standardizedFileURL.resolvingSymlinksInPath()
    }

    static func route(
        urls: [URL],
        mode: ExternalFileOpenMode,
        activeEditor: Bool,
        openDocuments: [URL],
        activateExisting: (URL) -> Void,
        replaceActive: (URL) -> Void,
        createWindow: (URL) -> Void
    ) {
        let open = Set(openDocuments.filter(\.isFileURL).map(normalized))
        var seenInEvent = Set<URL>()

        for (eventIndex, rawURL) in urls.enumerated() where rawURL.isFileURL {
            let url = normalized(rawURL)
            guard seenInEvent.insert(url).inserted else { continue }
            switch IncomingFileRoutingPolicy.action(
                mode: mode,
                eventIndex: eventIndex,
                hasActiveEditor: activeEditor,
                hasOpenDuplicate: open.contains(url)
            ) {
            case .activateExisting: activateExisting(url)
            case .replaceActive: replaceActive(url)
            case .createWindow: createWindow(url)
            }
        }
    }
}
```

`StartupBootstrapState` remains the sole owner of pre-bootstrap URL queuing. `AppWindowManager` supplies AppKit closures; tests supply array-appending closures.

- [ ] **Step 4: Preserve all cold-start URLs**

Change `StartupBootstrapState.Completion.createInitialWindow` to carry `additionalDocumentPaths: [String]`. Store `[String]` rather than one pending path. `cacheIncomingDocumentsIfNeeded(_:)` must append each first-seen path while bootstrap is incomplete, collapse duplicates without reordering, and return `false` after completion. The caller already supplies standardized, symlink-resolved paths. `complete()` returns the first distinct path as `documentPath`, the remainder as `additionalDocumentPaths`, then clears the array.

Update `completeBootstrapAndEnsureInitialWindow()` so Finder input bypasses the startup-action resolver but still consumes `StartupActionState` exactly once before creating the prepared first window. This prevents a later blank `⌘N` window from unexpectedly running the configured startup action. Then preflight the first path, create the initial window from its `PreparedDocument`, and preflight/create one prepared window per additional path in order. If the first path cannot be read, create the required initial blank window and report the localized open error there; if an additional path cannot be read, report the error without creating an extra window. With no Finder input, preserve the existing startup-action flow.

- [ ] **Step 5: Read replacement content before disposition**

Add:

```swift
struct PreparedDocument: Equatable {
    let url: URL
    let markdown: String

    static func read(from url: URL) throws -> PreparedDocument {
        let standardized = url.standardizedFileURL.resolvingSymlinksInPath()
        return PreparedDocument(
            url: standardized,
            markdown: try String(contentsOf: standardized, encoding: .utf8)
        )
    }
}
```

Refactor `openDocument(at:)` to call `PreparedDocument.read` first, report `无法打开文档：%@` and return on failure, then request `.replaceDocument`, and only on `.proceed` call a private `loadPreparedDocument(_:)`. Remove the second disk read from the post-disposition path.

Add focused `PreparedDocument` tests using a temporary valid Markdown file, a missing path, and a file containing invalid UTF-8 bytes. Add an `EditorSession` test seam around `loadPreparedDocument`/disposition so the invalid-file case asserts no disposition request occurs and the current document URL, dirty flag, and content identity remain unchanged.

Add `pendingInitialPreparedDocument` to `EditorSession`, an `openInitialDocument(prepared:)` overload, and consume that prepared value first in `runInitialLoad()`. Add matching forwarding on `EditorWindowController` and an `AppWindowManager.newWindow(preparedDocument:)` overload. The prepared overload must not call `performStartupAction`; cold-start bootstrap explicitly marks that one-shot state consumed before using it. Also change the explicit in-app `openDocumentInNewWindow()` panel completion to preflight and call the prepared overload, fixing later new-window opens that currently pass through the already-consumed startup state.

- [ ] **Step 6: Route every external URL and activate duplicates**

Replace `openDocumentInFrontWindow(_:)` with `openExternalDocuments(_:)`. Its algorithm must be:

1. Drop non-file URLs.
2. Standardize and resolve symlinks.
3. If bootstrap is incomplete, cache the whole path array and return.
4. Snapshot normalized `session.documentURL` values and call `IncomingFileRouter.route` with the whole original URL array.
5. `activateExisting`: find the matching controller, call `makeKeyAndOrderFront(nil)`, and activate the app.
6. `replaceActive`: call `activeWindowController.session.openDocument(at:)`; that method owns preflight-before-disposition.
7. `createWindow`: call `PreparedDocument.read`; only on success call `newWindow(preparedDocument:)`, otherwise report the localized open error without creating a window.

Change AppDelegate to preserve all URLs:

```swift
func application(_ application: NSApplication, open urls: [URL]) {
    AppWindowManager.shared.openExternalDocuments(urls)
}
```

Do not change `openDocumentInNewWindow()`; it remains the explicit in-app command.

- [ ] **Step 7: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: all policy, router ordering, duplicate, and bootstrap tests pass with no AppKit-window dependency.

- [ ] **Step 8: Commit external routing**

```bash
git add macos/Sources/MarkLeaf/Services/IncomingFileRoutingPolicy.swift \
  macos/Sources/MarkLeaf/Services/IncomingFileRouter.swift \
  macos/Sources/MarkLeaf/Services/StartupBootstrapState.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Sources/MarkLeaf/Views/EditorWindowController.swift \
  macos/Sources/MarkLeaf/App/AppWindowManager.swift \
  macos/Sources/MarkLeaf/App/AppDelegate.swift \
  macos/Tests/MarkLeafTests/IncomingFileRoutingPolicyTests.swift \
  macos/Tests/MarkLeafTests/StartupIntegrationStateTests.swift
git commit -m "feat(macos): route external files across windows"
```

---

### Task 5: EditorWeb One-Shot Format-Painter State Machine

**Files:**
- Create: `src/EditorWeb/src/format-painter.ts`
- Modify: `src/EditorWeb/src/editor.ts:276-300, 480-520`
- Modify: `src/EditorWeb/src/main.ts:1-25, 125-190, 250-335, 540-615`
- Create: `src/EditorWeb/tests/format-painter.test.ts`

**Interfaces:**
- Produces: `PaintableBlock`, `FormatPainterSnapshot`, `FormatPainterState`, `isPaintableTextSelection`, `captureFormat`, `applyCapturedFormat`, and `FormatPainterController`.
- Produces: `EditorCommandState.canStartFormatPainter` and `EditorCommandState.formatPainterArmed`.
- Consumes: Tiptap `Editor`, ProseMirror `TextSelection`, existing `selectionUpdate`, `loadDocument`, source-mode toggle, host command, and global Escape handling.

- [ ] **Step 1: Write failing format-painter tests**

Create the test file with editor cleanup matching `roundtrip.test.ts`. Cover eligibility and state transitions with these representative assertions:

```ts
it('captures a heading and uniform supported marks', () => {
  const editor = makeEditor('## **source**\n\ntarget')
  selectText(editor, 'source')
  const snapshot = captureFormat(editor)
  expect(snapshot).toEqual({
    block: 'heading2',
    marks: { bold: true, italic: false, underline: false, strike: false, code: false },
  })
})

it('rejects caret, cross-block, list, table, node, and mixed-mark sources', () => {
  expect(captureFormat(editorWithCaret())).toBeNull()
  expect(captureFormat(editorWithCrossParagraphSelection())).toBeNull()
  expect(captureFormat(editorWithListSelection())).toBeNull()
  expect(captureFormat(editorWithTableSelection())).toBeNull()
  expect(captureFormat(editorWithSelectedImage())).toBeNull()
  expect(captureFormat(editorWithPartiallyBoldSelection())).toBeNull()
})

it('applies once without changing text or link href and one undo restores the target', () => {
  const editor = makeEditor('## **source**\n\n[target](https://example.com)')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.handleSelectionUpdate(editor)).toBe('applied')
  expect(painter.isArmed).toBe(false)
  expect(editor.getMarkdown()).toContain('## **[target](https://example.com)**')
  editor.commands.undo()
  expect(editor.getMarkdown()).toContain('[target](https://example.com)')
})

it('cancels on invalid target and never remains armed', () => {
  const editor = makeEditor('**source**\n\n- target')
  const painter = new FormatPainterController()
  selectText(editor, 'source')
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.handleSelectionUpdate(editor)).toBe('cancelled')
  expect(painter.isArmed).toBe(false)
})
```

Also add exact tests that: the unchanged source range returns `waiting`; paragraph/heading levels 1–6 round-trip; all five supported marks are replaced on the target; `cancel()` returns to idle; applying twice changes only the first target; and target text content is byte-for-byte unchanged.

Tiptap 3.29.2's inline-code mark has `excludes: "_"`, so it cannot coexist with a link. Add this exact preservation regression: capture inline code, target linked text, expect `cancelled`, unchanged Markdown link destination/text, and idle state. Non-code formatting applied to a link must still preserve its `href`.

- [ ] **Step 2: Run Vitest and verify RED**

```bash
cd src/EditorWeb
./node_modules/.bin/vitest run tests/format-painter.test.ts
```

Expected: test collection fails because `../src/format-painter` does not exist.

- [ ] **Step 3: Implement structural eligibility and uniform mark capture**

Use these exact exported shapes:

```ts
import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'

export type PaintableBlock = 'paragraph' | `heading${1 | 2 | 3 | 4 | 5 | 6}`
export type FormatPainterSnapshot = {
  block: PaintableBlock
  marks: { bold: boolean; italic: boolean; underline: boolean; strike: boolean; code: boolean }
}
export type FormatPainterState =
  | { mode: 'idle' }
  | { mode: 'armed'; snapshot: FormatPainterSnapshot; sourceRange: { from: number; to: number } }
```

`isPaintableTextSelection(editor)` must require a non-empty `TextSelection`, one shared textblock parent of type `paragraph` or `heading`, and no ancestor named `bulletList`, `orderedList`, `taskList`, `listItem`, `taskItem`, `table`, `tableRow`, `tableCell`, or `tableHeader`. `captureFormat` must traverse every selected text segment and return `null` if any supported mark is present on only part of the source selection.

- [ ] **Step 4: Apply in one Tiptap chain and preserve links**

Build one chain, set paragraph or heading, and for each supported mark call only that mark’s `setMark` or `unsetMark`; never call `unsetAllMarks`:

```ts
export function applyCapturedFormat(editor: Editor, snapshot: FormatPainterSnapshot): boolean {
  if (!isPaintableTextSelection(editor)) return false
  if (snapshot.marks.code && selectionContainsMark(editor, 'link')) return false
  let chain = editor.chain().focus()
  chain = snapshot.block === 'paragraph'
    ? chain.setParagraph()
    : chain.setHeading({ level: Number(snapshot.block.slice('heading'.length)) as 1 | 2 | 3 | 4 | 5 | 6 })
  for (const mark of ['bold', 'italic', 'underline', 'strike', 'code'] as const) {
    chain = snapshot.marks[mark] ? chain.setMark(mark) : chain.unsetMark(mark)
  }
  return chain.run()
}
```

Implement `selectionContainsMark` by traversing `editor.state.doc.nodesBetween(from, to, ...)` and checking every selected text node's marks; do not rely on `editor.isActive('link')`, which does not prove that a partially linked target is safe. Because one chain produces one ProseMirror transaction, a single undo must restore the prior target block and marks. The explicit linked-inline-code rejection is required to uphold the release rule that format painter never removes or changes link marks.

- [ ] **Step 5: Implement the one-shot controller**

`arm` captures and stores the source range. `handleSelectionUpdate` returns `waiting` for the same range, cancels on the first different invalid selection, and sets state to idle before applying a valid target so transaction events cannot re-enter an armed state.

```ts
export class FormatPainterController {
  state: FormatPainterState = { mode: 'idle' }
  get isArmed(): boolean { return this.state.mode === 'armed' }

  arm(editor: Editor): boolean {
    const snapshot = captureFormat(editor)
    if (!snapshot) return false
    const { from, to } = editor.state.selection
    this.state = { mode: 'armed', snapshot, sourceRange: { from, to } }
    return true
  }

  cancel(): void { this.state = { mode: 'idle' } }

  handleSelectionUpdate(editor: Editor): 'waiting' | 'applied' | 'cancelled' {
    if (this.state.mode !== 'armed') return 'waiting'
    const armed = this.state
    const { from, to } = editor.state.selection
    if (from === armed.sourceRange.from && to === armed.sourceRange.to) return 'waiting'
    this.state = { mode: 'idle' }
    if (!isPaintableTextSelection(editor)) return 'cancelled'
    return applyCapturedFormat(editor, armed.snapshot) ? 'applied' : 'cancelled'
  }
}
```

- [ ] **Step 6: Integrate with EditorWeb command state and lifecycle**

Create one `const formatPainter = new FormatPainterController()` beside the editor. Extend `sendCommandState()` with:

```ts
canStartFormatPainter: !sourceMode && captureFormat(editor) !== null,
formatPainterArmed: !sourceMode && formatPainter.isArmed,
```

In visual-editor `selectionUpdate`, call `formatPainter.handleSelectionUpdate(targetEditor)` before sending state. In host command handling, special-case `formatPainter` to call `arm(editor)` rather than `executeEditorCommand`. Cancel before replacing the editor on `loadDocument`, before entering either direction of source mode, and when `Escape` is pressed. If Escape cancels an armed painter, call `preventDefault()`, send state, and return before the find-bar Escape branch.

Add both fields to `EditorCommandState` in `editor.ts`, with `getEditorCommandState` returning `false` defaults; `main.ts` overwrites them with lifecycle-aware values.

- [ ] **Step 7: Run format-painter and full EditorWeb tests**

```bash
cd src/EditorWeb
./node_modules/.bin/vitest run tests/format-painter.test.ts
./node_modules/.bin/vitest run
npm run build
```

Expected: the focused suite passes, the existing 62-test baseline plus the new tests passes, and TypeScript/Vite build succeeds.

- [ ] **Step 8: Commit EditorWeb format painter**

```bash
git add src/EditorWeb/src/format-painter.ts \
  src/EditorWeb/src/editor.ts \
  src/EditorWeb/src/main.ts \
  src/EditorWeb/tests/format-painter.test.ts
git commit -m "feat(editor): add one-shot format painter"
```

---

### Task 6: Native Format and Context Menu Integration

**Files:**
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift:145-165, 306-340, 515-565`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift:1-35, 165-185`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift:10-55, 75-86`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Create: `macos/Tests/MarkLeafTests/FormatPainterMenuTests.swift`

**Interfaces:**
- Consumes: Task 5 command-state fields and host command `formatPainter`.
- Produces: `EditorSession.canStartFormatPainter` and `isFormatPainterArmed` read-only state.
- Produces: native command ID `formatPainter` in both required menus, with no key equivalent.

- [ ] **Step 1: Write failing native menu tests**

```swift
import XCTest
@testable import MarkLeaf

final class FormatPainterMenuTests: XCTestCase {
    func testFormatMenuContainsFormatPainterWithoutShortcut() {
        let main = NativeMenuBuilder().build()
        let format = main.items.first { $0.title == L10n.t("格式") }?.submenu
        let item = format?.items.first { ($0.representedObject as? String) == "formatPainter" }
        XCTAssertEqual(item?.title, L10n.t("格式刷"))
        XCTAssertEqual(item?.keyEquivalent, "")
    }

    func testFormatPainterHasFourLanguageCopy() {
        XCTAssertEqual(L10n.translate("格式刷", language: "zh-Hans"), "格式刷")
        XCTAssertEqual(L10n.translate("格式刷", language: "zh-Hant"), "格式刷")
        XCTAssertEqual(L10n.translate("格式刷", language: "en"), "Format Painter")
        XCTAssertEqual(L10n.translate("格式刷", language: "ja"), "書式のコピー/貼り付け")
    }
}
```

- [ ] **Step 2: Run test and verify RED**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-menu/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-menu/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-menu/scratch \
  --filter FormatPainterMenuTests
```

Expected: the Format menu item assertion fails.

- [ ] **Step 3: Parse EditorWeb availability and route the command**

Add session properties defaulting to `false`, then parse both booleans in `commandStateChanged`. Add:

```swift
case "formatPainter": execute("formatPainter")
```

to `performMenuCommand`.

- [ ] **Step 4: Add native menu entries and validation**

In `formatMenu()`, add `格式刷` after the five inline-format commands and before link/image commands, separated from both groups. Do not pass a key equivalent.

In `validateMenuItem`:

```swift
case "formatPainter":
    return s?.isSourceMode == false
        && s?.canStartFormatPainter == true
        && s?.isFormatPainterArmed == false
```

In the context menu, add the same command after inline code and before headings. Set its `isEnabled` from the same three conditions before adding it. The item must use existing `handleCommand(_:)`, so native and context routes stay identical.

- [ ] **Step 5: Add exact localization**

Add `格式刷` as `格式刷` in Traditional Chinese, `Format Painter` in English, and `書式のコピー/貼り付け` in Japanese. No armed status copy is shown in 1.1.6.

- [ ] **Step 6: Run Swift and EditorWeb command-state tests**

Run the command from Step 2, then:

```bash
cd src/EditorWeb
./node_modules/.bin/vitest run tests/format-painter.test.ts tests/roundtrip.test.ts
```

Expected: both suites pass.

- [ ] **Step 7: Commit native integration**

```bash
git add macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift \
  macos/Sources/MarkLeaf/Services/L10n.swift \
  macos/Tests/MarkLeafTests/FormatPainterMenuTests.swift
git commit -m "feat(macos): expose format painter in native menus"
```

---

### Task 7: Four-Language Markdown Changelogs and Resolver

**Files:**
- Delete: `macos/Changelog/changelog.txt`
- Create: `macos/Changelog/changelog.zh-Hans.md`
- Create: `macos/Changelog/changelog.zh-Hant.md`
- Create: `macos/Changelog/changelog.en.md`
- Create: `macos/Changelog/changelog.ja.md`
- Create: `macos/Sources/MarkLeaf/Services/ChangelogResource.swift`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift:260-290`
- Modify: `macos/script/build_and_run.sh:45-55`
- Modify: `macos/Tests/MarkLeafTests/ChangelogMenuTests.swift`

**Interfaces:**
- Produces: `ChangelogResource.candidateFileNames(displayLanguage:) -> [String]` and `bundledURL(in:displayLanguage:) -> URL?`.
- Consumes: `AppSettings.displayLanguage`, existing cache directory, existing `无法打开更新内容` error, and `EditorSession.openDocument(at:)`.

- [ ] **Step 1: Write failing resolver and resource tests**

Extend `ChangelogMenuTests`:

```swift
func testChangelogCandidatesUseRequestedLanguageThenSimplifiedChineseFallback() {
    XCTAssertEqual(ChangelogResource.candidateFileNames(displayLanguage: "en"), [
        "changelog.en.md", "changelog.zh-Hans.md",
    ])
    XCTAssertEqual(ChangelogResource.candidateFileNames(displayLanguage: "zh-Hans"), [
        "changelog.zh-Hans.md",
    ])
    XCTAssertEqual(ChangelogResource.candidateFileNames(displayLanguage: "unsupported"), [
        "changelog.zh-Hans.md",
    ])
}

func testBundledLookupFallsBackAndReturnsNilWhenEveryCandidateIsMissing() throws {
    let fallbackBundle = try makeTemporaryBundle(resources: [
        "Changelog/changelog.zh-Hans.md": "# fallback",
    ])
    XCTAssertEqual(
        ChangelogResource.bundledURL(in: fallbackBundle, displayLanguage: "en")?.lastPathComponent,
        "changelog.zh-Hans.md"
    )

    let emptyBundle = try makeTemporaryBundle(resources: [:])
    XCTAssertNil(ChangelogResource.bundledURL(in: emptyBundle, displayLanguage: "ja"))
}

func testCachedTargetKeepsMarkdownExtensionAndSelectedLanguageName() {
    let source = URL(fileURLWithPath: "/bundle/Changelog/changelog.ja.md")
    let cache = URL(fileURLWithPath: "/tmp/MarkLeaf/Cache", isDirectory: true)
    XCTAssertEqual(
        ChangelogResource.cachedURL(for: source, cacheDirectory: cache).lastPathComponent,
        "changelog.ja.md"
    )
}

func testEveryLocalizedMarkdownContainsFullHistory() throws {
    let changelogDirectory = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Changelog")
    for language in ["zh-Hans", "zh-Hant", "en", "ja"] {
        let url = changelogDirectory.appendingPathComponent("changelog.\(language).md")
        let text = try String(contentsOf: url, encoding: .utf8)
        for version in ["1.1.6", "1.1.5", "1.1.4", "1.1.3"] {
            XCTAssertTrue(text.contains(version), "\(language) is missing \(version)")
        }
    }
}

func testBuildCopiesTheWholeChangelogDirectory() throws {
    let root = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    let script = try String(contentsOf: root.appendingPathComponent("script/build_and_run.sh"))
    XCTAssertTrue(script.contains("cp -R \"$ROOT_DIR/Changelog/.\" \"$APP_CONTENTS/Resources/Changelog/\""))
    XCTAssertFalse(script.contains("changelog.txt"))
}
```

- [ ] **Step 2: Run the changelog test and verify RED**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-changelog/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-changelog/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-changelog/scratch \
  --filter ChangelogMenuTests
```

Expected: compilation fails for `ChangelogResource` and localized files are absent.

- [ ] **Step 3: Implement language candidates and bundle lookup**

```swift
import Foundation

enum ChangelogResource {
    private static let supported = Set(["zh-Hans", "zh-Hant", "en", "ja"])

    static func candidateFileNames(displayLanguage: String) -> [String] {
        let language = supported.contains(displayLanguage) ? displayLanguage : "zh-Hans"
        let requested = "changelog.\(language).md"
        let fallback = "changelog.zh-Hans.md"
        return requested == fallback ? [fallback] : [requested, fallback]
    }

    static func bundledURL(in bundle: Bundle, displayLanguage: String) -> URL? {
        for name in candidateFileNames(displayLanguage: displayLanguage) {
            let stem = String(name.dropLast(3))
            if let url = bundle.url(forResource: stem, withExtension: "md", subdirectory: "Changelog") {
                return url
            }
        }
        return nil
    }

    static func cachedURL(for source: URL, cacheDirectory: URL) -> URL {
        cacheDirectory.appendingPathComponent(source.lastPathComponent)
    }
}
```

Update `openChangelog()` to resolve by current `displayLanguage`, copy to `ChangelogResource.cachedURL`, and open the cached `.md`. If both candidates are absent or copying fails, keep the existing `无法打开更新内容` status. Implement the temporary-bundle test helper in the test file by writing a minimal `Info.plist` plus the supplied resource map under a unique temporary directory, then constructing `Bundle(path:)`; remove the directory in `defer` after each lookup assertion.

- [ ] **Step 4: Replace the changelog source with exact localized histories**

Use this section order in all files: `# MarkLeaf Changelog`, then `## 1.1.6 — 2026-08-12`, `## 1.1.5 — 2026-08-12`, `## 1.1.4 — 2026-08-11`, and `## 1.1.3 — 2026-08-10`.

The 1.1.6 section must naturally translate these exact eight points in every file:

1. Configure externally opened files to use new windows or the current window, while activating an already-open file instead of duplicating it.
2. Prompt to save, discard/delete, or cancel when closing, replacing, or quitting with modified documents; auto-save only under the relevant setting and only after a successful write.
3. Add one-shot Format Painter to the Format and editor context menus.
4. Provide complete localized Markdown changelogs in four interface languages.
5. Localize the recovery dialog completely.
6. Align Outline font size and gray selection background with Workspace.
7. Make Outline selection appear on the first click.
8. Preserve the active Preferences page and frame when changing language.

Translate the existing 1.1.5, 1.1.4, and 1.1.3 bullets from `changelog.txt` without dropping or combining entries. Use the localized top headings `MarkLeaf 更新内容`, `MarkLeaf 更新內容`, `MarkLeaf Changelog`, and `MarkLeaf 更新履歴`.

- [ ] **Step 5: Copy the entire changelog directory during packaging**

Replace the single-file copy with:

```bash
if [ -d "$ROOT_DIR/Changelog" ]; then
  mkdir -p "$APP_CONTENTS/Resources/Changelog"
  cp -R "$ROOT_DIR/Changelog/." "$APP_CONTENTS/Resources/Changelog/"
fi
```

- [ ] **Step 6: Run changelog tests and verify GREEN**

Run the command from Step 2. Also run:

```bash
rg -n "1\.1\.[3-6]" macos/Changelog/changelog.*.md
test "$(find macos/Changelog -name 'changelog.*.md' | wc -l | tr -d ' ')" = "4"
test ! -e macos/Changelog/changelog.txt
```

Expected: Swift tests pass, every file contains all four versions, exactly four localized Markdown files exist, and the old text file is absent.

- [ ] **Step 7: Commit localized changelogs**

```bash
git add macos/Changelog macos/Sources/MarkLeaf/Services/ChangelogResource.swift \
  macos/Sources/MarkLeaf/App/AppWindowManager.swift \
  macos/script/build_and_run.sh \
  macos/Tests/MarkLeafTests/ChangelogMenuTests.swift
git commit -m "feat(macos): localize markdown changelogs"
```

---

### Task 8: Version 1.1.6, Full Verification, Build, Install, and Manual Acceptance

**Files:**
- Create: `macos/Sources/MarkLeaf/Support/AppVersion.swift`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift:210-215`
- Modify: `macos/script/build_and_run.sh:12-20, 75-82`
- Create: `macos/Tests/MarkLeafTests/ReleaseVersionTests.swift`
- Verify: all files changed in Tasks 1–7

**Interfaces:**
- Produces: `AppVersion.fallback = "1.1.6"`.
- Consumes: all preceding tasks.

- [ ] **Step 1: Write failing release metadata tests**

```swift
import XCTest
@testable import MarkLeaf

final class ReleaseVersionTests: XCTestCase {
    func testFallbackVersionIs116() {
        XCTAssertEqual(AppVersion.fallback, "1.1.6")
    }

    func testBuildScriptPackagesVersion116() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let script = try String(contentsOf: root.appendingPathComponent("script/build_and_run.sh"))
        XCTAssertTrue(script.contains("APP_VERSION=\"1.1.6\""))
        XCTAssertEqual(script.components(separatedBy: "<string>$APP_VERSION</string>").count - 1, 2)
        XCTAssertTrue(script.contains("--build-only|build-only"))
        XCTAssertFalse(script.contains("<string>1.1.5</string>"))
    }
}
```

- [ ] **Step 2: Run the release test and verify RED**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-release/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-release/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-release/scratch \
  --filter ReleaseVersionTests
```

Expected: compilation fails because `AppVersion` does not exist.

- [ ] **Step 3: Centralize and apply version 1.1.6**

Create:

```swift
enum AppVersion {
    static let fallback = "1.1.6"
}
```

Use `AppVersion.fallback` in `showAbout()`. Add `APP_VERSION="1.1.6"` near `APP_NAME` in the build script and use `$APP_VERSION` for both `CFBundleShortVersionString` and `CFBundleVersion`.

Add a non-launching build mode to the final `case` statement:

```bash
  --build-only|build-only)
    echo "[build] 已完成构建，不启动 $APP_NAME"
    ;;
```

Document `--build-only` in the script header and usage error. It must run the existing stop, resource preparation, Swift build, packaging, plist, and signing steps, then exit without calling `open`, allowing restricted CI/Codex environments to validate the bundle.

- [ ] **Step 4: Run the complete EditorWeb verification**

```bash
cd src/EditorWeb
./node_modules/.bin/vitest run
npm run build
cd ../..
```

Expected: all EditorWeb tests and the production build pass.

- [ ] **Step 5: Run the complete Swift test suite**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-116-full/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-116-full/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-116-full/scratch
```

Expected: all tests pass outside the Codex synthetic-event limitation. Under the restricted Codex sandbox, accept only the previously documented `WorkspaceTreeMouseInteractionTests.testRealAppKitClickOnMarkdownFileActivatesExactlyOnce` failure; investigate every other failure.

- [ ] **Step 6: Build the application bundle and verify its resources**

```bash
cd macos
./script/build_and_run.sh --build-only
plutil -extract CFBundleShortVersionString raw dist/MarkLeaf.app/Contents/Info.plist
plutil -extract CFBundleVersion raw dist/MarkLeaf.app/Contents/Info.plist
find dist/MarkLeaf.app/Contents/Resources/Changelog -maxdepth 1 -name 'changelog.*.md' -print | sort
cd ..
```

Expected: both `plutil` commands print `1.1.6`, and the bundle lists the four localized Markdown files.

- [ ] **Step 7: Install the verified bundle and compare it byte-for-byte**

```bash
pkill -x MarkLeaf >/dev/null 2>&1 || true
ditto macos/dist/MarkLeaf.app /Applications/MarkLeaf.app
plutil -extract CFBundleShortVersionString raw /Applications/MarkLeaf.app/Contents/Info.plist
plutil -extract CFBundleVersion raw /Applications/MarkLeaf.app/Contents/Info.plist
cmp macos/dist/MarkLeaf.app/Contents/MacOS/MarkLeaf \
  /Applications/MarkLeaf.app/Contents/MacOS/MarkLeaf
shasum -a 256 macos/dist/MarkLeaf.app/Contents/MacOS/MarkLeaf \
  /Applications/MarkLeaf.app/Contents/MacOS/MarkLeaf
```

Expected: both installed version fields print `1.1.6`, `cmp` exits 0, and both SHA-256 lines have the same digest.

- [ ] **Step 8: Perform document-safety manual acceptance**

Use disposable temporary files and verify:

- Saved modified document + auto-save off: close shows Save/Cancel/Don’t Save; Cancel leaves it open; Save writes then closes; Don’t Save closes without writing.
- Saved modified document + auto-save on: close writes successfully and closes without a sheet; a read-only destination reports the save error and remains open.
- Untitled modified document: close, replacement, and quit show Save…/Cancel/Delete; cancelling either the alert or save panel leaves the original operation cancelled.
- `⌘Q` with at least three modified windows prompts sequentially; cancelling the second keeps the app and all windows alive.
- Current-window replacement validates a missing or invalid UTF-8 target before showing any save/discard prompt and preserves the current document.

- [ ] **Step 9: Perform routing, painter, and localization manual acceptance**

- Default external mode: open three Markdown files from Finder together; receive three editor windows.
- Open one of those files again via Finder; its existing window activates and no duplicate appears.
- Current-window mode: the first file replaces the frontmost editor after the confirmed disposition flow; a second file from the same event opens in a new window.
- Explicit `文件 > 在新窗口中打开…` always creates a new window regardless of preference.
- Select uniformly formatted source text, invoke Format Painter from both entry points, select a valid target, and verify exactly one application; `Esc`, source mode, document switching, invalid list/table/image targets, and closing cancel the armed state.
- Confirm links retain their destination and one Undo restores the painted target.
- Switch through Simplified Chinese, Traditional Chinese, English, and Japanese; `更新内容` opens the matching Markdown history and Preferences stays on the same page and frame.
- About panel reports `1.1.6`.

- [ ] **Step 10: Inspect the final diff and commit the release metadata**

```bash
git diff --check
git status --short
git diff --stat HEAD~7..HEAD
git add macos/Sources/MarkLeaf/Support/AppVersion.swift \
  macos/Sources/MarkLeaf/App/AppWindowManager.swift \
  macos/script/build_and_run.sh \
  macos/Tests/MarkLeafTests/ReleaseVersionTests.swift
git commit -m "chore(macos): release version 1.1.6"
```

- [ ] **Step 11: Record final evidence**

Capture the final commit hash, Swift pass count, EditorWeb pass count, bundle version output, four bundled changelog names, and manual acceptance results in the execution handoff. Do not claim completion if any required check other than the single documented sandbox-only synthetic-click test is failing.
