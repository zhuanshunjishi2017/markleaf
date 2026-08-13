# Recovery Action Visibility and Color Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Show selected-snapshot actions only when a table row is selected and tone destructive controls closer to TextEdit's restrained red.

**Architecture:** Keep selection state as the single source of truth in `RecoveryWindowController.updateActionAvailability()`. Use semantic destructive behavior plus appearance-resolved TextEdit-style colors for the button title and bezel, with no delayed recoloring.

**Tech Stack:** Swift, AppKit, SwiftPM, standalone AppKit probes.

## Global Constraints

- “全部丢弃” remains visible regardless of selection.
- “保存”, “另存为…” and selected “丢弃” are hidden when no row is selected.
- Selected actions are shown only for a valid selected row; “保存” still requires an original path.
- Resolve colors from the current appearance; in dark mode match the supplied TextEdit reference (`#DC3929` title and `#5D3A38` bezel), while light mode remains system-adaptive.

---

### Task 1: Add failing visibility and color regression coverage

**Files:**
- Modify: `macos/Tests/Probes/RecoveryWindowActionsProbe.swift`

- [ ] **Step 1: Assert unselected actions are hidden and the destructive color is muted**

Add assertions immediately after the controller is shown:

```swift
precondition(controller.saveAsButton?.isHidden == true)
precondition(controller.discardSelectedButton?.isHidden == true)
let selectedTitleColor = controller.discardSelectedButton?.attributedTitle.attribute(
    .foregroundColor, at: 0, effectiveRange: nil
) as? NSColor
precondition(selectedTitleColor != NSColor.systemRed)
```

Expose `saveAsButton` from the controller for the probe in the same way as the existing button properties.

- [ ] **Step 2: Run the probe and verify it fails for the missing behavior**

Run the existing standalone probe compile/run command with the macOS 26.5 SDK. Expected failure: the controller still reports visible selected actions and uses exact `NSColor.systemRed`.

---

### Task 2: Implement selection-driven visibility and TextEdit-style destructive styling

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift`

- [ ] **Step 1: Hide selected actions by default**

Set `saveAsButton.isHidden = true` and `discardSelectedButton.isHidden = true` during construction. Keep the existing `saveOriginalButton.isHidden = true` default.

- [ ] **Step 2: Update all selected-action visibility from one helper**

In `updateActionAvailability()`, set:

```swift
saveAsButton?.isHidden = !selected
discardSelectedButton?.isHidden = !selected
saveAsButton?.isEnabled = selected
discardSelectedButton?.isEnabled = selected
```

Keep `saveOriginalButton?.isHidden = !hasPath` unchanged.

- [ ] **Step 3: Replace saturated red with appearance-resolved TextEdit colors**

Define dynamic colors that resolve to sRGB title `#DC3929` and bezel `#5D3A38` in dark appearance, with the existing softer system-red alpha treatment in light appearance. Apply the same title color to both destructive buttons while retaining `hasDestructiveAction = true`.

- [ ] **Step 4: Run the probe and verify it passes**

Recompile and run the AppKit probe. Expected: selected actions hidden before selection, visible/enabled after selection, and destructive title color is no longer exact full-opacity `systemRed`.

---

### Task 3: Build and verify the installed app

**Files:**
- No additional source files.

- [ ] **Step 1: Build with the project’s supported SDK/module cache settings**

Run `swift build --disable-sandbox` from `macos/` with SDKROOT set to `/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk` and the established temporary module caches.

- [ ] **Step 2: Run `git diff --check` and verify the worktree**

Confirm no whitespace errors and only the intended plan, probe, and controller changes are present.

- [ ] **Step 3: Install and exercise the real recovery window**

Use isolated recovery fixtures. Confirm no-selection state hides “另存为…”/“丢弃”, selecting a row reveals them, “全部丢弃” remains visible, and the destructive controls visually use the muted red style in the current appearance. Restore any pre-existing recovery data unchanged after the test.

- [ ] **Step 4: Commit the implementation**

```bash
git add macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift macos/Tests/Probes/RecoveryWindowActionsProbe.swift docs/superpowers/plans/2026-08-13-recovery-action-visibility-color.md
git commit -m "fix(macos): refine recovery action visibility and color"
```
