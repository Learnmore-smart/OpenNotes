# Services/DocumentEditAdmission.cs
> Last updated: 2026-08-23（Wave 2 close/navigation admission）| Protection: CRITICAL

## Purpose

WPF-independent edit admission used by `EditorPage` to close the race where a WPF ink/text/undo event changes the live model while a final save is awaiting disk. Mutating handlers take a short lease; close/navigation first blocks new leases, waits for active leases to quiesce, and only then accepts the latest persisted generation.

## Contract

- `TryEnter(out IDisposable)` returns a lease only while the document is open.
- `BeginClose()` rejects queued mutations and `WaitForQuiescenceAsync()` joins work already admitted.
- `CancelClose()` reopens the admission after a failed/timeout close, including a release failure after the save loop completed. Navigation uses the same transition when an editor returns from the frame back stack, reopening input only after its latest persisted snapshot is already safe.
- `CompleteClose()` permanently rejects later edits after the final clean snapshot.

## Important Notes / NEVER Change

- The lease is synchronous and intentionally small; it must not be held across unrelated UI dialogs.
- `DocumentSaveCoordinator.RecordChange` still retains a late generation when a model event has already mutated the live model. The admission prevents normal UI input; the retained generation is the defensive fallback for queued callbacks.
- This class has no WPF dependency so close/edit state is tested deterministically without a user data directory.

## Verification

`DocumentSaveCoordinatorTests.EditAdmissionBlocksQueuedMutationsUntilAFailedCloseIsCancelled` and `CompletedNavigationAdmissionCanBeResumedForTheActiveEditor` exercise the production class: an admitted edit is counted, close blocks a second edit, cancellation permits a retry, and a completed navigation close can be reopened.
