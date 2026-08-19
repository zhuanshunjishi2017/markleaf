# macOS Windows 1.2.5 Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sync all applicable Windows 1.2.5 capabilities into the macOS application and publish the code without creating a GitHub Release.

**Architecture:** Rebase the existing macOS Dock/icon branch onto remote `main`, then add small pure Swift models for host protocol decisions, menu behavior, and export persistence. AppKit controllers consume those models, while shared editor and theme changes are taken directly from `packages/editor-web` and `packages/styles`.

**Tech Stack:** Swift 6/AppKit/WebKit, TypeScript/Vite, shell-based Swift regression tests, Git/GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-08-19-macos-windows-1.2.5-sync-design.md`

## Global Constraints

- Version remains `1.2.5`.
- Do not create a GitHub tag or Release.
- Preserve all Windows 1.2.5 commits already on remote `main`.
- Preserve the existing macOS Dock lifecycle and icon safe-area work.
- Shared editor behavior and themes must be consumed from `packages/`, not copied into macOS source.

---

### Task 1: Synchronize the feature branch with remote main

**Files:**
- Modify through merge/rebase: repository history only
- Preserve: all current macOS modified and untracked files

**Interfaces:**
- Consumes: remote `main` commit containing Windows 1.2.5
- Produces: feature branch based on current remote `main`

- [ ] **Step 1: Verify and commit the completed Dock/icon baseline**

Run the lifecycle policy test, icon verifier, and `git diff --check`, then commit only the existing Dock lifecycle, icon, version, changelog, and regression-test files.

- [ ] **Step 2: Fetch and rebase onto `origin/main`**

Run `git fetch origin` and `git rebase origin/main`; resolve version/changelog conflicts by retaining macOS 1.2.5 Dock/icon entries alongside remote Windows/shared changes.

- [ ] **Step 3: Verify ancestry**

Run `git merge-base --is-ancestor origin/main HEAD` and confirm success.

### Task 2: Unsafe emphasis host bridge

**Files:**
- Create: `apps/macos/Sources/MarkLeaf/Services/UnsafeEmphasisPolicy.swift`
- Create: `apps/macos/Sources/MarkLeaf/Views/UnsafeEmphasisDialog.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/AppSettings.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/L10n.swift`
- Test: `apps/macos/script/tests/UnsafeEmphasisPolicyTest.swift`
- Test: `apps/macos/script/tests/unsafe-emphasis-policy-test.sh`

**Interfaces:**
- Consumes: frontend message `unsafeEmphasisRequested` with request id and kind
- Produces: host message `unsafeEmphasisResponse` with action `literal` or `html`

- [ ] **Step 1: Write a failing policy test**

Test that bold/italic requests map to localized dialog descriptions, a saved preference bypasses the dialog, and responses preserve the request id and allowed action.

- [ ] **Step 2: Run the test and observe the missing-type failure**

- [ ] **Step 3: Implement the pure policy and native dialog**

Use `NSAlert` with literal/HTML choices, a “do not ask again” checkbox, and a help link. Persist the default action in `AppSettings`.

- [ ] **Step 4: Wire the protocol message in `EditorSession`**

Handle `unsafeEmphasisRequested` and reply with `send("unsafeEmphasisResponse", payload: ["action": action], requestId: requestID)`.

- [ ] **Step 5: Run the policy test**

### Task 3: Clipboard and read-only menus

**Files:**
- Create: `apps/macos/Sources/MarkLeaf/Services/EditorMenuPolicy.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession+Clipboard.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/L10n.swift`
- Test: `apps/macos/script/tests/EditorMenuPolicyTest.swift`
- Test: `apps/macos/script/tests/editor-menu-policy-test.sh`

**Interfaces:**
- Produces: `pastePlainTextFromClipboard()` and menu policy for editable/source/read-only contexts

- [ ] **Step 1: Write failing tests for menu policy**

Assert that read-only contexts expose copy/select-all but no mutating commands, and editable contexts include paste-as-plain-text.

- [ ] **Step 2: Run the tests and observe failure**

- [ ] **Step 3: Implement plain-text paste and menu policy**

Read only `NSPasteboard.PasteboardType.string`, send `pasteText`, and expose Command-Shift-V.

- [ ] **Step 4: Apply the policy to main and context menus**

Rename the copy submenu to “Copy/Paste As”, add plain-text paste, and restrict read-only menus.

- [ ] **Step 5: Run the menu policy test**

### Task 4: Export settings parity

**Files:**
- Create: `apps/macos/Sources/MarkLeaf/Services/PersistedExportSettings.swift`
- Create: `apps/macos/Sources/MarkLeaf/Services/PDFHeaderFooterPolicy.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Views/ExportWindowController.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/EditorSession+Export.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/PDFGenerator.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`
- Modify: `apps/macos/Sources/MarkLeaf/Services/L10n.swift`
- Test: `apps/macos/script/tests/PersistedExportSettingsTest.swift`
- Test: `apps/macos/script/tests/persisted-export-settings-test.sh`

**Interfaces:**
- Produces: Codable persisted settings, `exportWithLastSettings()`, and resolved header/footer content/alignment

- [ ] **Step 1: Write failing settings round-trip tests**

Cover defaults, Codable round-trip, invalid-value normalization, page placeholders, and header/footer alignment.

- [ ] **Step 2: Run the tests and observe missing-type failures**

- [ ] **Step 3: Implement persisted settings and header/footer policy**

Store settings in `UserDefaults`, normalize enum-like strings, and resolve `{title}`, `{page}`, and `{pages}` placeholders.

- [ ] **Step 4: Restore and save settings in the export window**

Restore every control at window creation, save on export, add preset/custom header and footer controls, and retain the current document style/theme as the first-run default.

- [ ] **Step 5: Add Export with Last Settings**

Add a native File menu item that skips the dialog and exports using persisted format/options, prompting only for the destination.

- [ ] **Step 6: Match PDF header/footer rendering**

Use style-derived font family, 0.875 body font size, alignment-specific margin boxes, and 6 mm offsets.

- [ ] **Step 7: Run export settings tests**

### Task 5: Shared frontend/theme integration and full verification

**Files:**
- Verify: `packages/editor-web/**`
- Verify: `packages/styles/colors-saltlemon.css`
- Verify/build: `apps/macos/**`

**Interfaces:**
- Consumes: remote Windows 1.2.5 shared editor/style commit
- Produces: signed macOS 1.2.5 app bundle

- [ ] **Step 1: Run shared editor tests**

Run `pnpm --dir packages/editor-web test`.

- [ ] **Step 2: Run all macOS standalone regression tests**

Run every `apps/macos/script/tests/*-test.sh` script with the matching Xcode toolchain and writable module cache.

- [ ] **Step 3: Build the macOS app**

Run `apps/macos/script/build_and_run.sh --build-only`, clear bundle xattrs, re-sign ad hoc, and verify with `codesign --verify --deep --strict`.

- [ ] **Step 4: Verify version, icon, shared theme, and protocol strings**

Confirm version `1.2.5`, icon strong-alpha bounds 81%-82%, `colors-saltlemon.css` is bundled, and the unsafe-emphasis protocol appears in both frontend and Swift host.

- [ ] **Step 5: Commit and push code only**

Commit the completed sync, push the feature branch, and open a PR against `main`; do not create a tag or Release.
