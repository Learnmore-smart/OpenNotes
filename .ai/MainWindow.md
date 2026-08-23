# MainWindow

## Purpose

Owns the single-window Frame/tab shell, application chrome, startup theme and global tab keyboard navigation.

## V5 Changes

- Applies the persisted `ThemeService` palette before localization at startup.
- `Ctrl+Tab` and `Ctrl+Shift+Tab` cycle the existing `_tabs` list without replacing the active Frame.
- Settings preview propagates the complete `AppSettings` snapshot to open editor tabs and restores it on cancel.
- Title-bar, search, tab-strip and chrome surfaces consume the runtime theme brush resources. The shell now reads as a paper archive: desk root, alternate-paper title/tab areas, an ink search surface and a margin-red brand rail.

## Constraints

- Preserve the single MainWindow + Frame-tab architecture.
- Do not tint PDF page bitmaps when changing application theme.

## Open Threads

- **Status:** performance lifecycle complete. `ActivateTab` suspends the previous editor and activates the selected editor; `Frame_Navigated` applies the same rule to back/forward/new-content navigation, and navigation-away paths suspend before leaving. Minimizing/restoring the window uses the same host-active lifecycle. Tab and window close paths await `ReleaseResourcesAsync` after the existing autosave. The Frame/tab shell and save ordering are unchanged.

## Agent Decisions / Thoughts

- **2026-08-20 Codex:** Runtime-created tab controls use `SetResourceReference` so an in-place `ThemeService.Apply` swaps their brushes without requiring a new window. Inactive tabs remain transparent; active tabs use the theme's alternate surface and border tokens.
- **2026-08-20 Codex:** Tab borders are keyboard focusable and activate on Enter/Space. Focus uses `ThemeFocusBrush` and does not alter the existing drag/reorder or single-window Frame architecture.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-20 | Replaced fixed runtime tab colors with dynamic theme resources and added keyboard focus/activation feedback. | Codex |
| 2026-08-20 | Migrated title-bar, navigation, selection, search, and toast chrome to existing runtime `Theme*Brush` resources without changing layout or behavior. | Codex |
| 2026-08-21 | Wired editor activation/navigation/minimize suspension and awaited native-resource cleanup on tab/window close. | Codex |
| 2026-08-22 | Applied the Desk/Paper/PaperAlt/Ink/Margin material tokens to the existing shell without changing Frame/tab bindings or runtime behavior. | Codex |
