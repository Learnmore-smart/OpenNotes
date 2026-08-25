# OpenNotes.Tests/HiddenInkTests.cs
> Last updated: 2026-08-23（Wave 2 automated scope complete: gray/default/PDF/UIA contract） | Protection: STANDARD

## Purpose

Verify Hidden Ink model persistence, reveal timing, geometry and ownership rules without requiring pointer hardware.

## Open Threads / Resume Context

- **Status:** ready_for_next — Wave 2 automated scope complete
- Wave 2 adds model/source/PDF contracts for the new neutral-gray default, explicit legacy white round-trip, reveal state exclusion, missing-`/C` production default, and the themed non-eye card vector mark. The source contract also requires stable `HiddenInkToolButton` AutomationId plus localized Automation Name/HelpText; real pointer/timer/eraser/UIA/save-reopen and third-party viewer checks remain environment-dependent.
- 2026-08-24 toolbar normalization keeps the named `PanelTop` card mark but now requires it to inherit the owning toggle foreground, matching every other Lucide toolbar glyph instead of using a per-tool subtle brush.

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
