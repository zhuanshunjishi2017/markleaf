# Format Painter Caret-Paragraph Design

## Goal

Complete format painter behavior when the source or target has no selected
text: use the caret position's inline format together with its paragraph or
heading type as the source, and apply that format to the entire paragraph at a
caret target while preserving the caret.

## Approved Semantics

- Source with a text selection: keep the existing behavior and require one
  paintable block with uniform supported marks.
- Source with no selection: capture the current paragraph/heading type and the
  inline marks at the caret position. Prefer the marks immediately before the
  caret, then immediately after it, then the editor's stored/active marks.
- Target with a text selection: apply the captured block type and marks only to
  that selection.
- Target with no selection: apply the captured block type and marks to the
  entire current paragraph/heading, then restore the original caret position.
  Empty paragraphs must retain the captured marks as typing attributes.
- Invalid list/table/image targets remain rejected and leave the painter armed.
- A successful paint disarms the one-shot painter.

## Implementation Boundary

The behavior belongs in `src/EditorWeb/src/format-painter.ts`. Tests live in
`src/EditorWeb/tests/format-painter.test.ts`; no native Swift protocol changes
are required because the existing `formatPainter` command already preserves
the editor selection and routes to the controller.

## Verification

- Add tests for a caret source inside a marked run, caret source at the end of
  a marked run, caret target over a full paragraph, and an empty caret target
  whose stored marks are applied to newly typed text.
- Run the complete EditorWeb Vitest suite and production build.
- Run a real installed-app smoke check that the format painter command remains
  available with a caret and that painting a caret target changes the whole
  paragraph without changing its text.
