# OpenNotes.Tests/StickyNoteInteractionTests.cs
> Last updated: 2026-08-24 (Wave6 dual-review RED→GREEN closure) | Protection: STANDARD

## Purpose

Wave6 test-first contracts for Sticky Note model/PDF compatibility, page-bounded placement,
marker hit testing, pointer/keyboard capture, explicit Save/Cancel/Delete controls, and the
production annotation lifecycle. The PDF test creates a temporary production PDF and
round-trips CJK text, stable Id, DIP geometry and RGB marker data.

## Open Threads / Resume Context

- **2026-08-28:** The text capture-loss regression now drives the annotation container border drag controller directly; it no longer searches for the retired dotted move handle.

- **2026-08-24 UI repair GREEN:** semantic primary/secondary/destructive Sticky actions and a named SizeAll drag header are implemented. Real STA coverage proves header drag changes only Popup offsets while model coordinates and dirty state remain unchanged; focused Sticky/transient/localization tests pass 36/36.

- **Wave6 dual-review RED plan:** add real STA regressions for marker LostMouseCapture,
  Escape/deactivation rollback, repeated reopen, live-session Save/Cancel/Delete after reload,
  malicious duplicate IDs, visible EN/ZH/FR popup metadata refresh, and focus/HC semantics.
  Resumed implementation must also prove that a stale popup/container cannot create a phantom
  undo/dirty action after a new document session replaces the page.

- **Status:** green for focused automated scope (`StickyNoteInteractionTests` plus
  `TransientUiSourceTests` = `20/20`; full suite `241/241`)
- **Intent/result:** model JSON/PDF-compatible identity, geometry/colour, page-bounded
  marker interaction, pointer/keyboard capture, context-menu deletion and explicit
  Save/Cancel/Delete editor lifecycle are covered by source contracts plus live STA/WPF
  marker/editor controls, including UIA button peers, drag/keyboard clamp, undo/redo,
  and cancel-on-transient-close behavior. Dual-review regressions now cover deterministic STA
  Sticky capture rollback/reopen, text drag/resize cancellation on capture loss/deactivation,
  stale session Save isolation, duplicate/empty PDF `/NM` repair and live EN/ZH/FR popup
  Content/Tooltip/Name/HelpText refresh.
- **Verification:** full repository suite/build/i18n are green; a generated three-page PDF
  editor UIA smoke is green. External foreground/deactivation Sticky smoke remains unclaimed
  because the dedicated Sticky smoke script is absent in this checkout.
- **Blockers / notes:** keep 'StickyNoteAnnotation' fields and PDF/sidecar ownership
  compatible; no Wave7+ files are in scope.

## Important Notes / NEVER Change

- Preserve DIP X/Y/Text and the existing /Text owned-prefix PDF path.
- Tests may inspect source contracts, but production behavior must remain testable through
  the real WPF controls where STA coverage is available.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-24 | Added Wave6 RED contracts for Sticky Note lifecycle and interaction boundaries. | Codex |
| 2026-08-24 | GREEN: stable Id/size/colour persistence, marker capture/keyboard/context Delete, explicit popup lifecycle, and real STA/WPF marker/editor paths implemented; Sticky class passed 7/7 and combined focused filter passed 11/11. | Codex |
| 2026-08-24 | Dual-review GREEN: shared capture rollback, live session guards, duplicate PDF/UIA identities, localized popup metadata/focus and STA regressions; combined focused `20/20`, full `241/241`. | Codex |
| 2026-08-24 | Restyled runtime editor actions and added isolated draggable popup-header coverage. | Codex |
