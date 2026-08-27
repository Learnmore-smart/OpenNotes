# Pages/ThumbnailCompositor.cs

> Last updated: 2026-08-26 | Protection: STANDARD

## Purpose

Compose ordinary live ink over a clean Pdfium thumbnail without changing the
strip-and-rebuild PDF rendering boundary.

## Open Threads / Resume Context

- **Status:** ready_for_next
- **Intent:** add a pure WPF pixel compositor for ordinary pen/highlighter
  strokes and use page-local revision checks so stale async thumbnails cannot
  overwrite a newer mutation. Implemented in `ThumbnailCompositor` and
  `ThumbnailRevisionGate`; EditorPage now composes after the clean base render
  and retries a realized row after a stale mutation.
- **Constraints:** preserve DIP coordinates, source bitmap dimensions/DPI,
  frozen output, the 24-entry EditorPage LRU, and PdfService's clean bitmap.

## Agent Decisions / Thoughts

- The helper accepts `StrokeAnnotation` snapshots rather than live WPF `Stroke`
  objects, keeping composition independent of InkCanvas ownership and easy to
  exercise in an STA pixel test. It renders at the source thumbnail DPI and
  dimensions, preserves source color/alpha/width/highlighter/FitToCurve, and
  freezes the `RenderTargetBitmap` result.
- The base thumbnail remains the sole Pdfium image. Ordinary ink is drawn in
  thumbnail coordinates with source alpha and highlighter attributes preserved;
  hidden ink/text/images are intentionally outside this helper.
- `ThumbnailRevisionGate` clears all revisions at a document-session boundary,
  increments only the mutated page, and rejects old session or page revisions.
  EditorPage leaves an in-flight marker until its stale callback exits, avoiding
  the old-callback/new-marker race while still refreshing realized rows.

## Important Notes / NEVER Change

- Do not render owned PDF annotations through Pdfium or alter PdfService's
  strip-and-rebuild invariant.
- Coordinates in `StrokeAnnotation.Points` are page DIPs (96 DPI); output must
  stay frozen for cross-thread binding/cache use.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-26 | Implemented frozen clean-base + live-ink composition and page/session stale-result guard. | Codex |
