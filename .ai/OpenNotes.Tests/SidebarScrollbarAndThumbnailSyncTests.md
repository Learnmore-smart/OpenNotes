# OpenNotes.Tests/SidebarScrollbarAndThumbnailSyncTests.cs

> Last updated: 2026-08-26 | Protection: STANDARD

## Purpose

Protect sidebar scroll behavior, thumbnail synchronization, live-ink composition, and stale-render rejection.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** live ordinary ink now appears in thumbnails and page-local
  mutations invalidate stale cached renders. Dedicated coverage lives in
  `ThumbnailCompositorTests` so the existing sidebar source/STA checks remain
  unchanged.
- **Constraint:** preserve the clean Pdfium base-image and bounded thumbnail-cache architecture.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-26 | Implemented and verified clean-base live-ink composition with page/session stale-result guards. | Codex |
