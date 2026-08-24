# Services/DocumentReleaseState.cs
> Last updated: 2026-08-23（Wave 2 post-cleanup retry contract GREEN）| Protection: CRITICAL

## Purpose

UI-independent state machine that keeps an editor non-interactive while native resource release is still running or has partially failed. A timeout only stops the caller's wait; it never reopens the editor or permits a second disposal.

## Contract

- `TryBeginRelease()` transitions Active/Failed→Releasing and rejects Releasing/Released callers, so CloseTab/window retries join the tracked task rather than invoking `DisposeAsync` concurrently.
- `CanResumeInteraction` is true only in Active. Releasing, Failed and Released all reject `ActivateTab`, late edits and autosave re-entry.
- `MarkCleanupStarted()` records the irreversible detach/native-dispose boundary. `MarkFailed()` remembers a post-cleanup failure, and `ResetAfterPreReleaseFailure()` can return only a truly pre-cleanup attempt to Active; retry-prepare failure after partial cleanup stays Failed.
- `MarkSucceeded()` publishes Released only after every owner succeeds.

## Evidence

`DocumentSaveCoordinatorTests.ReleaseTimeoutKeepsInteractionBlockedUntilReleaseFinishesAndAllowsOnlyAJoin` and `PostCleanupFailureCannotBeResetAfterRetryPrepareFailureOrCancelActivation` pass deterministically. MainWindow retains tab/window workflow guards through the background release continuation; only settled success removes a tab or requests window `Close()`.

## Wave 2 follow-up plan

- **2026-08-23:** Distinguish a pre-cleanup prepare failure from a post-cleanup/native-dispose failure. Once cleanup has started, a later prepare failure and `CancelClosePreparation` must leave the editor failed/non-resumable; only a complete retry may reach `Released`.

## Never change

Do not make timeout/cancellation reset the state to Active, and do not mark a resource released before the underlying `PdfService.DisposeAsync` completes successfully.
