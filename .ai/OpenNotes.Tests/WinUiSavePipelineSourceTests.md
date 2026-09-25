# OpenNotes.Tests/WinUiSavePipelineSourceTests.cs
> Last updated: 2026-09-24（V6 Task 9 Phase A — WinUI save/close source contract）| Protection: STANDARD

## Purpose

Source contract pinning the WinUI save/load pipeline and close/dirty
protocol so a later refactor cannot silently drop the pieces the WPF
editor relies on. Reads the `OpenNotes.WinUI` tree (not the WPF sources);
the behavioural half lives in `DocumentSaveCoordinatorTests` because the
coordinator/admission/release/navigation types are shared Core services.

## What It Pins

- `EditorPageExposesTheSharedSaveAndAutosavePipeline` — coordinator stack
  fields (`DocumentSaveCoordinator`/`DocumentEditAdmission`/
  `DocumentReleaseState`/`_lifecycleGate`/`_saveGate`/`_autoSaveInFlight`),
  repeating `DispatcherQueueTimer` + `Interlocked` tick guard + 15s floor,
  `ApplySettings()` re-arm, `AutoSaveAsync`/`SaveCurrentDocumentWithLeaseAsync`/
  `SaveCurrentDocumentCoreAsync`, atomic `SaveAnnotationsToPdfAsync` +
  version sidecar ordering, `SavePdf_Click` + Ctrl+S, the five localized
  toast/dialog keys, coordinator `Reset()` on document load.
- `EditorPageImplementsTheCloseProtocol` — `PrepareForNavigationAsync`/
  `PrepareForCloseAsync`/`ReleaseResourcesAsync`/`CancelClosePreparation`/
  `ResumeDocumentInteraction`, admission close → quiescence →
  `DispatcherQueueBarrierAsync` → `SaveUntilCleanAsync(finalClose:true)` →
  `CompleteClose` ordering, release-state transitions, awaited
  `_pdfService.DisposeAsync`, session cancel, `DeferredTeardownAsync`.
- `EditorPageGuardsDocumentMutatingOperations` — `TryBeginDocumentEdit`
  leases inside `PushUndoAction`/`PerformUndoAsync`/`PerformRedoAsync`/
  `InsertExternalDocumentAsync`/`RotateCurrentPage_Click` plus the
  `_documentSaveCoordinator.IsDirty` flush before binary PDF rewrites.
- `PushUndoActionRecordsDirtyStateOnBothPaths` — the spec-fix contract:
  `_documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty)` on
  the blocked path AND `ApplyDirtyStateForAction(action)` on the admitted
  path (helper pins `RecordChange` + `SyncDirtyStateMirror`).
- `InkMutatedMarksDirtyOutsideAnnotationLoad` — `MarkDirty()` inside
  `if (!_isLoadingAnnotations)` with unconditional
  `InvalidateThumbnail(page.PageIndex)` after it.
- `StructuralOperationsPushDocumentSnapshotUndo` — `DocumentSnapshotAction`
  class surface (`SetOperationLease`/`CompletedOperationLease`/
  `LeavesDocumentDirty => false`), `ApplyDocumentSnapshotAsync`/
  `WriteDocumentBytesAsync`/`ReloadDocumentForOperationAsync`,
  `RecentFilesService.UpdateMetadata`, `PageBookmarkService.Replace`, the
  insert method's before/after byte + bookmark snapshots and rollback
  writes, rotate's snapshot push, and the undo/redo
  `SetOperationLease`/`CompletedOperationLease` handoff.
- `MainWindowRunsTheTabAndWindowCloseProtocol` — workflow markers,
  `AppWindow.Closing` intercept + `args.Cancel`, bounded prepare/release
  waits, timed-out continuations, `RemoveTabAfterResourcesReleased`,
  `NavigationCloseCoordinator.TryNavigateBackAsync`, `Main.FileAutoSaved`,
  `ResumeDocumentInteraction` on re-activation.
- `CollectAnnotationsCoversAllEightCollections` — strokes, hidden inks,
  texts, sticky notes, highlights, images, text markups, area highlights.
- `SaveButtonIsWiredInXaml` — `SavePdfButton` carries `Click` and no
  longer ships `IsEnabled="False"`.
- `NavigationCloseCoordinatorLivesInCore` — the type moved to
  `OpenNotes.Core/Services/`; the WPF facade copy is deleted.

## Open Threads / Resume Context

- **Status:** GREEN — 10/10 pass in the 700-test suite (post residual pass).

## Change History
- 2026-09-25 Residual pass: insert-rollback asserts repointed at the shared
  `RollbackStructuralOperationAsync` body (import now calls
  `TryRollbackStructuralOperationAsync`); rotate body asserts adjusted for
  hoisted rollback state + gained a `TryRollback` pin; dirty-flush window
  widened for the hoisted declarations. | Devin
