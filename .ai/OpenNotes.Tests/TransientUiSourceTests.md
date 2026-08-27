# OpenNotes.Tests/TransientUiSourceTests.cs

## Wave6 async stale-operation P2 (2026-08-24) — audit continuation

Source contracts require `DocumentOperationSession` capture/validation
around Version History, sidebar/page context, PDF context/structural async
callbacks, and Undo/Redo. Deterministic lease tests live in
`DocumentOperationSessionTests`; stale callbacks must be silent after reload and
same-session callbacks must preserve normal busy/reentry behavior.

The continuation adds precise source contracts for Version History's final
toast/dirty publication, session-scoped thumbnail cleanup, stale PDF-search
exceptions, autosave/print lease ownership, and deferred context-menu admission.
Each contract was run RED before its production guard and is GREEN in the final
23-test focused filter.
> Last updated: 2026-08-24 (Wave6 dual-review RED→GREEN closure) | Protection: STANDARD

## Purpose

Source-level and lightweight production contracts for closing every editor transient
surface on Escape, outside click, tab/navigation/unload, and MainWindow.Deactivated.

## Open Threads / Resume Context

- **Wave6 dual-review RED plan:** add source/STA contracts for shared interaction cancellation
  across Sticky/text/selection drags, MainWindow deactivation and LoadPdf isolation, plus exact
  Unfix coverage for text color, formatting ComboBoxes and PDF viewer context menus. Resumed
  implementation must cancel before `PagesContainer.Children.Clear()` and re-fix only on a live
  reopen path.

- **Status:** green for the Wave6 async continuation (`TransientUiSourceTests`
  plus `DocumentOperationSessionTests` = `23/23`; the final full suite is
  `258/258`)
- **Intent/result:** unified weak registry, Escape/outside/lifecycle closure,
  MainWindow.Deactivated/tab isolation and owner-safe PopupZOrder exact-Unfix hooks are
  implemented and compile-tested. Additional source/STA contracts prove cancellation before
  page clear, live reopen hook idempotency, text/selection/Sticky capture rollback and stale
  popup session isolation.
- **Verification:** full repository suite/build/i18n are green; a generated three-page
  editor UIA smoke is green. External foreground/deactivation focus-loss smoke remains
  unclaimed because foreground activation was unavailable and the dedicated Sticky smoke
  script is absent.
- **Blockers / notes:** closing transient UI must not close tabs or discard ordinary
  document edits; Sticky editor closure follows its explicit Cancel contract.

## Wave6 async stale-operation continuation

- Version History callbacks hold edit admission while restoring snapshots, and
  validate before/after every asynchronous read, before toast/dirty/undo/error
  publication. Sidebar/PDF context bindings reject inactive, released, or
  interaction-blocked hosts.
- Thumbnail callbacks validate the live recycled item/model and use a
  session-scoped loading marker, so an old completion cannot publish into a
  replacement row or suppress the replacement page's render.
- Search selection, outline errors, navigation/close saves, and autosave
  diagnostics publish only while their captured lease is live; stale exceptions
  are silent. No commit and no Wave7+ changes.

## Important Notes / NEVER Change

- Keep PopupZOrderHelper owner/z-order behavior and exact Unfix hook semantics.
- Saving dialogs/modal workflows remain outside the transient sweep.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-27 | Normalized source files from CRLF to LF in the shared reader so multiline fail-closed contracts are checkout-independent. | Codex |
| 2026-08-24 | Added Wave6 RED contracts for deactivation and registry lifecycle. | Codex |
| 2026-08-24 | GREEN: registry/lifecycle/deactivation contracts pass 4/4 with Sticky focused tests (11/11 combined); no commit made. | Codex |
| 2026-08-24 | Dual-review GREEN: shared capture/load/session/unfix contracts pass in the 20-test focused filter; full 241/241 and i18n are green, with external Alt-Tab unclaimed. | Codex |
| 2026-08-24 | Wave6 async P2 RED→GREEN: five deterministic lease tests plus transient source contracts pass 23/23; Version History, thumbnail/session, search, lifecycle, autosave/print, and deferred context barriers are guarded. Full/build/i18n/UIA evidence is recorded in PROJECT_CONTEXT; no commit. | Codex |
