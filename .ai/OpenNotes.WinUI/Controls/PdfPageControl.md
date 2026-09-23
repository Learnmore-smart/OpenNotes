# OpenNotes.WinUI/Controls/PdfPageControl.xaml(.cs)
> Last updated: 2026-09-23 (V6 Task 6 — editor page frame) | Protection: STANDARD

## Purpose
`Caelum.Controls.PdfPageControl : UserControl` — the per-page frame stacked in
`EditorPage.PagesContainer`: fixed DIP size, `PdfImage` raster slot and the
named overlay canvases the annotation tasks (T7/T8/T9) attach to.

## What It Does
- `PageIndex` (zero-based), `PageSource`/`SetPageImage(SoftwareBitmapSource)` —
  assigning replaces `PdfImage.Source` so the working-set trim can reclaim the
  old bitmap via `null`.
- XAML layer order mirrors WPF: `PdfImage` → `PdfImageOverlay` →
  `ImageOverlayCanvas` → `ShapePreviewCanvas` → `TextOverlayCanvas` →
  `HighlightsCanvas` → `PdfTextSelectionCanvas` → `SelectionOverlayCanvas` →
  `HiddenInkCanvas` → `EraserCanvas`(+`EraserIndicator`) → `LaserInkCanvas`.
- `SetHostActive`, `SetDocumentInputEnabled`, `ClearPdfTextSelection`,
  `SetPdfTextSelectionRects`, `RefreshStickyNoteContextMenuLocalization` —
  Task-6 shells/stubs kept so call sites compile; annotation behavior lands T7–T9.
- `AutomationProperties.AutomationId` = `PdfPageControl.<index>` (set by
  EditorPage) — load-bearing for `tools/winui-editor-smoke.ps1`.

## Important Notes / NEVER Change
- WPF `InkCanvas` input/processing is NOT here — the WinUI ink surface is Task 7;
  do not bolt pointer-ink onto these canvases early.
- `SetBitmapScalingMode` intentionally absent (WPF toggle during scroll/zoom;
  WinUI images sample full quality).
- Deferred: overlay-swap animation (`PdfImageOverlay` staged swap), stroke/text
  hit overlays.

## Open Threads / Resume Context
- **Status:** GREEN — renders BGRA pages; smoke `page-control-rendered` passes.
