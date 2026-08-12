# Windows 1.1.5 Features and Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add CJK glyph preference, consolidated font settings, Focus Mode, shared style changes, and release metadata for macOS 1.1.5.

**Architecture:** Persist CJK language as a strongly typed BCP-47 value and apply it with editor CSS variables. Keep Preferences as the owner of a dedicated modal font controller. Focus Mode is temporary per-window controller state and never mutates saved sidebar/status choices.

**Tech Stack:** Swift 6, AppKit, WebKit JavaScript bridge, CSS, XCTest, SwiftPM

## Global Constraints

- Supported CJK values are exactly `zh-Hans`, `zh-Hant`, `ja`, and `ko`.
- F11 toggles Focus Mode and Escape exits it.
- Focus Mode restores the pre-focus sidebar/status presentation and leaves persisted settings unchanged.
- Bundle version is exactly `1.1.5`.

---

### Task 1: CJK Preference and Font Dialog

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/AppSettings.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Create: `macos/Sources/MarkLeaf/Views/FontSettingsWindowController.swift`
- Modify: `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Test: `macos/Tests/MarkLeafTests/CjkGlyphPreferenceTests.swift`
- Test: `macos/Tests/MarkLeafTests/PreferencesParityTests.swift`

**Interfaces:**
- Produces: `CJKLanguageTag: String, Codable, CaseIterable`, `AppSettings.cjkLanguageTag`, and a font dialog returning CJK font, Western font, and source font size.

- [ ] Add decode/round-trip tests for all four tags and a JavaScript-builder test asserting literal `lang` and `--ml-cjk-lang` output.
- [ ] Run focused tests and observe missing-type/behavior failures.
- [ ] Add the enum/setting and extend `applyVisualVariables` to set the root language and CSS variable.
- [ ] Add the modal font controller, replace the three inline source-font controls with a summary/button, and persist accepted values only.
- [ ] Add all four localizations and run localization coverage plus Preferences tests until green.
- [ ] Commit as `feat(macos): add CJK and font preferences`.

### Task 2: Per-Window Focus Mode

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/EditorWindowController.swift`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift`
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Test: `macos/Tests/MarkLeafTests/FocusModeTests.swift`

**Interfaces:**
- Produces: `EditorWindowController.toggleFocusMode()`, `exitFocusMode()`, `isFocusMode`, and menu command `toggleFocusMode`.

- [ ] Add controller-state tests proving enter hides temporary chrome, exit restores it, and persisted visibility values are unchanged.
- [ ] Run focused tests and observe failure because Focus Mode does not exist.
- [ ] Add the View menu command with F11, per-window temporary state, Escape interception, and reversible presentation options.
- [ ] Refresh menu state and remove the event monitor on exit/window close.
- [ ] Run focused and existing sidebar/menu tests until green.
- [ ] Commit as `feat(macos): add focus mode`.

### Task 3: Styles, Outline, Version, and Full Verification

**Files:**
- Modify: `src/MarkLeaf/Resources/Styles/base.css`
- Modify: `src/MarkLeaf/Resources/Styles/sans.css`
- Modify: `src/MarkLeaf/Resources/Styles/colors-morandi.css`
- Modify: `src/MarkLeaf/Resources/Changelog/changelog.txt`
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift`
- Modify: `macos/script/build_and_run.sh`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift`
- Test: `macos/Tests/MarkLeafTests/Release115ParityTests.swift`

**Interfaces:**
- Produces: macOS bundle version `1.1.5` and the Windows 1.1.5 user-visible style changes.

- [ ] Add behavior tests reading staged resource behavior through `StyleManager`, asserting Outline rows use the 1.1.5 presentation, and asserting the built version source resolves to 1.1.5.
- [ ] Run focused tests and observe failures against 1.1.3/current styling.
- [ ] Apply the exact shared CSS/Morandi changes, neutral Outline selection with 9pt typography, 1.1.5 changelog, and version metadata.
- [ ] Run the full Swift suite and frontend suite with zero failures.
- [ ] Build with `macos/script/build_and_run.sh --verify`, strictly verify the signature, install to `/Applications/MarkLeaf.app`, relaunch it, and test every acceptance path directly.
- [ ] Commit as `feat(macos): release Windows parity 1.1.5`.
