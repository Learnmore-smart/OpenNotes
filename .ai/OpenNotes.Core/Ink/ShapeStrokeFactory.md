# OpenNotes.Core/Ink/ShapeStrokeFactory.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

UI-free shape-tool stroke construction (`Caelum.Ink`), Task 7 Phase B —
turns a completed drag into the `InkStrokeData` set the page commits.
Ports the WPF shape-commit semantics: every part shares one freshly minted
`ShapeGroupId`, uniform colour/size, `FitToCurve=false`,
`IgnorePressure=true` (uniform-width shape ink), sequential
`ShapePartIndex`, and `IsDashedShape` marking.

## API surface (all static)

- `DragThreshold = 4.0` — drags shorter than this (DIPs) are taps (WPF
  parity; the page enforces it, the factory does not).
- `IsOpenKind(kind)` — `Line`/`Arrow`/`DashedLine` only: the open
  single-segment kinds eligible for ruler-edge snapping.
- `BuildPreviewPolylines(kind, start, end, strokeSize)` → raw outline
  segments for the live drag preview: arrow → shaft + head (two
  polylines), everything else → one `StrokeGeometry.BuildShapeOutline`
  outline (`DashedLine` previews as `Line` — preview geometry never
  bakes dashes; WPF drew the outline with a relative dash array).
- `BuildShapeStrokes(kind, start, end, r, g, b, a, strokeSize, dashed,
  segmentConstraint)` → the committed strokes. `dashed=true` OR
  `kind==DashedLine` bakes each segment into per-dash strokes via
  `StrokeGeometry.GetShapeDashPattern` + `BuildDashedPolyline` — persisted
  ink is pre-segmented, not a dash array. `segmentConstraint` rewrites
  each segment pre-bake (the ruler constraint); returning null drops the
  segment. Empty segment list → empty result.

## Constraints / NEVER Change

- Part indices are assigned per emitted stroke in segment order — the
  arrow's shaft is part 0, head part 1; baked dashes number sequentially.
  Selection/undo group-moves resolve membership by `ShapeGroupId`, so all
  parts of one commit MUST share it.
- `FitToCurve=false`/`IgnorePressure=true` are load-bearing: shape ink
  renders straight segments at uniform width (WPF `DrawingAttributes`
  parity). Do not "smooth" them.
- Committed strokes are added quietly by the caller and grouped into ONE
  `InkStrokesAddedAction` — the factory only builds data, never mutates
  the store.
