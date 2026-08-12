# macOS Document Lifecycle Reliability Design

## Goal

Repair the document lifecycle regressions found after the macOS 1.1.6 release: a MarkLeaf save must never be reported as an external edit, external-edit detection must survive atomic replacement and Save As, concurrent snapshot requests must not strand document operations, and save/discard prompts must match the approved horizontal AppKit sheet design.

## Scope

This repair is limited to the macOS document save pipeline, external-file change monitoring, document-disposition prompt presentation, and their tests. It preserves the existing 1.1.6 disposition rules, settings, localized copy, and Markdown file format.

## Save and Snapshot Coordination

EditorSession will replace its single `pendingSnapshot` callback slot with a FIFO snapshot request queue. Each request captures one completion closure; a received editor snapshot completes exactly the oldest queued request. Requests made by manual save, periodic auto-save, close, replacement, termination, export, or other callers therefore cannot overwrite one another.

Document writes are serialized per EditorSession. If a write is in progress, later write requests wait in order. Each queued write takes its own editor snapshot only after the preceding write finishes, writes that snapshot, and completes only its own callback. A failed snapshot or write completes that request with failure, does not mark the document clean, and then allows the next queued write to begin.

## Self-Writes and External Changes

MarkLeaf continues to use atomic writes to prevent corrupting a document on interruption. It will no longer observe a file descriptor through its own atomic replacement.

Before writing an already-open document, the session stops its file watch. After a successful atomic write it records the new path's file version, restarts the watch against the replacement file, and then completes the save. This prevents the old descriptor's delete event from presenting an external-change alert.

The file watcher owns a normalized document URL and reports both its event mask and current file version to the session. A file version consists of the `stat` device number, inode, nanosecond modification time, and byte size. On delete, rename, or revoke events where the path still exists, the session immediately rebinds to the file now present at that path before deciding whether the event is external. An event is ignored only when the current version equals the last version accepted by MarkLeaf after loading, saving, reloading, or explicitly ignoring an external change. No time-window suppression is used.

For an actual external modification, the session presents one prompt at a time. Choosing Reload reads the current file, loads it, and installs a new watch. Choosing Ignore retains the current in-memory document and also rebinds the watch to the current file identity so future changes remain observable. A missing file keeps the existing `文件已被外部删除` status behavior.

Saving a previously untitled document must start a file watch after its first successful write. Saving an existing document must leave exactly one active watch attached to the replacement file.

## Document Disposition Sheets

Replace the direct `NSAlert` use for modified saved and untitled document disposition with a dedicated AppKit sheet presenter. The presenter attaches to the editor window and lays out an icon, localized title and detail copy, plus horizontal native `NSButton` controls in this order:

- Saved document: `不保存`, `取消`, `保存`.
- Untitled document: `删除`, `取消`, `保存…`.

The trailing Save or Save As button is the default action. Cancel remains the Escape action. The presenter supplies semantic choices to DocumentDispositionCoordinator; it does not contain saving, document replacement, closing, or termination logic.

Only an explicit discard/delete button press may discard content. Sheet cancellation, Escape, window teardown, or an unknown modal response map to Cancel.

Other alerts, including the external-modification alert, are outside this visual repair unless they share the same disposition-sheet presenter.

## Tests

Add deterministic tests for the extracted coordinator and presenter-facing mapping:

- Snapshot requests complete FIFO and one request cannot replace another request's completion.
- Save requests are serialized and a failed save leaves the document dirty.
- A MarkLeaf atomic save suppresses its own change event, rebinds the watch, and does not present an external-change prompt.
- An external atomic replacement is detected after the session's own save.
- Ignore after an external atomic replacement retains monitoring for a later modification.
- First Save As starts monitoring.
- Saved and untitled sheet button specifications have the approved horizontal order, default action, cancel action, and safe unknown-response mapping.

Run focused Swift tests, then package and manually verify: edit an existing document, press Command-S, confirm no external-change prompt; edit the same file from another application, confirm one reload prompt; ignore it, edit it externally again, confirm a second prompt; save a new document, then externally edit it; and inspect both close/disposition sheets.

## Out of Scope

- Migrating to NSDocument, NSFilePresenter, or document version browsing.
- Changing auto-save or document-switch preferences.
- Changing the external-modification prompt's text or action semantics.
- Changing version number or changelog content.
