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

## Out of scope (flagged suspects — same E_INVALIDARG vector)
- `PdfPageControl` laser polylines (`Ink_LaserStroke*`), freeform selection
  `_freeSelectionPath.Points.Add`, shape-preview polylines — all feed raw
  pointer positions into `Polyline.Points` per move event.
- `InkSurface.ShowEraserIndicatorAt`/`ShowBrushIndicatorAt` →
  `Canvas.SetLeft/Top` with a non-finite `PointD` also throws E_INVALIDARG.
- If the field crash recurs, the crash log will name the real site.

## Result
Implemented as planned. `CreateStrokePath` additionally delegates to
`UpdateStrokePath` so both geometry-build entry points ride the one guarded
rebuild. `LogFault` caps crash-file writes at 3/process (renderer is a
per-pointer-move hot path). Build Release 0 errors; new fixture
`WinUiInkRenderingSourceTests` 3/3 green, related ink/geometry +
WinUI-source-contract batches 177 + 65 green.
