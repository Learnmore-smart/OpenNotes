# OpenNotes.Tests/WinUiInkRenderingSourceTests.cs
> Last updated: 2026-09-26 (T14-A — created) | Protection: STANDARD

## Purpose
Source-contract fixture for the T14-A P0 leg: pins the ink-rendering crash
fixes and the crash-journal wiring by grepping `OpenNotes.WinUI` sources
(uses the standard `Read`/`ProjectRoot` helper pair — resolves the solution
root by locating `OpenNotes.WinUI/OpenNotes.WinUI.csproj`).

## Coverage (3 tests)
- `StrokeGeometryUsesNonzeroFillRuleSoCrossingsFillSolid` — `StrokeRenderer`
  sets `FillRule = FillRule.Nonzero` (the EvenOdd default punched holes in
  self-crossing strokes).
- `OutlineFigureSkipsNonFiniteVerticesAndUpdateRebuildIsGuarded` —
  `double.IsFinite` scan runs inside `AddOutlineFigure` BEFORE any vertex
  reaches `StartPoint`/`PolyLineSegment` (the E_INVALIDARG chokepoint);
  `UpdateStrokePath` body contains try + `catch (Exception` around
  `path.Data = BuildGeometry(stroke)`; faults report via `Debug.WriteLine`
  + `CrashLogger.Log`.
- `AppHooksCrashLoggingWithoutSwallowing` — `App.xaml.cs` hooks
  `UnhandledException`, `AppDomain.CurrentDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`; `e.Handled = false` and no
  `SetObserved()` call. `CrashLogger` writes under
  `ProductInfo.GetDataDirectory()/logs` with `crash-` names and flattens
  `AggregateException`.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-26 | Created for T14-A. | Devin |
