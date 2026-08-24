# Services/NavigationCloseCoordinator.cs
> Last updated: 2026-08-23（Wave 2 stale-journal cancellation GREEN）| Protection: STANDARD

## Purpose

UI-independent barrier for a Back navigation request. Preparation is provisional until the journal still reports `CanGoBack`; a stale queued click cancels close preparation and does not navigate.

## Contract

`TryNavigateBackAsync(prepareAsync, canGoBack, cancelPreparation, navigateAsync)` awaits preparation, checks the journal after the await, invokes cancellation when the route disappeared, and only then performs navigation. It preserves the editor's autosave/input/coordinator admission when no transition occurs.

## Evidence

`DocumentSaveCoordinatorTests.NavigationPrepareSuccessButStaleJournalCancelsPreparationWithoutNavigating` passes deterministically without WPF window state or a user directory.
