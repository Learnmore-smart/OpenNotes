# OpenNotes.Core/Models/StrokeReplacementSnapshot.cs
> Last updated: 2026-09-23 (shape identity fields added — ctor params optional, WPF call sites unchanged) | Protection: STANDARD

## Purpose

Immutable, session-safe payloads for replacing a recognized freehand stroke without retaining live `Stroke` objects. Moved unchanged into `OpenNotes.Core` for the V6 WinUI 3 migration — the file was already UI-free, so both WPF and WinUI undo paths can share the same snapshot types.

## API

- `enum StrokeReplacementSide { Original, Ideal }` — which side of a recognition pair a snapshot represents.
- `readonly record struct StrokeReplacementPoint(double X, double Y, float PressureFactor = 0.5f)`.
- `sealed class StrokeReplacementSnapshot` — immutable: `Token`, `Side`, `Points`, RGBA, `Width`/`Height`, `IsHighlighter`, `FitToCurve`, `IgnorePressure`, plus the logical-shape identity `ShapeGroupId`/`ShapeKind`/`ShapePartIndex`/`IsDashedShape` (optional ctor params, default empty/0/false); `GetShapeIdentity()` packs the four; `WithSide`/`WithIgnorePressure` return modified copies preserving identity; equality/hash include the identity fields.
- `sealed class StrokeReplacementEntry` — wraps one snapshot with `Token`/`Side` convenience accessors.
- `sealed class StrokeReplacementState` — ordered entry list with `FindIndex(token)`, `InsertAt`, `RemoveAt`, `ReplaceAt`, `TryReplaceStrokeQuiet` (missing token = quiet no-op, never appends).

## Important Notes / NEVER Change

- Do not store live stroke references in replacement actions or snapshots.
- Preserve point coordinates, each point's `PressureFactor`, RGBA colour, width/height, highlighter state, `FitToCurve`, `IgnorePressure`, AND the shape-identity fields exactly enough for undo restoration — a round-trip that drops `ShapeGroupId`/`IsDashedShape` demotes a grouped/dashed shape to plain ink on the next save.
- Keep tokens session-only; they are not serialized into `StrokeAnnotation`.
- A missing token must be a no-op; never append a replacement.
- New ctor params must stay optional-trailing — WPF `PdfPageControl.CaptureStrokeSnapshot` and tests construct this type directly.

## Verification

`OpenNotes.Tests/StrokeReplacementProductionTests.cs` covers the production snapshot/token path; `ShapeRecognitionUndoTests` covers undo/redo through it; `CoreInkEngineTests` covers identity round-trips (`CaptureSnapshot`→`FromSnapshot`, withers, equality) and erase-undo on grouped strokes.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Wave 1 immutable token/snapshot contract plus pressure/IgnorePressure fidelity and `StrokePlacement` owner/index metadata. | Codex |
| 2026-09-22 | File moved verbatim from `Models/` to `OpenNotes.Core/Models/` for V6 Task 2; public contract unchanged. | Devin |
| 2026-09-23 | Added `ShapeGroupId`/`ShapeKind`/`ShapePartIndex`/`IsDashedShape` + `GetShapeIdentity()` through ctor/withers/equality — snapshot round-trips no longer corrupt grouped/dashed shape identity. | Devin |
