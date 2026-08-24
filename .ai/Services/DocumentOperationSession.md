# Services/DocumentOperationSession

> Last updated: 2026-08-24 (Wave6 async stale-operation P2, audit continuation) | Protection: STANDARD

## Purpose

Shared editor-operation lease boundary. A lease captures the load session id,
normalized document path, optional live model identity, and a cancellation token.
`Validate` must be called after every await and immediately before a UI/model/
undo/dirty mutation. Beginning a PDF load, releasing a tab/editor, or setting an
editor inactive cancels the previous session so old menu/popup continuations
silently stop.

## Wave6 audit plan

- Keep the deterministic TaskCompletionSource tests for Version History,
  sidebar/page context, and async Undo/Redo reload interleavings as the lease
  contract.
- Audit every transient-owned continuation in `EditorPage.xaml.cs`, including
  thumbnail/outline reads and structural rollback/error paths, for a lease check
  after each await and immediately before UI/model/undo/dirty/error mutation.
- Preserve existing load/release/host cancellation and same-session admission;
  stale callbacks must not log/display old errors or publish dirty/undo state.

## Invariants

- Path comparison is normalized and case-insensitive on Windows.
- A matching session/path is still invalid after cancellation or release.
- Optional identity is reference-checked for live page/model/container objects.
- Lease validation is pure and thread-safe; mutation remains owned by the UI
  dispatcher and existing `DocumentEditAdmission`/save coordinator.

## Verification

The helper and primary integration are green. The audit added stale-exception
guards for Version History, thumbnails, outline/search continuations, lifecycle
save callbacks, and PDF text selection; session-scoped thumbnail markers prevent
an old completion from blocking a replacement page index. Deferred context-menu
callbacks respect the close/navigation admission boundary. The deterministic
lease/source focused filter passes 23/23; Wave7+ remains out of scope and no
commit is made.

## Open Threads / Resume Context

- **Status:** complete for the approved Wave6 automated scope
- **Intent:** finish the Wave6 P2 audit without redoing the existing lease wiring.
- **Next steps:** run the final full test, solution build, i18n, diff check, and
  three-page Editor UIA smoke; record any external foreground/device blockers.
- **Blockers / notes:** do not change PDF annotation format, save coordination,
  Wave7+ files, or commit. External foreground/deactivation evidence remains
  unclaimed unless a real run is available.
