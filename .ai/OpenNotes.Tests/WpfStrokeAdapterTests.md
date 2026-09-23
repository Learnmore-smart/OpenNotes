# OpenNotes.Tests/WpfStrokeAdapterTests.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

STA coverage for `Caelum.Controls.WpfStrokeAdapter` — the only interop layer between live WPF `Stroke`/`StylusPointCollection` objects and the UI-free `InkStrokeData`/`PointD`/`RectD` primitives in `OpenNotes.Core`.

## Coverage (5 tests)

- `OrdinaryStroke_RoundTripsThroughInkStrokeData`: points + PressureFactor, RGBA, Size→Width/Height, IsHighlighter, FitToCurve; ordinary ink carries no shape identity.
- `SinglePointStroke_ExpandsToDotSegment`: 1-point `InkStrokeData` → 2-point `Stroke` (+0.1 X, pressure preserved), matching `AddStroke`/`PreserveTapStroke`/`ThumbnailCompositor`.
- `ShapeStrokeMetadata_RoundTripsThroughAdapter`: group id/kind/part index/dashed identity survives `ToInkStrokeData`→`ToStroke` (via `ShapeStrokeMetadata.Read`), `IgnorePressure` set for shape parts.
- `NullOrEmptyPoints_ReturnNullStroke`: null payload, null `Points`, and empty `Points` all return null.
- `ToInkShapeKind_ThrowsOnUnrecognizedKind`: name-switch maps all known kinds; cast-to-invalid `ShapeKind` throws `ArgumentOutOfRangeException` instead of degrading to Line.

## Important Notes / NEVER Change

- Fixture is `[Apartment(STA)]` like sibling ink tests — `Stroke`/`StylusPointCollection` construction needs no dispatcher but STA keeps it consistent.
- `WpfStrokeAdapter` and `ShapeStrokeMetadataKeys` are internal; both are visible to this assembly via `InternalsVisibleTo`.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-09-22 | Added 5 tests for the Task 2 review fixes (round-trip, dot expansion, null contract, fail-loud kind map). | Devin |
