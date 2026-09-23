# OpenNotes.Core/Models/AnnotationRotation.cs

> Last updated: 2026-09-22 | Protection: STANDARD

## Purpose

UI-free annotation rotation math (`internal static`), moved from the WPF
`Caelum.Models.AnnotationTransform` so the Core `Caelum.Pdf.PdfService` can
normalize `/WNARotation` metadata without a `System.Windows` dependency.

## What It Does

- `RotatePoint(PointD point, PointD center, double degrees)` — rotates a DIP
  point around a center (same formula as the legacy WPF helper).
- `NormalizeDegrees(double degrees)` — wraps to (−180, 180]; NaN/∞ → 0.
  Load-bearing: saved `/WNARotation` values must match the legacy
  normalization byte-for-byte.

## Dependencies

- `Caelum.InkGeometry` (`PointD`) only.
- WPF `Models/AnnotationTransform.cs` is now a thin `System.Windows.Point`
  adapter over these methods.

## Important Notes / NEVER Change

- **NEVER** change `NormalizeDegrees` semantics — persisted annotation
  rotation depends on identical wrapping.

## Change History

- 2026-09-22: Created for Task 3 — the only piece of `AnnotationTransform`
  the Core PDF service needed (rotation metadata on save).
