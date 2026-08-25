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

- **Status:** complete (2026-08-25 OpenNotes 5.2.1 large-PDF crash hotfix)
- Windows event 1026 identified autosave capturing the session between `Cancel`
  and the following `Begin`. `Cancel` now cancels without disposing the current
  CTS; `Begin`/`Dispose` remain its retirement owners, so a racing capture returns
  a cancelled lease that validation rejects instead of terminating the process.
- The exact 50.04 MiB, 1,353-page textbook opened in the Release build and stayed
  alive for a 90-second hold across the 60-second autosave tick with no .NET crash.

## Bug Fixes

| Date | Bug | Cause | Fix |
|---|---|---|---|
| 2026-08-25 | Large PDF load could terminate OpenNotes during autosave | `Cancel` disposed the current CTS before `Begin` replaced it, so `Capture().Token` threw `ObjectDisposedException` | Keep the inactive current CTS cancelled-but-alive until `Begin` or `Dispose` retires it |
