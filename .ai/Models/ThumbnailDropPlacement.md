# ThumbnailDropPlacement

## Sidebar page reorder (2026-08-30) — GREEN for focused scope

- Production consumers resolve an original-list insertion slot (including the end slot) and use the source-removal-aware final index returned by this pure helper. The row/before-after and direct-slot overloads remain deterministic and side-effect free.
- Focused placement coverage passes in ThumbnailDropPlacementTests.

> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Pure conversion helpers for turning a thumbnail row placement (before/after)
or an insertion slot into the final zero-based page index after the dragged
source page is removed.

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** cover upward, downward, end-slot, and no-op placement semantics
  before the sidebar drag/drop wiring consumes the helper.
- **Next steps:** add the model after the RED tests fail, then synchronize this
  mirror with the final API and focused test result.

## Important Notes / NEVER Change

- The returned index is the destination index after source removal and is the
  same convention consumed by `PdfService.ReorderPagesAsync`.
- A slot is clamped to `[0, pageCount]`; the final page index is clamped to the
  valid page range.

## Agent Decisions / Thoughts

- **2026-08-30 Codex:** Keep the arithmetic pure and side-effect free. Provide a
  row/before overload for WPF hit-testing and a direct slot overload for callers
  that already resolved the insertion slot.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-30 | Documented the planned source-removal-aware placement contract. | Codex |
