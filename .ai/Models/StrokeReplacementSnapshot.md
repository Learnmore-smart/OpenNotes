# Models/StrokeReplacementSnapshot.cs → OpenNotes.Core/Models/StrokeReplacementSnapshot.cs
> Last updated: 2026-09-22 (moved to OpenNotes.Core for V6 Task 2) | Protection: STANDARD

## Purpose

MOVED: this file now lives at `OpenNotes.Core/Models/StrokeReplacementSnapshot.cs` — see `.ai/OpenNotes.Core/Models/StrokeReplacementSnapshot.md`. The contract is unchanged; the move is part of the V6 WinUI 3 migration (Task 2) because the snapshots were already UI-free.

## Important Notes / NEVER Change

- Do not store live `System.Windows.Ink.Stroke` references in replacement actions or snapshots.
- Preserve point coordinates, each point's `PressureFactor`, RGBA color, width/height, highlighter state, `FitToCurve`, and `DrawingAttributes.IgnorePressure` exactly enough for undo restoration.
- Keep tokens session-only; they are not serialized into `StrokeAnnotation`.
- A missing token must be a no-op; never append a replacement.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Planned Wave 1 immutable token/snapshot contract; implemented token/snapshot replacement; added pressure/IgnorePressure fidelity and `StrokePlacement` owner/index metadata (5/5 STA tests). | Codex |
| 2026-09-22 | File moved verbatim from `Models/` to `OpenNotes.Core/Models/` for V6 Task 2; public contract unchanged. | Devin |
