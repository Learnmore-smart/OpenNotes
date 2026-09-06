# Models/AnnotationTransform.cs

> Last updated: 2026-09-05 | Protection: STANDARD

## Purpose

Pure geometry helpers for selection rotation. Used by `PdfPageControl` when rotating ink/shapes around the selection center.

## What It Does

- `RotatePoint` rotates a DIP point around a center by degrees.
- `NormalizeDegrees` wraps to (−180, 180].

## Important Notes / NEVER Change

- Rotation of ink/shapes mutates `StylusPoint` coordinates; it must not invent a new persisted stroke field.
- Text/image/sticky rotation is stored as `RotationDegrees` on the annotation model, not here.
