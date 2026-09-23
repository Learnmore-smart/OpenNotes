# OpenNotes.Tests/CoreStrokeGeometryTests.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

Unit coverage for the UI-free `Caelum.InkGeometry.StrokeGeometry` primitives added in V6 Task 2 — 36 tests running on `net8.0-windows` in the existing `OpenNotes.Tests` project (no separate Core test project per task spec).

## Coverage

- Widened stroke hit-testing: inside/on-capsule hits, outside misses, radius scaling.
- Eraser: `CreateEraserRects` sizing, `SegmentIntersectsRect`, `SplitStrokeAtEraser` fragment point lists and pressure preservation, `EraserHitsStroke`, `HiddenInkIntersectsEraser`.
- Bounds: `GetBounds`/`GetSpineBounds` inflation, empty-stroke and degenerate (single-point/duplicate-point) strokes.
- Selection: `IsPointInPolygon`, `IsStrokeInsidePolygon`/`IsRectInsidePolygon`, `IsStrokeInsideRect`.
- Shapes: `BuildShapeOutline` for line/rect/ellipse/triangle/arrow/etc., `BuildArrowGeometry`, `ConstrainShapeEndpoints` (shift-snapping), dashed polyline phase across corners.
- Transforms: `SimulateInkFlow`, `SmoothPoints`, `TryGetStraightEndpoints`, `ConstrainPointsToRuler`, `TryRecognizeShape` scribble thresholds.

## Important Notes / NEVER Change

- The ellipse outline closes within floating-point tolerance, not bit-exact — assertions use tolerance.
- `SplitStrokeAtEraser` is the capsule-model port for the UI-free host; the WPF runtime still clips with exact `Stroke.GetEraseResult` — tests pin the Core contract only.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-22 | Added 36 tests for the Task 2 Core geometry port. | Devin |
