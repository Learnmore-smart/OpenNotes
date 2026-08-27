# OpenNotes.Tests/HiddenInkTests.cs
> Last updated: 2026-08-23（Wave 2 automated scope complete: gray/default/PDF/UIA contract） | Protection: STANDARD

## Purpose

Verify Hidden Ink model persistence, reveal timing, geometry and ownership rules without requiring pointer hardware.

## Open Threads / Resume Context
- **Status:** ready_for_next — the toolbar card/reveal vector is named `HiddenInkReveal`; existing Hidden Ink persistence/reveal/ownership contracts remain unchanged. Focused coverage passes 10/10.

- **Status:** ready_for_next — Wave 2 automated scope complete; issue-3 overlay boundary verified
- Wave 2 adds model/source/PDF contracts for the new neutral-gray default, explicit legacy white round-trip, reveal state exclusion, missing-`/C` production default, and the themed non-eye card vector mark. The source contract also requires stable `HiddenInkToolButton` AutomationId plus localized Automation Name/HelpText; real pointer/timer/eraser/UIA/save-reopen and third-party viewer checks remain environment-dependent.
- The issue-3 production-path popup regression confirms an interactive Hidden Ink `Polyline` consumes its own click without arming the native InkCanvas dismissal flag; `HiddenInkTests` pass 10/10 alongside popup 4/4.
- 2026-08-24 toolbar normalization kept the named card mark inheriting the owning toggle foreground, matching every other Lucide toolbar glyph instead of using a per-tool subtle brush. Task 4 gives that card/reveal geometry the explicit `HiddenInkReveal` name.

## Important Notes / NEVER Change

- Hidden Ink remains a separate collection and must not be treated as ordinary strokes.
- Temporary reveal state is never serialized.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Documented Hidden Ink regression tests. | Codex |
| 2026-08-23 | Added Wave 2 gray-default/legacy-white/transient-reveal RED contracts. | Codex |
| 2026-08-23 | Verified production missing-`/C` fallback and localized UIA Name/HelpText alongside the stable smoke AutomationId. | Codex |
| 2026-08-24 | Updated the themed card-mark contract from a raw Path tag to the named Lucide `PanelTop` vector. | Codex |
| 2026-08-24 | Updated the card-mark contract to owner-foreground Lucide styling while preserving its localized metadata and stable ID. | Codex |
| 2026-08-26 | Documented the interactive Hidden Ink overlay boundary for popup dismissal; overlay clicks cannot leave native-ink pending state. | Codex |
