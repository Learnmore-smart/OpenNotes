# OpenNotes.Core/Geometry/StrokeGeometry.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

Pure, UI-free stroke/ink geometry for the V6 WinUI 3 migration (Task 2). Everything here is a direct port of the math that used to live inside `Controls/PdfPageControl.xaml.cs` on `System.Windows.*` types — behaviour is identical by construction, now expressed over `InkStrokeData`/`InkPointData`/`PointD`/`RectD`.

## Namespace

`Caelum.InkGeometry` — deliberately NOT `Caelum.Geometry`, which collides with `System.Windows.Media.Geometry` inside `Caelum.*` code files.

## API surface

- Value types: `PointD` (ops/dot/midpoint), `RectD` (contains/intersects/inflate/union), `enum InkShapeKind` (member order identical to WPF `ShapeKind`).
- Distance/projection: `Dist`, `PerpendicularDistance`, `DistanceToSegment`, `ProjectToSegment`, `MaxDistanceToSegment`, `DirectionBucket`.
- Bounds: `GetBounds`, `GetSpineBounds`.
- Eraser: `CreateEraserRects`, `HiddenInkIntersectsEraser`, `SegmentIntersectsRect`, `EraserHitsStroke`, `SplitStrokeAtEraser` (capsule-model approximation).
- Hit-testing/selection: `HitTestStroke`, `HitTestPolyline`, `HitTestClosedOrBounds`, `IsPointInPolygon`, `IsRectInsidePolygon`, `IsStrokeInsidePolygon`, `IsContainerInsidePolygon`, `IsStrokeInsideRect`.
- Shape generation: `BuildShapeOutline` (all `InkShapeKind` members incl. 64-segment ellipse), `BuildArrowGeometry`, `ConstrainShapeEndpoints`, `BuildDashedLine`/`BuildDashedPolyline` (phase carries across corners).
- Recognition: `TryRecognizeShape` + scribble thresholds and `DirectionRun` helper.
- Point transforms: `SimulateInkFlow`, `SmoothPoints`, `TryGetStraightEndpoints`, `ConstrainPointsToRuler`, `IsPointInsideConvexQuad`, `TryFindFirstQuadIntersection`.

## Constraints / NEVER Change

- One deliberate approximation: `SplitStrokeAtEraser` models the eraser as capsules along its path. The WPF layer still uses exact `Stroke.GetEraseResult` rendered-geometry clipping at runtime; the pure split exists for the UI-free host and tests.
- `InkShapeKind` member order must stay identical to `Caelum.Controls.ShapeKind` — `WpfStrokeAdapter` maps by name.
- No `System.Windows.*` references; `net8.0`, `ImplicitUsings` disabled (explicit `using System.Linq` etc.).
- Scribble-recognition thresholds ported verbatim; do not retune.

## Verification

`OpenNotes.Tests/CoreStrokeGeometryTests.cs` — 36 tests covering widened-outline hit-testing in/out, eraser split point lists, bounds, empty/degenerate strokes, polygon/rect selection, shape outlines, dashes, recognition, ruler/smoothing/ink-flow.
