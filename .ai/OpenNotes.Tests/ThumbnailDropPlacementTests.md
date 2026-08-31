# ThumbnailDropPlacementTests

> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Table-driven regression coverage for thumbnail before/after placement and its
conversion to the PDF service's final page index.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** prove upward, downward, final/end, adjacent no-op, and clamped
  placement behavior before sidebar drag/drop integration.
- **Next steps:** run RED with the missing model, implement the pure helper,
  then rerun the focused class GREEN.

## Constraints

- Tests must remain pure and must not construct WPF controls or write PDFs.
- The expected index is zero-based and is evaluated after removing the source
  page from the original list.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-30 | Added the planned placement contract mirror before test-first implementation. | Codex |
