# OpenNotes.Tests/WinUiDialogsServicesSourceTests.cs
> Last updated: 2026-09-25 (V6 Task 9 Phase B — dialogs + services contract) | Protection: STANDARD

## Purpose
Source-contract tests pinning the Task 9 Phase B WinUI dialog/service parity
so a refactor cannot silently drop the pieces the WPF shell relies on:

- `DialogServiceSerializesEveryContentDialogBehindOneGate` — `DialogGate`
  semaphore, the info/error/confirm/danger-confirm API surface, explicit
  `XamlRoot` null-no-op contract, `RunUnderDialogGateAsync<T>`, and the
  `PopupZOrderHelper`/HWND_NOTOPMOST no-port doc note.
- `SettingsDialogStagesPreviewsAndRevertsOnDismiss` — ctor staging +
  `SelectedSettings`, live `PreviewSettings` calls, `Closed`-based revert,
  `LanguageChanged`/`ThemeApplied` subscribe-detach, all eight setting rows
  and the unexposed-field clone coverage.
- `PageTemplatePickerCoversBothModesBehindTheGate` — both ctors,
  `IsConfirmed`/`SelectedTemplate`/`SelectedFolderPath`, all nine templates,
  card-click confirm in insert mode, gated Create button in notebook mode,
  and the two gated call sites (EditorPage insert, HomePage notebook).
- `EditorPageCarriesTheVersionHistoryRestoreSequence` — wired+enabled
  `VersionHistoryButton`, session/path capture, versions flyout, the
  snapshot-first reversible restore sequence, and `_transientFlyout`
  registration.
- `EditorPageCarriesLeaseGuardedStructuralPageOperations` — insert/delete
  lease chain, dirty flush, byte snapshots, bookmark remap, single-page
  guard, and the page chrome builders.
- `MainWindowWiresSettingsThroughPreviewApplyAndTheGate` — enabled
  `SettingsMenuItem` → `Settings_Click` → `OpenSettingsDialogAsync`,
  `PreviewSettings`/`ApplySettings` propagation, `Main.SettingsSaved` toast.
- `EditorApplySettingsCarriesThePerformanceModeReRenderBranch` — the
  `ApplySettings(AppSettings)` overload + cache-reset/re-render branch.

Follows the same `Read()`/`ProjectRoot()` helper pattern as
`WinUiSavePipelineSourceTests` (reads `OpenNotes.WinUI/**` sources,
normalizes line endings).

## Change History
- 2026-09-25 Task 9 Phase B quality pass: picker contract pins the real
  `Editor.InsertPageDialogTitle`/`Subtitle` keys and bans the
  nonexistent `Editor.PageTemplateTitle`/`Subtitle`; the structural-ops
  contract now pins `InsertPageCoreAsync`, the thumbnail context menu
  (`ThumbnailListBox_ContextRequested` + XAML hookup +
  `Editor.InsertBlankPageBefore`/`DuplicatePage`/`DeletePage` keys),
  `InsertBlankPageBeforeAsync`, `DuplicatePageAtAsync`,
  `BeginStructuralOperation` and `RollbackStructuralOperationAsync`.
  | Devin
- 2026-09-25 Task 9 Phase B: initial contract set. | Devin
