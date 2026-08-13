# Native Destructive Save-Prompt Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace MarkLeaf's delayed, hard-coded red save-prompt styling with AppKit's native destructive-action button while preserving prompt layout and behavior.

**Architecture:** Keep `DocumentDispositionSheetPresenter` as the presentation and response-mapping boundary. Extract construction of the two `NSAlert` variants into internal factory methods used by both production presentation and a focused AppKit probe, then set `hasDestructiveAction` before presentation and delete every manual color override.

**Tech Stack:** Swift 5, AppKit (`NSAlert`, `NSButton.hasDestructiveAction`), SwiftPM, a standalone Swift probe, and macOS Computer Use for live UI verification.

## Global Constraints

- Keep the existing compact `NSAlert` layout, wording, application icon, button order, keyboard equivalents, response mapping, and close-after-sheet behavior.
- Only “不保存” and “删除” receive destructive-action semantics.
- Do not hard-code destructive-button colors.
- Do not schedule post-presentation destructive-button restyling.
- The minimum deployment target remains macOS 13.0; `hasDestructiveAction` is available from macOS 11.0.
- Use `/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk` for local verification because the default 27.0 SDK is newer than the installed Swift compiler.

---

### Task 1: Native AppKit alert configuration

**Files:**
- Create: `macos/Tests/Probes/DocumentDispositionSheetProbe.swift`
- Modify: `macos/Tests/MarkLeafTests/DocumentDispositionSheetTests.swift`
- Modify: `macos/Sources/MarkLeaf/Views/DocumentDispositionSheet.swift`

**Interfaces:**
- Consumes: `L10n.t(_:)`, `L10n.f(_:_:)`, `SavedDocumentChoice`, and `UntitledDocumentChoice` already used by the presenter.
- Produces: `DocumentDispositionSheetPresenter.makeSavedAlert(filename:) -> NSAlert` and `DocumentDispositionSheetPresenter.makeUntitledAlert() -> NSAlert`; both are internal so production and tests exercise the same alert construction.

- [ ] **Step 1: Write the failing standalone AppKit probe**

Create `macos/Tests/Probes/DocumentDispositionSheetProbe.swift` with test-local definitions for the presenter's unrelated localization and choice dependencies, then inspect the real alerts built by the production presenter:

```swift
import AppKit
import Foundation

enum SavedDocumentChoice { case save, discard, cancel }
enum UntitledDocumentChoice { case saveAs, delete, cancel }

enum L10n {
    static func t(_ text: String) -> String { text }
    static func f(_ format: String, _ args: CVarArg...) -> String {
        String(format: format, arguments: args)
    }
}

@main
enum DocumentDispositionSheetProbe {
    static func main() {
        _ = NSApplication.shared

        let saved = DocumentDispositionSheetPresenter.makeSavedAlert(filename: "notes.md")
        precondition(saved.buttons.map(\.title) == ["保存", "取消", "不保存"])
        precondition(saved.buttons.map(\.hasDestructiveAction) == [false, false, true])
        precondition(saved.buttons[1].keyEquivalent == "\u{1b}")

        let untitled = DocumentDispositionSheetPresenter.makeUntitledAlert()
        precondition(untitled.buttons.map(\.title) == ["保存…", "取消", "删除"])
        precondition(untitled.buttons.map(\.hasDestructiveAction) == [false, false, true])
        precondition(untitled.buttons[1].keyEquivalent == "\u{1b}")
    }
}
```

Production change that makes this test pass: adding the two alert factory methods and marking exactly their third buttons as destructive. Omitting the destructive flag, applying it to the wrong button, or changing the approved order makes the probe fail.

- [ ] **Step 2: Run the probe and verify RED**

Run from `macos/`:

```bash
swiftc \
  -sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
  -module-cache-path /private/tmp/markleaf-destructive-probe-module-cache \
  Sources/MarkLeaf/Views/DocumentDispositionSheet.swift \
  Tests/Probes/DocumentDispositionSheetProbe.swift \
  -o /private/tmp/markleaf-document-disposition-probe
```

Expected: compilation fails because `makeSavedAlert(filename:)` and `makeUntitledAlert()` do not exist. This proves the probe detects the missing production contract rather than a framework or test-double behavior.

- [ ] **Step 3: Implement the minimal native alert factories**

Refactor `presentSaved` and `presentUntitled` to obtain their alerts from these methods:

```swift
static func makeSavedAlert(filename: String) -> NSAlert {
    let alert = NSAlert()
    alert.alertStyle = .warning
    alert.icon = NSApp.applicationIconImage
    alert.messageText = L10n.f("是否保存对“%@”的修改？", filename)
    alert.informativeText = L10n.t("如果不保存，您的更改将会丢失。")
    alert.addButton(withTitle: L10n.t("保存"))
    alert.addButton(withTitle: L10n.t("取消"))
    alert.addButton(withTitle: L10n.t("不保存"))
    alert.buttons[1].keyEquivalent = "\u{1b}"
    alert.buttons[2].hasDestructiveAction = true
    return alert
}

static func makeUntitledAlert() -> NSAlert {
    let alert = NSAlert()
    alert.alertStyle = .warning
    alert.icon = NSApp.applicationIconImage
    alert.messageText = L10n.t("是否保存此文档？")
    alert.informativeText = L10n.t("如果不保存，这个文档将被删除。")
    alert.addButton(withTitle: L10n.t("保存…"))
    alert.addButton(withTitle: L10n.t("取消"))
    alert.addButton(withTitle: L10n.t("删除"))
    alert.buttons[1].keyEquivalent = "\u{1b}"
    alert.buttons[2].hasDestructiveAction = true
    return alert
}
```

Delete `styleDestructive(_:)`, both pre-presentation calls to that helper, and the `DispatchQueue.main.async` block after `beginSheetModal`. Do not change the response switches or the single-completion guard.

- [ ] **Step 4: Run the standalone probe and verify GREEN**

Run from `macos/`:

```bash
swiftc \
  -sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
  -module-cache-path /private/tmp/markleaf-destructive-probe-module-cache \
  Sources/MarkLeaf/Views/DocumentDispositionSheet.swift \
  Tests/Probes/DocumentDispositionSheetProbe.swift \
  -o /private/tmp/markleaf-document-disposition-probe
/private/tmp/markleaf-document-disposition-probe
```

Expected: compilation succeeds and the probe exits with status 0.

- [ ] **Step 5: Replace the obsolete XCTest coverage with the shared factory contract**

Rewrite `macos/Tests/MarkLeafTests/DocumentDispositionSheetTests.swift` so a future full-Xcode run checks the same real production factories:

```swift
import XCTest
@testable import MarkLeaf

final class DocumentDispositionSheetTests: XCTestCase {
    func testSavedAlertMarksOnlyDontSaveAsDestructive() {
        let alert = DocumentDispositionSheetPresenter.makeSavedAlert(filename: "notes.md")

        XCTAssertEqual(alert.buttons.map(\.title), [L10n.t("保存"), L10n.t("取消"), L10n.t("不保存")])
        XCTAssertEqual(alert.buttons.map(\.hasDestructiveAction), [false, false, true])
        XCTAssertEqual(alert.buttons[1].keyEquivalent, "\u{1b}")
    }

    func testUntitledAlertMarksOnlyDeleteAsDestructive() {
        let alert = DocumentDispositionSheetPresenter.makeUntitledAlert()

        XCTAssertEqual(alert.buttons.map(\.title), [L10n.t("保存…"), L10n.t("取消"), L10n.t("删除")])
        XCTAssertEqual(alert.buttons.map(\.hasDestructiveAction), [false, false, true])
        XCTAssertEqual(alert.buttons[1].keyEquivalent, "\u{1b}")
    }
}
```

The current CommandLineTools installation does not ship a resolvable `XCTest` module, so record that environment limitation rather than claiming the full suite ran. The standalone probe is the executable regression gate for this change.

- [ ] **Step 6: Build the complete macOS executable**

Run from `macos/`:

```bash
SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
CLANG_MODULE_CACHE_PATH=/private/tmp/markleaf-clang-module-cache \
SWIFTPM_MODULECACHE_OVERRIDE=/private/tmp/markleaf-swiftpm-module-cache \
swift build --disable-sandbox
```

Expected: `Build complete!`. Pre-existing localization, deprecation, and unused-result warnings may remain; no new warning may originate from the changed files.

- [ ] **Step 7: Inspect the focused diff and commit**

Run:

```bash
git diff --check
git diff -- macos/Sources/MarkLeaf/Views/DocumentDispositionSheet.swift macos/Tests/MarkLeafTests/DocumentDispositionSheetTests.swift macos/Tests/Probes/DocumentDispositionSheetProbe.swift
git add macos/Sources/MarkLeaf/Views/DocumentDispositionSheet.swift macos/Tests/MarkLeafTests/DocumentDispositionSheetTests.swift macos/Tests/Probes/DocumentDispositionSheetProbe.swift
git commit -m "fix(macos): use native destructive save-prompt actions"
```

Expected: the diff contains no `bezelColor`, `attributedTitle`, hard-coded red components, or post-presentation styling queue.

---

### Task 2: Installed-app and first-frame verification

**Files:**
- Verify only: `macos/dist/MarkLeaf.app`
- Verify installed artifact: `/Applications/MarkLeaf.app`

**Interfaces:**
- Consumes: the native alert factories committed in Task 1 and the existing document-disposition/close workflow.
- Produces: evidence for the final report covering untitled, saved, first-frame, dark-appearance, and one-click close behavior.

- [ ] **Step 1: Package and install the new build**

Run from the repository's `macos/` directory. The explicit build command keeps
SwiftPM out of the restricted package sandbox while using the compiler-matched
SDK:

```bash
./script/prepare_resources.sh
SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
CLANG_MODULE_CACHE_PATH=/private/tmp/markleaf-clang-module-cache \
SWIFTPM_MODULECACHE_OVERRIDE=/private/tmp/markleaf-swiftpm-module-cache \
swift build --disable-sandbox
```

Then run the existing packaging script in build-only mode, which copies the
fresh binary and resources into `macos/dist/MarkLeaf.app`:

```bash
SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
CLANG_MODULE_CACHE_PATH=/private/tmp/markleaf-clang-module-cache \
SWIFTPM_MODULECACHE_OVERRIDE=/private/tmp/markleaf-swiftpm-module-cache \
./script/build_and_run.sh --build-only
```

Preserve the installed app before replacing it:

```bash
MARKLEAF_BACKUP="/private/tmp/MarkLeaf.app.backup-$(date +%H%M%S)"
mv /Applications/MarkLeaf.app "$MARKLEAF_BACKUP"
cp -R dist/MarkLeaf.app /Applications/MarkLeaf.app
codesign --verify --deep --strict /Applications/MarkLeaf.app
stat -f "%Sm %N" /Applications/MarkLeaf.app/Contents/MacOS/MarkLeaf
```

If `open` or `/Applications` is denied by the sandbox, rerun the same scoped
packaging/install operation with the user's approval for the existing app.

- [ ] **Step 2: Verify the untitled-document prompt through the real UI**

Launch the installed app, type text into a new document, and close the window. Confirm through fresh accessibility state that the alert exposes “保存…”, “删除”, and “取消” once each. Visually compare the destructive button with TextEdit under the same light appearance and confirm no white-to-red transition occurs while the sheet opens.

- [ ] **Step 3: Verify one-click deletion and close timing**

Click “删除” once. Refresh application state and confirm the dirty editor window is closed without a second click; distinguish any recovery window from the editor before judging the result.

- [ ] **Step 4: Verify the saved-document prompt**

Open a disposable Markdown file, edit it, close the window, and confirm “不保存” receives the same native destructive treatment. Click “不保存” once and verify the editor closes and the disposable file's original contents are unchanged.

- [ ] **Step 5: Verify appearance adaptation**

Repeat prompt inspection under dark appearance without changing production color values. Confirm the button remains legible and system-styled, then restore the user's previous appearance setting.

- [ ] **Step 6: Final audit**

Run:

```bash
git status --short --branch
git log -2 --oneline
```

Report the focused probe result, full executable build result, installed-app UI results, exact commit, and the pre-existing `XCTest`/CommandLineTools limitation. Do not claim the full XCTest suite passed.
