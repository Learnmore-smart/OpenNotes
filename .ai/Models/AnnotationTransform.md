# Models/AnnotationTransform.cs

> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

Thin `System.Windows.Point` adapter over the UI-free rotation math in
`OpenNotes.Core/Models/AnnotationRotation.cs`. Used by `PdfPageControl` when
rotating ink/shapes around the selection center.

## What It Does

- `RotatePoint` converts WPF `Point` → `PointD`, delegates to
  `AnnotationRotation.RotatePoint`, converts back.
- `NormalizeDegrees` delegates to `AnnotationRotation.NormalizeDegrees`
  (wraps to (−180, 180]).

## Important Notes / NEVER Change

- Rotation of ink/shapes mutates `StylusPoint` coordinates; it must not invent a new persisted stroke field.
- Text/image/sticky rotation is stored as `RotationDegrees` on the annotation model, not here.
- `NormalizeDegrees` semantics are load-bearing for persisted `/WNARotation`
  metadata — keep the delegation in sync with the Core implementation.

## Change History

- 2026-09-22: Task 3 — rotation math moved to `Caelum.Models.AnnotationRotation`
  (Core, `PointD`); this file kept its signatures as a WPF adapter so
  `PdfPageControl` call sites and tests compile unchanged.
