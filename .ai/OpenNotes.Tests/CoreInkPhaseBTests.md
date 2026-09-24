# OpenNotes.Tests/CoreInkPhaseBTests.cs
> Last updated: 2026-09-23 | Protection: STANDARD

## Purpose

Headless NUnit coverage for Task 7 Phase B — the UI-free half of the
WinUI ink tools (`Caelum.Tests`, 44 tests). Pins the WPF-parity numbers
the WinUI host calls so the port can't silently drift.

## Coverage map

- **Selection transform math** — `TranslateSpinePoints` (pressure kept),
  `ScaleSpinePoints` (uniform scale about anchor, pressure forced 0.5 —
  WPF InkCanvas parity), `RotateSpinePoints` (90° case, pressure kept),
  `ScalePoint`, `GetRenderedStrokeBounds` (spine bounds + rendered
  half-width), `GetSelectionBounds` (union), `GetRotateHandlePoint`
  (22 DIP above top-centre), `GetOppositeCorner` (all four handles),
  `GetSelectionCornerHandleRects`/`TryGetResizeHandleIndex` (8×8 corner
  rects, long-edge midpoint is not a handle).
- **Lasso/marquee containment** — `IsStrokeInsidePolygon` (bounds-fit OR
  ≥60% spine points), `IsStrokeInsideRect` (full containment OR ≥70%),
  `IsContainerInsidePolygon` (full/center/≥2 corners),
  `HitTestClosedOrBounds` (closed ring → polygon interior test; open
  stroke → bounds test).
- **Hidden ink** — `HiddenInkStore` sanitize (Id/Size/A/RevealDurationMs
  defaults + Id de-dup), `IndexOf` by Id (clone resolves same slot),
  clamped `InsertQuiet` + `Changed`, `HiddenInkIntersectsEraser`
  (point-in-inflated-rect, segment crossing, miss, degenerate), and the
  `HiddenInkAdded`/`Removed`/`RemovedBatch` undo actions incl. index
  restore.
- **Shape commit** — `ShapeStrokeFactory.BuildShapeStrokes` identity
  stamping (shared `ShapeGroupId`, `ShapeKind` name, sequential
  `ShapePartIndex`, `IsDashedShape`), arrow shaft+head, dashed baking
  (>1 segment, `DashedLine` forces dashed), `segmentConstraint` dropping
  segments, closed-polygon outline closure, `IsOpenKind`.
- **Laser** — `LaserInkFade` hold/fade curve (0.15 s hold → linear to 0
  over 0.9 s), `IsExpired`, `animate=false` hard-expire, WPF constants
  (red `FF3B30`, thickness 3, 60-polyline cap).
- **Ruler** — `ConstrainPointsToRuler`: start-inside → null, crossing →
  clipped at first quad-edge intersection (t-preserved pressure), near
  edge → projected onto nearest long edge, far → same list back.
- **Undo ordering** — every Phase B action class
  (`InkStrokesAdded`/`Removed`/`Erased`, `InkSelectionMove`/`Resize`/
  `Rotate`/`CrossPageMove`, `InkStrokesStyleChanged`, hidden-ink trio) +
  a mixed draw→erase→move→hidden-ink sequence undone then redone in
  order, verifying z-order, positions and store membership end-to-end.

## Constraints

- UI-free only — no WinUI/WPF application types are exercised here;
  `AnnotationTransformTests`/`WpfCoreEraserParityTests` hold the
  `System.Windows` side.
- `InkSelectionMoveAction` on a stroke already removed from the store is
  a deliberate no-op test — the skip is part of the WPF contract.
