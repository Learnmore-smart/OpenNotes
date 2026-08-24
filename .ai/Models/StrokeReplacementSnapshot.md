# Models/StrokeReplacementSnapshot.cs
> Last updated: 2026-08-23 (Wave 1 quality follow-up GREEN; pointer evidence open) | Protection: STANDARD

## Purpose

Immutable, session-safe payloads for replacing a recognized freehand stroke without retaining live WPF `Stroke` objects.

## Open Threads / Resume Context

- **Status:** complete (Wave 1 quality follow-up)
- **Intent:** Keep token-carrying, immutable snapshots and quiet replacement semantics as the stable shape-recognition undo/redo boundary.
- **Next steps:** Wave 2+ must preserve this session-only contract; the dedicated shape-recognition pointer smoke remains unclaimed because its script is not present.
- **Blockers / notes:** A missing token must be a no-op; never append a replacement. Tokens are session-only and must not alter persisted annotation fields.

## Agent Decisions / Thoughts

- **2026-08-23:** Keep immutable snapshot data independent of WPF controls so the replacement sequence can be tested deterministically without a live `InkCanvas`.
- **2026-08-23:** Wave 1 implemented copied point/style/token/side data, reference-identity token tracking in `PdfPageControl`, and no-append quiet replacement. Focused shape tests passed 4/4; the full suite passed 107/107.
- **2026-08-23:** Quality follow-up found that live-stroke restore paths regenerated tokens and dropped pressure/IgnorePressure; the fix must preserve identity through action-owned placements and page transfer.
- **2026-08-23:** Quality implementation is complete: snapshots carry pressure/IgnorePressure, page replacement reuses `StrokeReplacementState`, and automated production coverage passes 5/5 with full suite 113/113.

## Important Notes / NEVER Change

- Do not store live `System.Windows.Ink.Stroke` references in replacement actions or snapshots.
- Preserve point coordinates, each point's `PressureFactor`, RGBA color, width/height, highlighter state, `FitToCurve`, and `DrawingAttributes.IgnorePressure` exactly enough for undo restoration.
- Keep tokens session-only; they are not serialized into `StrokeAnnotation`.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Planned Wave 1 immutable token/snapshot contract. | Codex |
| 2026-08-23 | Implemented the token/snapshot replacement contract and recorded focused GREEN evidence. | Codex |
| 2026-08-23 | Quality follow-up added pressure/IgnorePressure fidelity, production reuse of `StrokeReplacementState`, and `StrokePlacement` owner/index metadata for ordinary undo paths; production STA tests passed 5/5. | Codex |
