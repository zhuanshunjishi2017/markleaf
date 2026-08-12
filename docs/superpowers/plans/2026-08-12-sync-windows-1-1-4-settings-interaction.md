# Windows 1.1.4 Settings and Interaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add configurable system light/dark defaults, sidebar-tab persistence, and mouse-up workspace file opening.

**Architecture:** Extend `AppSettings` with backward-compatible defaults, pass preferred theme identifiers through `StyleManager`, persist tab changes at the session boundary, and move file activation from outline selection to `WorkspaceTreeView.mouseUp(with:)`.

**Tech Stack:** Swift 6, AppKit, Codable, XCTest, SwiftPM

## Global Constraints

- Preserve the existing follow-system toggle and theme fallback behavior.
- Preserve native selection, gray Workspace selection geometry, and directory-name double-click disclosure animation.
- Do not open files from `shouldSelectItem`.

---

### Task 1: Persisted Theme Defaults and Sidebar Tab

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/AppSettings.swift`
- Modify: `macos/Sources/MarkLeaf/Services/StyleManager.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift`
- Test: `macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift`
- Test: `macos/Tests/MarkLeafTests/StyleManagerThemeDefaultsTests.swift`

**Interfaces:**
- Produces: `AppSettings.defaultLightThemeID`, `AppSettings.defaultDarkThemeID`, and `StyleManager.defaultThemeID(forDark:preferredLight:preferredDark:)`.

- [ ] Write decoding/round-trip tests for both preferred IDs and `sidebarTab`, plus resolver tests proving valid custom IDs win and missing IDs fall back.
- [ ] Run the focused tests and observe failures caused by missing fields/signature.
- [ ] Add the Codable defaults and resolver parameters; pass them from every follow-system resolution call.
- [ ] Save `sidebarTab` as `workspace` or `outline` when the visible tab changes.
- [ ] Re-run focused tests and the existing sidebar tests until green.
- [ ] Commit as `feat(macos): sync 1.1.4 theme and sidebar state`.

### Task 2: Preferences Theme Defaults

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`
- Test: `macos/Tests/MarkLeafTests/PreferencesParityTests.swift`

**Interfaces:**
- Consumes: the settings fields from Task 1 and `ColorThemeInfo.isDark`.
- Produces: light/dark default popups that save valid IDs and remain enabled only while follow-system mode is on.

- [ ] Add a test constructing Preferences with light and dark fixtures and asserting each popup is filtered and selects the persisted value.
- [ ] Run the test and observe failure because the controls do not exist.
- [ ] Add two popups, place them under the follow-system row, and persist their selected IDs in `controlChanged()`.
- [ ] Re-run the focused Preferences test and settings tests until green.
- [ ] Commit as `feat(macos): choose system appearance themes`.

### Task 3: Open Workspace Files on Mouse Up

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift`
- Modify: `macos/Tests/MarkLeafTests/WorkspaceTreeMouseInteractionTests.swift`

**Interfaces:**
- Produces: `WorkspaceTreeView.activateWorkspaceEntry(_:)`, called only by a left-button mouse-up over a non-directory entry.

- [ ] Add a probe subclass test proving selection alone does not activate and one synthetic mouse-up test proving a Markdown row activates exactly once.
- [ ] Run both tests and observe the pre-change activation timing failure.
- [ ] Remove file opening from `shouldSelectItem`, override `mouseUp(with:)`, and route activation through the overridable helper.
- [ ] Run all Workspace interaction tests and verify directory single/double-click behavior remains green.
- [ ] Commit as `fix(macos): open workspace files on mouse release`.
