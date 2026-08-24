# OpenNotes.Tests/DocumentSaveCoordinatorTests.cs
> Last updated: 2026-08-23（Wave 2 final review GREEN: close-safe autosave/manual behavior）| Protection: STANDARD

## Purpose

Exercise the production save state machine without WPF: one in-flight task for manual/autosave callers, generation mismatch and retry, failure recovery, timer-style re-entry coalescing, and final-close waiting for the newest persisted snapshot.

## Open Threads / Resume Context

- **Status:** ready_for_next — 14 executable production-state tests pass; no WPF window or user directory is required.
- Tests use deterministic `TaskCompletionSource` gates and in-memory generation lists; no user directory or desktop pointer is required.

## Important Notes / NEVER Change

- Do not replace executable state-machine tests with source-string contracts.
- Close tests must prove an edit during an active save is persisted before close can complete.
- The late-edit test mutates a fake model before notifying the coordinator, matching WPF ink/text event ordering; the persisted values must include the late snapshot.
- The completion-barrier test deterministically observes clean state while `_inFlight` remains active and requires `SaveAsync` to return the existing task.
- Post-cleanup release tests mark the irreversible boundary, retry a failed prepare, call the same cancel/resume guard used by the editor, and require `Failed`/non-resumable state until `MarkSucceeded`.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added Wave 2 revision RED contracts for autosave/manual/close behavior. | Codex |
| 2026-08-23 | Implemented `DocumentSaveCoordinator` and verified coalescing, edit-during-save retry, final-close latest persistence, and failure recovery GREEN. | Codex |
| 2026-08-23 | Added cancellation recovery coverage: a cancelled final-close releases its close request, joins the underlying save, and allows later edits. | Codex |
| 2026-08-23 | Added executable late-model-edit, clean/in-flight completion-window, and `DocumentEditAdmission` lease/cancel/navigation-resume/quiescence tests; 11/11 pass. | Codex |
| 2026-08-23 | Added release-state timeout/retry invariants and stale-navigation journal cancellation through `NavigationCloseCoordinator`; 13/13 pass. | Codex |
| 2026-08-23 | Added deterministic post-cleanup release failure coverage: retry prepare failure and cancel/activate remain blocked until a complete release retry succeeds; 14/14 pass. | Codex |
