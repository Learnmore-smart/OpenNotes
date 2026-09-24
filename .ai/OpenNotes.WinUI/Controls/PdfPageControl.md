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

## Phase B additions (2026-09-23)

- `CustomInkInputProcessingMode` = `None`/`Inking`/`Erasing`/`Shape`/`Laser`/
  `AreaHighlight`/`HiddenInk` — `SetInputMode` is the tool gate the page's own
  pointer handlers and `InkSurface` consult (EditorPage pushes it from
  `ApplyToolToAllPages`).
- **Selection** (`SelectionOverlayCanvas`): `SetSelectionMode`/`SetSelectionShape`
  (rect marquee vs free-form lasso)/`SetSelectionFilter`; `SelectAllAnnotations`,
  `SelectItems`, `ClearSelection`, `HasSelection`, `GetSelectionBounds`.
  Containment = Core `IsStrokeInsidePolygon` (60% point rule) /
  `IsStrokeInsideRect` (70%) / `HitTestClosedOrBounds`; all `ShapeGroupId`
  parts of a logical shape move together. Visuals: dashed bounds rect +
  marching-ants per-item outlines (`DispatcherQueueTimer` ~33 ms dash offset,
  gated on `WinUiThemeService.ShouldAnimate`), four 8×8 corner handles,
  rotate stem+knob 22 DIP above top-centre (all Core-computed). Gestures
  snapshot the pre-drag spine so `PointerCanceled`/capture loss restores it;
  completed drags raise `SelectionMoveCompleted`/`SelectionResizeCompleted`
  (total scale + opposite-corner anchor)/`SelectionRotateCompleted`
  (total degrees + centre) for the editor's undo push; `SelectionChanged`
  carries `HasSelection`+bounds for the flyout/keyboard paths. Outside-click
  + tool-switch deselect; Escape handled by the page-level key handler.
- **Shapes** (`ShapePreviewCanvas`): `CurrentShape`/`ShapeIsDashed`/`ShapeColor`/
  `ShapeStrokeSize`; live preview = `ShapeStrokeFactory.BuildPreviewPolylines`
  rendered via `LucideIcon.ParseIconGeometry` markup (WinUI has no
  `Geometry.Parse`); Shift applies `StrokeGeometry.ConstrainShapeEndpoints`
  (45° snap on Line/Arrow, square/circle on Rectangle/Ellipse); open kinds
  route through the ruler constraint at commit; release →
  `ShapeStrokeFactory.BuildShapeStrokes` quiet-adds every part then raises
  `ShapeCommittedUndoable` (editor pushes ONE `InkStrokesAddedAction` and
  arms Select — WPF order).
- **Hidden ink** (`HiddenInkCanvas` + `HiddenInkStore`): freehand gesture →
  `HiddenInkAnnotation` (default `HiddenInkMaskColor` opaque 199,205,212,
  `HiddenInkSize` 28) → `HiddenInkCreated` event. Opaque `Path` visuals +
  tap-to-reveal (`DispatcherQueueTimer`, auto-recover after
  `HiddenInkRevealDurationMs`/`RevealDurationMs`, `HiddenInkRevealState`
  deadline math). Eraser path hits whole masks via
  `HiddenInkIntersectsEraser` → `HiddenInkRemoved` (single) /
  `HiddenInksRemoved` (batch `HiddenInksRemovedEventArgs` with captured
  indices); cancel rolls back quietly. `AddHiddenInk` = quiet loader,
  `GetHiddenInkData` = save-path collector (T9 `CollectAnnotations`).
- **Laser** (`LaserInkCanvas`): surface events → red (`FF3B30`) 3-DIP
  `Polyline`s; completed strokes timestamp and fade on a ~30 ms
  `DispatcherQueueTimer` via `LaserInkFade.GetOpacity`/`IsExpired`
  (0.15 s hold + 0.9 s fade; `ShouldAnimate=false` expires instantly),
  `MaxLivePolylines` cap drops the oldest. Never stored/persisted/undoable.
- **Ruler bridge**: `GetRulerGeometryInPageCoords` delegate (EditorPage
  assigns per page — viewport→page `TransformToVisual` per query) feeds
  `InkSurface.RulerGeometryProvider`; `ProtectedCursor` (own-element only —
  WinUI keeps it protected) flips to the resize shape while the pointer is
  inside a corner handle.
- Timers store on `Microsoft.UI.Dispatching.DispatcherQueueTimer` via an
  alias — `Windows.System` is also imported and its type wins unqualified
  (was a build break).

## Important Notes / NEVER Change
- The ink layer sits UNDER `ShapePreviewCanvas`/`TextOverlayCanvas` — strokes must
  not swallow overlay input.
- `SetBitmapScalingMode` intentionally absent (WPF toggled it during scroll/zoom;
  WinUI images sample full quality).
- Hidden-ink visuals and `HiddenInkStore` stay OFF the stroke ledger — ordinary
  lasso/eraser/undo must never touch masks; only `HiddenInkIntersectsEraser`
  decides mask removal (whole mask, never fragments).
- Deferred: overlay-swap animation (`PdfImageOverlay` staged swap), text/sticky
  hit overlays (T8).

## Open Threads / Resume Context
- **Status:** GREEN — Phase A ink (pen/highlighter/eraser, pressure, scribble
  shape recognition, undo seams) + Phase B (select/lasso/transforms, shapes,
  hidden ink, laser, ruler bridge) live; renders BGRA pages.
