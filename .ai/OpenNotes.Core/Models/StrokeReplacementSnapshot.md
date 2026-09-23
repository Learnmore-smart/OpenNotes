# OpenNotes.Core/Models/StrokeReplacementSnapshot.cs
> Last updated: 2026-09-22 (moved from WPF `Models/` for V6 Task 2; contract unchanged) | Protection: STANDARD

## Purpose

Immutable, session-safe payloads for replacing a recognized freehand stroke without retaining live `Stroke` objects. Moved unchanged into `OpenNotes.Core` for the V6 WinUI 3 migration — the file was already UI-free, so both WPF and WinUI undo paths can share the same snapshot types.

## API

- `enum StrokeReplacementSide { Original, Ideal }` — which side of a recognition pair a snapshot represents.
- `readonly record struct StrokeReplacementPoint(double X, double Y, float PressureFactor = 0.5f)`.
- `sealed class StrokeReplacementSnapshot` — immutable: `Token`, `Side`, `Points`, RGBA, `Width`/`Height`, `IsHighlighter`, `FitToCurve`, `IgnorePressure`; `WithSide`/`WithIgnorePressure` return modified copies; value equality ignores the token.
- `sealed class StrokeReplacementEntry` — wraps one snapshot with `Token`/`Side` convenience accessors.
- `sealed class StrokeReplacementState` — ordered entry list with `FindIndex(token)`, `InsertAt`, `RemoveAt`, `ReplaceAt`, `TryReplaceStrokeQuiet` (missing token = quiet no-op, never appends).

## Important Notes / NEVER Change

- Do not store live stroke references in replacement actions or snapshots.
- Preserve point coordinates, each point's `PressureFactor`, RGBA colour, width/height, highlighter state, `FitToCurve`, and `IgnorePressure` exactly enough for undo restoration.
- Keep tokens session-only; they are not serialized into `StrokeAnnotation`.
- A missing token must be a no-op; never append a replacement.

## Verification

`OpenNotes.Tests/StrokeReplacementProductionTests.cs` covers the production snapshot/token path; `ShapeRecognitionUndoTests` covers undo/redo through it.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Wave 1 immutable token/snapshot contract plus pressure/IgnorePressure fidelity and `StrokePlacement` owner/index metadata. | Codex |
| 2026-09-22 | File moved verbatim from `Models/` to `OpenNotes.Core/Models/` for V6 Task 2; public contract unchanged. | Devin |
