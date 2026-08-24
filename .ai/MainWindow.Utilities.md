# MainWindow.Utilities

## Purpose

Shared settings, localization, tab refresh, and settings-dialog orchestration for the main window.

## Constraints

- Settings previews must be reversible when the dialog is cancelled.
- Persist settings only after the user confirms Save.
- Open editor tabs receive live settings changes without reloading document content.

## Open Threads

- Keep this mirror synchronized when settings or theme propagation changes.

## V5 Completion Status

- Settings preview applies language and ThemeService resources immediately, propagates the complete settings snapshot to open EditorPage tabs, and restores the original snapshot on cancel.
- Wave5 preview passes `WorkspaceBackdrop` to `ThemeService` so the editor surround changes live while PDF page/image layers remain untouched; save/reopen persists the value through `AppSettingsService`.
- Localization refresh no longer overwrites a live preview with persisted settings.
