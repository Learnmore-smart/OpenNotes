# Models/ShapeStrokeMetadata.cs
> Last updated: 2026-08-30 | Protection: STANDARD

## Purpose

Defines stable WPF stroke property keys for logical shapes and builds real dashed-line segments separated by empty gaps.

## Invariants

- Empty group/kind metadata means ordinary legacy ink.
- All parts of one arrow or dashed line share one group id and have distinct part indices.
- Dashed gaps contain no ink and therefore do not hit or erase.
- Property keys are stable across releases and copied into `StrokeAnnotation` for persistence.

## Open Threads / Resume Context

- **Status:** complete (2026-08-31)
- BuildDashedPolyline carries dash/gap phase across arbitrary polyline corners and BuildDashedLine delegates to it.
