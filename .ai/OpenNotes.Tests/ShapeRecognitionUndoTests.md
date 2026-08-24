# OpenNotes.Tests/ShapeRecognitionUndoTests.cs
> Last updated: 2026-08-23 (Wave 1 production-path quality follow-up GREEN) | Protection: STANDARD

## Purpose

Verify shape-recognition replacement undo/redo with immutable snapshots and session tokens, without constructing a WPF control.

## Open Threads / Resume Context

- **Status:** complete
- **Intent:** Prove in-place restoration, erase-safe no-op behavior, idempotent repeated undo/redo, and the source-level no-live-`Stroke` contract.
- **Next steps:** Preserve the snapshot-only/no-append boundary in later waves; real STA/WPF production coverage is in `StrokeReplacementProductionTests`.

## Important Notes / NEVER Change

- Tests must use deterministic snapshot/token state rather than live WPF input or pointer timing.
- A missing token never appends an ideal stroke or resurrects erased data.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added the Wave 1 RED contract for stable shape replacement undo/redo. | Codex |
| 2026-08-23 | Production implementation reached GREEN: 4 focused tests and 107 full-suite tests passed. | Codex |
| 2026-08-23 | Production-path quality follow-up passed 5/5 STA/WPF placement/pressure tests plus 4/4 shape tests; full suite passes 113/113. | Codex |
