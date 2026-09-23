# Models/ShapeStrokeMetadata.cs
> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

WPF facade over the UI-free shape-identity primitives now living in `OpenNotes.Core` (V6 Task 2). `ShapeStrokeIdentity`, the stable property keys and the dash-path math moved to Core; this class keeps only the live-`Stroke` extended-property interop (`Apply`/`Read`) and `System.Windows.Point`-based dash builders used by the shape tool and tests.

## API

- `Apply(Stroke, groupId, kind, partIndex, isDashed)` — writes the four stable `ShapeStrokeMetadataKeys` GUIDs as extended properties.
- `Read(Stroke) → ShapeStrokeIdentity` — reads them back (empty group/kind = ordinary ink).
- `BuildDashedLine(start, end, dash, gap)` / `BuildDashedPolyline(points, dash, gap)` — convert `Point`→`PointD` and delegate to `StrokeGeometry.BuildDashedPolyline`, converting results back.

## Invariants

- Empty group/kind metadata means ordinary legacy ink.
- All parts of one arrow or dashed line share one group id and have distinct part indices.
- Dashed gaps contain no ink and therefore do not hit or erase.
- Property keys are stable GUIDs across releases and copied into `StrokeAnnotation` for persistence — NEVER regenerate them.
- Public API surface is unchanged; existing callers and tests compile unmodified.

## Open Threads / Resume Context

- **Status:** complete (2026-09-22) — V6 Task 2 port; pure logic delegated to `OpenNotes.Core` (`Caelum.InkGeometry`), WPF `Stroke` property storage retained here.
- BuildDashedPolyline carries dash/gap phase across arbitrary polyline corners and BuildDashedLine delegates to it (unchanged).
