# OpenNotes.Tests/ThumbnailCompositorTests.cs

> Last updated: 2026-08-26 | Protection: STANDARD

## Purpose

Regression coverage for live ordinary-ink thumbnail pixels and page-local
thumbnail revision/session guards.

## Open Threads / Resume Context

- **Status:** complete
- **Intent:** protect frozen live-ink composition, page/session revision rejection,
  and quiet add/remove/replace invalidation used by undo, redo and deletion.
- **Constraints:** do not change PdfService's clean-Pdfium rendering or the
  bounded EditorPage thumbnail cache.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-26 | Added RED-first reflection tests; focused compositor/sidebar/stale-operation filter is GREEN at 24/24. | Codex |
| 2026-08-26 | Added a real STA PdfPageControl regression for quiet add/remove/re-add (redo/delete/undo paths); RED before event wiring, GREEN with 28/28 focused thumbnail/history/selection tests. | Codex |
