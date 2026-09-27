# T14-A — ink-path stowed crash + self-crossing fill holes (P0)
> 2026-09-26 | Plan + record for the v6.0.3 field-crash leg on `v6/winui3`.

## Symptom
- WER: `OpenNotes.WinUI.exe` → `Microsoft.UI.Xaml.dll` `0xC000027B` (stowed
  WinRT exception), secondary `combase.dll` `80070057` = E_INVALIDARG, raised
  while drawing ink. No managed stack exists — no app log.
- Ink strokes that cross themselves render with holes.

## Plan
1. `OpenNotes.WinUI/Rendering/StrokeRenderer.cs`
   - `PathGeometry` defaults to `FillRule.EvenOdd`; self-intersecting outline
     polygons punch holes. Set `FillRule = FillRule.Nonzero` in
     `BuildGeometry` (WPF ink renderer behaviour — crossings fill solid).
   - `AddOutlineFigure`: scan the outline once and skip the figure when any
     vertex is non-finite (`double.IsFinite`). A NaN/Infinity fed to
     `PathFigure.StartPoint`/`PolyLineSegment.Points` throws
     `ArgumentException` on the UI thread → the suspected stowed E_INVALIDARG.
     NaN can be produced inside `StrokeOutline` (Catmull-Rom resample on a
     non-finite packet, `ex*ex` overflow on huge deltas) even though the
     inputs are pointer positions.
   - `UpdateStrokePath`: wrap the per-pointer-move geometry rebuild in
     try/catch — log + keep the last good geometry instead of dying.
     `CreateStrokePath` delegates to it so the create path is guarded too.
2. `OpenNotes.WinUI/Services/CrashLogger.cs` (new, `internal static`)
   - Appends one timestamped entry per event to
     `%LOCALAPPDATA%\Caelum\logs\crash-yyyyMMdd-HHmmss.log`
     (`ProductInfo.GetDataDirectory()` + `logs`). Body = source, UTC/local
     stamp, exception `ToString()` (type+message+full stack+inner chain;
     `AggregateException` flattened). Prunes to newest 20 `crash-*.log`.
     Every path wrapped — never throws inside a crash handler.
3. `OpenNotes.WinUI/App.xaml.cs`
   - Hook `Application.UnhandledException`, `AppDomain.CurrentDomain.
     UnhandledException`, `TaskScheduler.UnobservedTaskException` in the ctor
     before `InitializeComponent` (a xbf-load crash gets logged too).
   - Log only: `e.Handled = false`, no `SetObserved()` — we want stacks, not
     suppression.
4. Test: `OpenNotes.Tests/WinUiInkRenderingSourceTests.cs` — source-contract
   pins for Nonzero fill, the finite guard, the UpdateStrokePath try/catch,
   the three App hooks, and the CrashLogger destination/cap.

## Follow-up leg (same day) — residual pointer→geometry guards
The originally-flagged sibling sites were then hardened too (same vector,
silent skips — hot paths get no per-event logging):
- `InkSurface.cs`: `IsFinite(PointD)`/`IsFinite(Point)` helpers;
  `PointerPressed` swallows a non-finite press BEFORE `CapturePointer`
  (guarding after capture would wedge `_activePointerId` — no gesture
  branch clears it on release); draw-move intermediates + tail-append and
  `CompleteStroke`'s release append require finite positions (also keeps
  NaN out of the stroke's saved point list → PDF); both indicator methods
  bail before `SetLeft/SetTop`.
- `PdfPageControl.xaml.cs`: `IsFinite(PointD)` helper; selection-overlay
  press + move gates (covers freeform `Polyline.Points`, rect
  `Canvas.SetLeft/Top`, drag/resize anchors); `Ink_ShapeDragStarted/
  Updated` guards; `Ink_ShapeDragEnded` substitutes the anchor for a
  non-finite release (zero-size → sub-threshold → commit skipped with all
  cleanup intact — both shape and area-highlight branches);
  `UpdateShapePreview` per-vertex skip; `AddHiddenInkVisual` finite check
  on `pt[0]/pt[1]` (covers stored masks); all three laser loops skip
  non-finite points.

## Review-follow-up leg (same day)
Approved-with-comments sweep added:
- **CrashLogger.Log** now wraps `Flatten()`+`ToString()` in try/catch — a
  throwing formatter falls back to logging `GetType().Name` (formatting
  must not kill the journal entry it exists to write).
- **InkSurface**: laser batch + eraser batch filter non-finite packets
  (`IsFinite(d)`/`IsFinite(tail)`/`IsFinite(currentPoint)`);
  `EraseAtPoint`/`EraseAlongPoints` keep `_lastErasePoint` finite (a NaN
  there freezes every subsequent dedup compare). Empty filtered erase
  batches bail before `ShowEraserIndicatorAt(batch[^1])`.
- **EditorPage.xaml.cs**: `IsFinite(Point)` helper + ingress guards —
  `Ruler_PointerPressed/Moved`, `PageControl_TextOverlayPointerPressed`
  (before `CreateTextBox`'s `SetLeft`), `PageControl_BackgroundPointerPressed`
  (before `_lastClickedPoint` + the sticky `pos`), `TextResizeHandle`
  press+`UpdateTextResize` move, `BeginTextBoxDrag`+`UpdateTextBoxDrag`.
- **M3**: `SelectionOverlay_PointerMoved` returns UNHANDLED on non-finite
  `pos` — only the captured-pointer path marks handled (hover parity).

## Result
Implemented as planned. `CreateStrokePath` additionally delegates to
`UpdateStrokePath` so both geometry-build entry points ride the one guarded
rebuild. `LogFault` caps crash-file writes at 3/process (renderer is a
per-pointer-move hot path). Build Release 0 errors; fixture
`WinUiInkRenderingSourceTests` 5/5 green, related ink/geometry +
WinUI-source-contract batches 174/214 + 65 green.
