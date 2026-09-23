# OpenNotes.WinUI/Controls/PdfPageControl.xaml(.cs)
> Last updated: 2026-09-23 (V6 Task 7 Phase A — ink engine wired) | Protection: STANDARD

## Purpose
`Caelum.Controls.PdfPageControl : UserControl` — the per-page frame stacked in
`EditorPage.PagesContainer`: fixed DIP size, `PdfImage` raster slot, the
`InkSurface` ink layer and the named overlay canvases the annotation tasks
(T7B/T8/T9) attach to.

## What It Does
- `PageIndex` (zero-based), `PageSource`/`SetPageImage(SoftwareBitmapSource)` —
  assigning replaces `PdfImage.Source` so the working-set trim can reclaim the
  old bitmap via `null`.
- XAML layer order mirrors WPF (`PageGrid`, with a `ThemeShadow` caster `Border`
  replacing the WPF DropShadowEffect): `PdfImage` → `PdfImageOverlay` →
  `ImageOverlayCanvas` → **`InkSurface` (ink strokes)** → `ShapePreviewCanvas` →
  `TextOverlayCanvas` → `HighlightsCanvas` → `PdfTextSelectionCanvas` →
  `SelectionOverlayCanvas` → `HiddenInkCanvas` → `EraserCanvas` (hosts the
  `EraserIndicator` `Ellipse`) → `LaserInkCanvas`.
- `Ink` exposes the `InkSurface` so `EditorPage.ApplyToolToAllPages` pushes
  tool/colour/size/`AppSettings` fields directly; the ctor assigns
  `InkSurface.EraserIndicator = EraserIndicator` and forwards
  `StrokeCollected`/`StrokeRecognized`/`StrokesErased`/`InkMutated`.
- `AddStroke(StrokeAnnotation)` — quiet sidecar load (no undo entry, mirroring
  the WPF loader). `CancelInteraction()` dismisses an in-flight stroke/erase
  gesture (scroll-pawn + tool-switch + teardown safety).
- `SetHostActive`/`SetDocumentInputEnabled` combine into `ApplyInputGate` →
  `InkSurface.InputEnabled` (+ `CancelInteraction` when the gate closes).
- `ClearPdfTextSelection`, `SetPdfTextSelectionRects`,
  `RefreshStickyNoteContextMenuLocalization` — Task-6 shells/stubs kept so call
  sites compile; annotation behavior lands T8–T9.
- `AutomationProperties.AutomationId` = `PdfPageControl.<index>` (set by
  EditorPage) — load-bearing for `tools/winui-editor-smoke.ps1`.

## Important Notes / NEVER Change
- The ink layer sits UNDER `ShapePreviewCanvas`/`TextOverlayCanvas` — strokes must
  not swallow overlay input.
- `SetBitmapScalingMode` intentionally absent (WPF toggled it during scroll/zoom;
  WinUI images sample full quality).
- Deferred: overlay-swap animation (`PdfImageOverlay` staged swap), text/sticky
  hit overlays, selection chrome, shape preview, hidden ink, laser (Phase B/T8).

## Open Threads / Resume Context
- **Status:** GREEN — Phase A ink (pen/highlighter/eraser, pressure, scribble
  shape recognition, undo seams) live; renders BGRA pages.
