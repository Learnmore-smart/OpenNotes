# OpenNotes.Core/Models/InkStrokeData.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

UI-free in-memory stroke payload mirroring `StrokeAnnotation` semantics 1:1 for the V6 WinUI 3 migration (Task 2). The WinUI ink surface builds this type directly; the WPF layer converts live `System.Windows.Ink.Stroke` objects through `Controls/WpfStrokeAdapter.cs`.

## API

- `Points`: `List<InkPointData>` spine points in page DIP coordinates (origin top-left).
- `R`/`G`/`B`/`A`: RGBA colour channels (`A` defaults to 255), same as `StrokeAnnotation`.
- `Size`: uniform stroke width/height in DIPs, default 2.0.
- `IsHighlighter`, `FitToCurve` (default `true`, same default as `StrokeAnnotation`).
- Shape metadata: `ShapeGroupId`, `ShapeKind`, `ShapePartIndex`, `IsDashedShape` — empty group/kind means ordinary legacy ink.
- `GetShapeIdentity()` / `ApplyShapeIdentity(ShapeStrokeIdentity)`: read or copy the logical-shape identity in one step.

## Constraints

- This is an in-memory/geometry payload, not a persistence format: `StrokeAnnotation` remains the serialized contract (`List<double[]> Points`); do not change it.
- Shape metadata semantics must stay identical to the WPF extended-property keys (`ShapeStrokeMetadataKeys`).

## Verification

`OpenNotes.Tests/CoreStrokeGeometryTests.cs` covers geometry over `InkStrokeData`; `ShapeStrokeMetadataTests` covers the WPF adapter round-trip.
