# OpenNotes.WinUI/Controls/PdfPageControl.xaml(.cs)
> Last updated: 2026-09-23 (V6 Task 7 Phase A — ink engine wired) | Protection: STANDARD

## Purpose
`Caelum.Controls.PdfPageControl : UserControl` — the per-page frame stacked in
`EditorPage.PagesContainer`: fixed DIP size, `PdfImage` raster slot, the
`InkSurface` ink layer and the named overlay canvases the annotation tasks
(T8/T9) attach to.

## What It Does
- `PageIndex` (zero-based), `PageSource`/`SetPageImage(SoftwareBitmapSource)` —
  assigning replaces `PdfImage.Source` so the working-set trim can reclaim the
  old bitmap via `null`.
- XAML layer order mirrors WPF: `PdfImage` → `PdfImageOverlay` →
  `ImageOverlayCanvas` → **`InkSurface` (ink strokes)** → `ShapePreviewCanvas` →
  `TextOverlayCanvas` → `HighlightsCanvas` → `PdfTextSelectionCanvas` →
  `SelectionOverlayCanvas` → `HiddenInkCanvas` → `EraserCanvas`(+`EraserIndicator`) →
  `LaserInkCanvas`.
- `InkSurface` is created/hosted by this control: exposes `InkToolKind` passthrough
  (`DefaultTool`, `PenOnlyMode`, `PressureEnabled`, `StrokeColor`, `StrokeSize`,
  `HighlighterAlpha`, `EraserSize`, `WholeStrokeEraseEnabled`, `BarrelEraseEnabled`,
  `IsInputEnabled`, `FitToCurve`) so `EditorPage.ApplyToolToAllPages` stays a single
  loop; forwards `StrokeCollected`/`StrokesErased`/`StrokeShapeReplaced`/
  `EraserGestureEnded`/`ActiveToolChanged` events plus `Store`/`EraserCanvas`/
  `EraserIndicator` accessors.
- Quiet store ops: `AddStrokeQuiet`/`RemoveStrokeQuiet`/`ClearStrokesQuiet` +
  `LoadAnnotations`/`ToAnnotation` converters feeding `InkStrokeData`⇄
  `StrokeAnnotation` (`[x,y,p]` pressure). `CancelInteraction` dismisses an
  in-flight stroke/erase gesture (scroll-pawn safety).
- `SetHostActive`, `SetDocumentInputEnabled`, `ClearPdfTextSelection`,
  `SetPdfTextSelectionRects`, `RefreshStickyNoteContextMenuLocalization` —
  Task-6 shells/stubs kept so call sites compile; annotation behavior lands T8–T9.
- `AutomationProperties.AutomationId` = `PdfPageControl.<index>` (set by
  EditorPage) — load-bearing for `tools/winui-editor-smoke.ps1`.

## Important Notes / NEVER Change
- The ink layer sits UNDER `ShapePreviewCanvas`/`TextOverlayCanvas` — strokes must
  not swallow overlay input; `InkSurface.IsHitTestVisible` gates by tool.
- `SetBitmapScalingMode` intentionally absent (WPF toggle during scroll/zoom;
  WinUI images sample full quality).
- Deferred: overlay-swap animation (`PdfImageOverlay` staged swap), text/sticky
  hit overlays, selection chrome, shape preview, hidden ink, laser (Phase B/T8).

## Open Threads / Resume Context
- **Status:** GREEN — Phase A ink (pen/highlighter/eraser, pressure, undo seams)
  live; renders BGRA pages; smoke `page-control-rendered` passes.
