# Services/PdfSaveCoordinator.cs
> Last updated: 2026-08-23（Wave 2 automated scope complete）| Protection: CRITICAL

## Purpose

Process-wide path-keyed serialization boundary for PDF writes. Same normalized full path (case-insensitive on Windows) has one active save; different paths use independent gates.

## Public API

| Member | Description |
|---|---|
| `RunExclusiveAsync(string path, Func<Task> save)` | Runs one complete save under the normalized path gate and propagates delegate exceptions after releasing it. |
| `RunExclusiveAsync(string path, Func<Task> save, CancellationToken)` | Cancels only a waiter; an active delegate keeps its lease and the cancelled waiter never enters the save callback. |
| `RunExclusiveAsync(IReadOnlyCollection<string> paths, Func<Task> save[, CancellationToken])` | Acquires distinct normalized paths in ordinal case-insensitive order; source/target operations cannot deadlock in crossed order, while unrelated paths still overlap. |
| `NormalizePath(string path)` | Internal deterministic full-path normalization used by tests and the coordinator. |
| `ActiveLeaseCount` | Internal deterministic diagnostic used by tests to prove a same-path waiter entered the map before the delegate can run. |

## Open Threads / Resume Context

- **Status:** ready_for_next — Wave 2 final acceptance follow-up green for automated scope.
- The map is case-insensitive and stores one semaphore per normalized path. User counts are incremented before waiting, so an idle-entry cleanup cannot remove a gate while a waiter remains. Release happens in nested `finally` blocks on success and exception; idle map entries are removed after semaphore release.
- Focused and expanded coordinator/PDF/Hidden Ink tests cover same-path serial, different-path parallel, relative/`.`/`..` aliases, whitespace rejection, exception cleanup, cancelled waiters, disposal races, stream ownership, and save/reopen parsing. External pointer/UIA/third-party viewer evidence remains environment-dependent.
- Physical PDF creation is covered by a RED/GREEN barrier: removing `CreateBlankPdfAsync`'s wrapper leaves only the held lease (`ActiveLeaseCount=1`), while the restored implementation registers the queued create lease before waiting (`ActiveLeaseCount>=2`).
- Multi-path import tests hold a source read lease, serialize crossed `A<-B`/`B<-A` calls without deadlock, and reject source==target. Cancellation and delegate failure release every acquired semaphore/map user.

## Important Notes / NEVER Change

- Normalize with `Path.GetFullPath`; compare keys case-insensitively on Windows.
- Do not use one global semaphore for every document.
- Gate release/removal must happen in `finally`, including delegate exceptions.
- Keep the existing PdfService temp-file/atomic-replace and stream ownership semantics.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Created Wave 2 handoff mirror before implementation. | Codex |
| 2026-08-23 | Implemented safe path-keyed gate lifecycle and exception cleanup; focused coordinator tests pass. | Codex |
| 2026-08-23 | Added cancellation-aware waiting and normalized alias/whitespace coverage; PdfService disposal admission now prevents post-dispose native reloads. | Codex |
| 2026-08-23 | Added deterministic active-lease diagnostics and blank-document physical-write coordination coverage. | Codex |
| 2026-08-23 | Added sorted multi-path source/target admission, atomic replacement failure coverage, and crossed-import deadlock barriers; all focused coordinator/PDF tests pass. | Codex |
