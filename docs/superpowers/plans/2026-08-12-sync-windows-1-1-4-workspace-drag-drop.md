# Windows 1.1.4 Workspace Drag and Drop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Support native internal workspace moves and copying workspace entries to Finder.

**Architecture:** `WorkspaceTreeView` becomes an outline drag source/destination using file URLs plus a private local-drag pasteboard type. A pure `WorkspaceMovePolicy` validates destinations, while `EditorSession` owns filesystem mutation and open-document watcher updates.

**Tech Stack:** Swift 6, AppKit drag/drop, FileManager, XCTest, SwiftPM

## Global Constraints

- Local drops move; drops into Finder copy.
- Never overwrite an existing destination.
- Never allow a directory to move inside itself or a descendant.
- Preserve open-document dirty content and update its URL/watch state after a successful move.

---

### Task 1: Move Validation and Filesystem Mutation

**Files:**
- Create: `macos/Sources/MarkLeaf/Services/WorkspaceMovePolicy.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Test: `macos/Tests/MarkLeafTests/WorkspaceMovePolicyTests.swift`

**Interfaces:**
- Produces: `WorkspaceMovePolicy.destination(source:targetDirectory:workspaceRoot:fileManager:) throws -> URL` and `EditorSession.moveWorkspaceEntry(from:toDirectory:) throws`.

- [ ] Add temporary-directory tests for valid file/folder destinations, same-parent rejection, collision rejection, outside-workspace rejection, and descendant rejection.
- [ ] Run focused tests and observe failure because `WorkspaceMovePolicy` is missing.
- [ ] Implement standardized/resolved-path validation and `FileManager.moveItem` routing in `EditorSession`.
- [ ] If the moved source is the current document, stop its watcher, update `documentURL` and status text, then restart the watcher without clearing editor content.
- [ ] Re-run focused tests until green.
- [ ] Commit as `feat(macos): validate workspace moves`.

### Task 2: Native Outline Drag Source and Destination

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/SidebarView.swift`
- Test: `macos/Tests/MarkLeafTests/WorkspaceDragDropTests.swift`

**Interfaces:**
- Consumes: `EditorSession.moveWorkspaceEntry(from:toDirectory:)`.
- Produces: local `.move` source mask, external `.copy` source mask, a private pasteboard marker, and outline destination validation for folder/root rows.

- [ ] Add tests for source operation masks, file-URL pasteboard output, valid folder/root targeting, and rejected file/self targets.
- [ ] Run focused tests and observe failure because the outline has no drag registrations/delegate methods.
- [ ] Register drag types, publish `NSURL` plus the private source path, implement source masks, validate proposed targets, and accept valid local drops.
- [ ] Use AppKit's native drop indicator and reload the workspace after successful moves; surface filesystem errors with a localized alert.
- [ ] Run drag/drop and all Workspace interaction tests until green.
- [ ] Commit as `feat(macos): add native workspace drag and drop`.
