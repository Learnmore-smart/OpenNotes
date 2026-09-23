# Controls/WpfStrokeAdapter.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

Thin WPF↔Core conversion layer for the V6 WinUI 3 migration (Task 2). All ink/geometry interop between `System.Windows.*` types and the UI-free `Caelum.InkGeometry` primitives goes through this one file so the rest of the WPF code can call `StrokeGeometry` directly.

## API

- Points: `ToPointD`/`ToPoint`, `ToRectD`/`ToRect`, `ToPointDList` (over `IReadOnlyList<Point>`/`PointCollection`/`StylusPointCollection`), `ToPointList`.
- Stylus points: `ToInkPoints`, `ToStylusPoint`/`ToStylusPoints` (over `InkPointData` and `PointD` — `PointD` gets default 0.5 pressure).
- Shape kind: `ToInkShapeKind(ShapeKind)` — maps by member NAME to `InkShapeKind`; throws `ArgumentOutOfRangeException` on unrecognized kinds (a new `ShapeKind` member must fail loud, not silently degrade to Line).
- Whole strokes: `ToInkStrokeData(Stroke)` copies stylus points (with `PressureFactor`), RGBA from `DrawingAttributes.Color`, uniform `Size` (width), `IsHighlighter`, `FitToCurve`, and shape identity via `ShapeStrokeMetadata.Read`; `ToStroke(InkStrokeData)` rebuilds a `Stroke` and re-applies the identity — returns null for a null payload or null/empty point list (same "no stroke" contract as `CreateStrokeFromSnapshot`), and expands a single-point payload to a +0.1-DIP segment so it renders as a dot (matching `AddStroke`/`PreserveTapStroke`/`ThumbnailCompositor`).

## Constraints

- Adapter only — no geometry logic lives here; delegate to `Caelum.InkGeometry.StrokeGeometry`.
- `ToStroke` uses a default `StylusPointDescription`-compatible collection: callers handling real hardware packets must keep normalizing to X/Y before building transient collections (the 5.2.7 eraser-crash fix).

## Verification

`OpenNotes.Tests/WpfStrokeAdapterTests.cs` (STA) pins the `ToInkStrokeData`/`ToStroke` round-trip, the single-point dot expansion, the shape-identity round-trip, the null/empty-Points→null contract and the fail-loud `ToInkShapeKind` default. Round-trips are also exercised indirectly by `ShapeStrokeMetadataTests`, `ShapeToolTests`, `StrokeEraserGeometryTests` and `CoreStrokeGeometryTests` (same math both sides).
