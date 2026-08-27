# OpenNotes.Tests/EditorTextSessionTests.cs

> Last updated: 2026-08-23（Wave 2 final review） | Protection: STANDARD

## Purpose

Production WPF/STA regressions for text-session save ordering and final close/navigation admission. They construct actual `EditorPage`/`PdfPageControl` instances, start `BeginTextEditSession`, hold the normalized PDF path lease to create a deterministic save barrier, mutate the live `TextBox` after input is blocked, and reopen the saved PDF.

## Coverage

- The text session is committed before `DocumentSaveCoordinator.SaveAsync` captures its generation.
- One latest text value produces one version sidecar, avoiding a pseudo-stale duplicate save.
- `PdfService.LoadPdfAsync` → `ExtractedAnnotations` verifies the persisted text content after reopen.
- Final close persists a real late text mutation while an earlier save waits for the same-path lease; resource release follows only after the latest snapshot.
- Final navigation persists a late text mutation, then `ResumeDocumentInteraction()` reopens both editor admission and the coordinator so an after-return edit autosaves instead of being discarded.
- The production save callback marshals WPF snapshot collection back to the editor Dispatcher when a generation-retry continuation arrives from the thread pool.
- All data and version sidecars use a temporary `OPENNOTES_DATA_ROOT`; the test restores `WINDIR`/data-root environment state and deletes its isolated directory.

## Evidence

- RED covered missing/incorrect text-session ordering and the late close retry's cross-thread WPF collection failure; GREEN `ProductionTextSessionCommitsBeforeSaveAndWritesLatestContentOnce`, `ProductionCloseKeepsLateTextMutationInTheFinalSnapshot`, and `ProductionNavigationReopensCoordinatorForEditsAfterReturning` pass on the production WPF/STA path.

## Open Threads / Resume Context

- **Status:** complete - `CreateTestApplication` now seeds the required `ToolbarFocusVisualStyle` alongside the other editor resources; isolated EditorTextSessionTests passes 3/3 and the complete suite passes 303/303.
- **RED evidence:** 2026-08-26 isolated `EditorTextSessionTests` failed all 3 cases with `XamlParseException` at `EditorPage.xaml` line 711: `ToolbarFocusVisualStyle` was not found. The class passed when grouped after popup tests only because their setup happened to seed that resource.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-26 | Added the missing ToolbarFocusVisualStyle to the isolated WPF test application; EditorTextSessionTests now passes 3/3 and the full suite passes 303/303. | Codex |
