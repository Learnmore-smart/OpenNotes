# OpenNotes.Tests/WinUiInkRenderingSourceTests.cs
> Last updated: 2026-09-26 (T14-A — created) | Protection: STANDARD

## Purpose
Source-contract fixture for the T14-A P0 leg: pins the ink-rendering crash
fixes and the crash-journal wiring by grepping `OpenNotes.WinUI` sources
(uses the standard `Read`/`ProjectRoot` helper pair — resolves the solution
root by locating `OpenNotes.WinUI/OpenNotes.WinUI.csproj`).

## Coverage (5 tests)
- `StrokeGeometryUsesNonzeroFillRuleSoCrossingsFillSolid` — `StrokeRenderer`
  sets `FillRule = FillRule.Nonzero` (the EvenOdd default punched holes in
  self-crossing strokes).
- `OutlineFigureSkipsNonFiniteVerticesAndUpdateRebuildIsGuarded` —
  `double.IsFinite` scan runs inside `AddOutlineFigure` BEFORE any vertex
  reaches `StartPoint`/`PolyLineSegment` (the E_INVALIDARG chokepoint);
  `UpdateStrokePath` body contains try + `catch (Exception` around
  `path.Data = BuildGeometry(stroke)`; faults report via `Debug.WriteLine`
  + `CrashLogger.Log`.
- `PointerToGeometryPathsDropNonFiniteInput` — the follow-up leg: pins
  `IsFinite(PointD)`/`IsFinite(Point)` helpers in `InkSurface`, the press
  chokepoint (`!IsFinite(point.Position)`), draw-ingest packet filter,
  both indicator guards (`indicator == null || !IsFinite(pagePoint)` ×2),
  and in `PdfPageControl` the selection press/move gates
  (`if (!IsFinite(pos))` ×2), shape-drag guards, `if (!IsFinite(p))` in
  the shape preview + all three laser loops, and the hidden-ink
  `double.IsFinite(pt[0]/pt[1])` vertex check. Review leg added:
  `IsFinite(d)`/`IsFinite(tail)`/`IsFinite(currentPoint)` packet filters
  on the laser + erase batch streams and the `_lastErasePoint` writers
  (`EraseAtPoint`/`EraseAlongPoints`).
- `EditorPagePointerIngressDropsNonFinitePositions` (review leg) — pins
  `EditorPage.xaml.cs`'s `private static bool IsFinite(Point p)` helper
  and the ingress guards: `BeginTextBoxDrag`/`UpdateTextBoxDrag`
  (`pressPoint`/`currentPoint`), `TextResizeHandle` press+move (`pos`),
  `Ruler_PointerPressed`/`Moved` (`local`/`viewport`/`p`),
  `PageControl_TextOverlayPointerPressed` (`point`) and
  `PageControl_BackgroundPointerPressed` (`clicked`).
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
