# OpenNotes.Tests/StrokeReplacementProductionTests.cs
> Last updated: 2026-08-23 (Wave 1 final stale-placement P1 GREEN; pointer evidence open) | Protection: STANDARD

## Purpose

Exercise the real STA/WPF `PdfPageControl` and nested `EditorPage` undo actions for stable stroke placement identity, pressure/`IgnorePressure` snapshot fidelity, cross-page ownership, and protected stroke access.

## Open Threads / Resume Context

- **Status:** final P1 stale-placement and same-token/side conflict follow-up complete for automated scope after deliberate RED/GREEN; 14 STA/WPF production tests pass
- **Intent:** Keep shape recognition undo correct after erase/delete/cross-page transfer, including after shape redo creates a fresh live stroke; resolve ordinary actions by stable token/side/owner without regenerating tokens or changing collection order.
- **Next steps:** Keep the dedicated shape/pointer smoke open until foreground/device injection is available; no production test is weakened for that external gap.
- **Blockers / notes:** Tests use STA WPF controls and reflection only to invoke the existing private production undo action types; they do not construct a parallel replacement fixture. The test fixture calls `WindowsEnvironment.NormalizeForWpf()` before WPF static initialization.

## Important Notes / NEVER Change

- Shape undo remains snapshot-only; ordinary live-stroke actions may retain `StrokePlacement` records that include the live stroke plus token/side/index/owner.
- Cross-page transfers must capture the current source placement after shape replacement and use exact target identity for add/remove/rollback; unrelated same-token/same-side targets are conflicts and cannot trigger source deletion.
- Original snapshots must retain every stylus point's `PressureFactor` and `DrawingAttributes.IgnorePressure`.
- `GetStrokes()` keeps the historical `StrokeCollection` API but returns a defensive copy; mutating that copy must not change page state.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added deterministic-token coverage for same-token/different-side add rejection, cross-page target conflicts, and cross-page redo/undo movement after shape redo. The focused production run is intentionally RED (3/3 failures). | Codex |
| 2026-08-23 | Tightened `AddStrokeQuiet(StrokePlacement)` to require the owning page and matching token/side; cross-page transfer now rolls back on conflicts and moves the placement returned by current resolution. The new deterministic tests pass 3/3 and the Wave 1 focused filter passes 19/19. | Codex |
| 2026-08-23 | Added three real production-path cross-sequence tests for erase/delete/cross-page placement redo after shape redo; the focused run is intentionally RED (3/3 failures, each leaves two strokes because stale live references are not resolved). | Codex |
| 2026-08-23 | Fixed placement redo to resolve current strokes by owner/token/side and make token re-add idempotent; the three cross-sequence tests pass 3/3 and the combined Wave 1 focused filter passes 16/16. | Codex |
| 2026-08-23 | Added STA/WPF production-path RED coverage for erase/delete/cross-page restore, pressure fidelity, and collection protection. | Codex |
| 2026-08-23 | Production placement/token implementation reached GREEN: 5/5 tests pass, including real `StrokeReplacedAction`, and the full suite passes 113/113. | Codex |
| 2026-08-23 | Added deliberate same-token/same-side cross-page conflict RED/GREEN and exact live-reference rollback; the combined Wave 2/Wave 1 production filter passes 44/44. | Codex |
| 2026-08-23 | Added STA/WPF multi-selection transaction coverage for both a later target identity conflict and a later stale source capture; all earlier adds roll back and no transfer is reported. | Codex |
