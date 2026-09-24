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

- **Status:** GREEN — 6/6 pass in the 685-test suite.
