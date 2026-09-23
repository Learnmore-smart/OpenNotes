# OpenNotes.Tests/CoreStrokeGeometryTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Unit coverage for the UI-free `Caelum.InkGeometry.StrokeGeometry` primitives added in V6 Task 2 — running on `net8.0-windows` in the existing `OpenNotes.Tests` project (no separate Core test project per task spec).

## Coverage

- Widened stroke hit-testing: inside/on-capsule hits, outside misses, radius scaling.
- Eraser (square-stamp model since Task 7A): `CreateEraserRects` sizing, `SegmentIntersectsRect`, `SplitStrokeAtEraser` fragment point lists and pressure preservation (interpolated cut pressure), `EraserHitsStroke`, `HiddenInkIntersectsEraser`, swept-path cuts between sparse vertices, `GetRenderedStrokeHalfWidth` width law.
- Bounds: `GetBounds`/`GetSpineBounds` inflation, empty-stroke and degenerate (single-point/duplicate-point) strokes.
- Selection: `IsPointInPolygon`, `IsStrokeInsidePolygon`/`IsRectInsidePolygon`, `IsStrokeInsideRect`.
- Shapes: `BuildShapeOutline` for line/rect/ellipse/triangle/arrow/etc., `BuildArrowGeometry`, `ConstrainShapeEndpoints` (shift-snapping), dashed polyline phase across corners.
- Transforms: `SimulateInkFlow`, `SmoothPoints`, `TryGetStraightEndpoints`, `ConstrainPointsToRuler`, `TryRecognizeShape` scribble thresholds.

## Important Notes / NEVER Change

- The ellipse outline closes within floating-point tolerance, not bit-exact — assertions use tolerance.
- Eraser signatures take `(strokeSize, strokeIgnoresPressure, eraserPath, eraserSize)` — pass the stroke's own size, not a global radius.
- Split assertions use `1e-6` tolerance — fragment interpolation lands ~1e-9 off round numbers (39.999999998999996-style), not a bug.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-22 | Added 36 tests for the Task 2 Core geometry port. | Devin |
| 2026-09-23 | Task 7A: calls updated to square-stamp signatures (stroke size + IgnorePressure + eraser path); split tolerance relaxed to 1e-6. | Devin |
