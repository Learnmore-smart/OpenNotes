# OpenNotes.Core/Geometry/StrokeGeometry.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Pure, UI-free stroke/ink geometry for the V6 WinUI 3 migration (Task 2). Everything here is a direct port of the math that used to live inside `Controls/PdfPageControl.xaml.cs` on `System.Windows.*` types — behaviour is identical by construction, now expressed over `InkStrokeData`/`InkPointData`/`PointD`/`RectD`.

## Namespace

`Caelum.InkGeometry` — deliberately NOT `Caelum.Geometry`, which collides with `System.Windows.Media.Geometry` inside `Caelum.*` code files.

## API surface

- Value types: `PointD` (ops/dot/midpoint), `RectD` (contains/intersects/inflate/union), `enum InkShapeKind` (member names identical to WPF `ShapeKind` — the adapter maps by name and throws on unknown; order locked for any ordinal consumers).
- Distance/projection: `Dist`, `PerpendicularDistance`, `DistanceToSegment`, `ProjectToSegment`, `MaxDistanceToSegment`, `DirectionBucket`.
- Bounds: `GetBounds`, `GetSpineBounds`.
- Eraser: `CreateEraserRects`, `HiddenInkIntersectsEraser`, `SegmentIntersectsRect` (rejects non-finite endpoints), `EraserHitsStroke`, `SplitStrokeAtEraser` — **square-stamp model since Task 7 Phase A** (was capsule-model approximation). See below.
- Hit-testing/selection: `HitTestStroke`, `HitTestPolyline`, `HitTestClosedOrBounds`, `IsPointInPolygon`, `IsRectInsidePolygon`, `IsStrokeInsidePolygon`, `IsContainerInsidePolygon`, `IsStrokeInsideRect`.
- Shape generation: `BuildShapeOutline` (all `InkShapeKind` members incl. 64-segment ellipse), `BuildArrowGeometry`, `ConstrainShapeEndpoints`, `BuildDashedLine`/`BuildDashedPolyline` (phase carries across corners).
- Recognition: `TryRecognizeShape` (enforces `public const int MinRecognizedShapePoints = 8` — the same ≥8-point gate the WPF call site applies) + scribble thresholds and `DirectionRun` helper.
- Point transforms: `SimulateInkFlow`, `SmoothPoints`, `TryGetStraightEndpoints`, `ConstrainPointsToRuler`, `IsPointInsideConvexQuad`, `TryFindFirstQuadIntersection`.
- Rendering law: `GetRenderedStrokeHalfWidth(size, pressure)` — the empirical WPF width law `half = size·(0.125 + 0.75·p)` (diameter = `size·(0.25+1.5p)`; p=0.5 → nominal, p=1.0 → 1.75×, IgnorePressure ≡ p=0.5). Shared by the eraser reach, the outline tessellator and tests.

## Square-stamp eraser model (Task 7 Phase A)

`EraserHitsStroke(strokePoints, strokeSize, strokeIgnoresPressure, eraserPath, eraserSize)` and `SplitStrokeAtEraser(...)` now model the WPF `RectangleStylusShape` + `Stroke.GetEraseResult` footprint exactly:

- Footprint = union of convex pieces: one axis-aligned `eraserSize`² square per path point + the convex hull (hexagon) swept between consecutive stamps.
- A spine point `s(t)` is erased when `dist(s(t), piece) ≤ w(t)` where `w(t)` = stroke rendered half-width lerped between segment endpoints (`GetRenderedStrokeHalfWidth` of the point's effective pressure — `IgnorePressure` pins 0.5).
- Per spine segment the removed interval is the union of: interior (all edge half-planes, linear inequalities), per-edge strips (projection ∩ |perp| ≤ w(t)), and per-vertex caps (quadratic `|s(t)−v|² ≤ w(t)²` — also covers expanding discs when pressure outgrows spine speed). Zero-width tangent touches are excluded; intervals merge before the complement walk.
- Fragment endpoints interpolate X/Y/**pressure** at cuts; a miss returns a copy of the input (the host compares coordinates — WPF returns the same reference instead).
- Verified: `WpfCoreEraserParityTests` cross-checks fragment counts + extents against live `Stroke.GetEraseResult` on a 23-case corpus (uniform/pressure/tapered/step spines, single/swept/multi-point stamps, dots, vertical/diagonal, IgnorePressure, endpoint graze, HitTest boundary ladder). Boundary agreement is exact to ~1e-2 DIP; corner grazes can differ ≤ ~0.4·strokeWidth because WPF clips the tapered quad patch while the model clips the node-disc union.

## Constraints / NEVER Change

- `InkShapeKind` member NAMES must stay identical to `Caelum.Controls.ShapeKind` — `WpfStrokeAdapter` maps by name-switch and throws `ArgumentOutOfRangeException` on unknown members. Order is irrelevant to the adapter but locked for any ordinal consumers.
- No `System.Windows.*` references; `net8.0`, `ImplicitUsings` disabled (explicit `using System.Linq` etc.).
- Scribble-recognition thresholds ported verbatim; do not retune.
- The eraser signatures take stroke size + IgnorePressure explicitly — callers must pass the stroke's own `Size`/`IgnorePressure`, not a global hit radius.

## Verification

`OpenNotes.Tests/CoreStrokeGeometryTests.cs` (geometry/eraser/hit-test/shape suites) + `OpenNotes.Tests/WpfCoreEraserParityTests.cs` (live WPF `GetEraseResult`/`HitTest` corpus — STA fixture).
