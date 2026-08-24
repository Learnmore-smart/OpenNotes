# Services/DocumentSaveCoordinator.cs
> Last updated: 2026-08-23（Wave 2 final review: close-safe save state machine）| Protection: CRITICAL

## Purpose

Production, WPF-independent save state machine used by EditorPage. It owns the dirty generation, coalesces manual/autosave callers onto one in-flight task, preserves dirty state when an edit arrives during persistence, retries latest state for close/navigation, and propagates failures so callers can block resource release.

## Public API

| Member | Description |
|---|---|
| `MarkDirty()` / `RecordChange(bool)` | Advances the generation and records whether the document remains dirty. A late notification racing final close is retained as dirty (while returning `false`) so an already-mutated model cannot be released stale. |
| `SaveAsync(Func<long, Task>)` | Joins `_inFlight` before checking `_isDirty`, closing the clean/completion observation window; returns one shared `DocumentSaveResult` task. Exceptions remain observable and the dirty state remains recoverable. |
| `SaveUntilCleanAsync(Func<long, Task>, bool)` | Joins any active task even if its callback has already cleared dirty state, retries after generation mismatch, and only succeeds once the latest generation is persisted. Final-close mode blocks new edits during the protocol. |
| `Reset()` / `CancelCloseRequest()` | Resets load state or reopens editing after a failed/timeout close or a resource-release retry. |

## Open Threads / Resume Context

- **Status:** ready_for_next — executable final-review tests pass; integration evidence is recorded in the Wave 2 plan ledger.
- This class is intentionally independent of WPF so timer/manual/close races can be tested with deterministic gates and no real user directory.

## Important Notes / NEVER Change

- Never mark a newer generation clean merely because an older save completed.
- Never clear `_inFlight` before the underlying task has completed; all waiters must observe the same result/exception.
- Final-close callers must await `SaveUntilCleanAsync` before disposing the PdfService or removing a tab; the clean check and `_closeCompleted` transition are atomic.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-23 | Added for Wave 2 revision close-safe autosave/manual coordination. | Codex |
| 2026-08-23 | Verified manual/autosave coalescing, generation mismatch retry, final-close blocking, exception recovery, and close-time joining of an active completion task with deterministic production-state tests. | Codex |
| 2026-08-23 | Final review: late model edits are retained for a latest-generation close retry; `SaveAsync` joins active work before dirty short-circuiting; final close only completes under the clean/in-flight lock. | Codex |
