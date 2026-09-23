# OpenNotes.WinUI/Pages/HomePage.Utilities.cs
> Last updated: 2026-09-23 (V6 Task 5 — selection-mode + localization partial) | Protection: STANDARD

## Purpose
`Caelum.Pages.HomePage` partial — `INotifyPropertyChanged` implementation, selection-mode state machine, and `ApplyLocalization`. The WinUI counterpart of the WPF `HomePage.Utilities.cs` (minus the WPF `ContextMenu` refresh — WinUI menus are per-show `MenuFlyout`s).

## What It Does
- **Selection state:** `IsChoosingMoveTarget`, `CanChooseMoveTarget`, `SelectedTileCount`, `HasSelectedTiles`, `CanSelectAllTiles`, `SelectionSummary`/`SelectionHint`, and the localized `Selection*Text` button labels — all `x:Bind`-facing.
- `SetSelectionMode(bool)`/`ToggleSelectionMode()` — enters/exits selection mode, clears selection on exit, refreshes `SelectionBarVisibility` + per-tile `IsSelected` visuals; `MainWindow.SelectButton_Click` is the entry point.
- `ToggleTileSelection`, `ClearSelectedTiles`, `SelectAllVisibleTiles`, `RemoveSelectedTilesAsync` (danger-confirm → per-file `DeleteFileTileAsync`/`RemoveFileTileAsync`), and the selection-bar `Click` handlers (`SelectAll/Clear/Move/Remove/Delete/Done`). `MoveSelectionButton_Click` arms `IsChoosingMoveTarget` — the next folder click executes `MoveSelectedTilesToFolderAsync`.
- `RefreshSelectionState()` re-raises every computed property (summary/hint/Can* flags) after any selection-affecting mutation.
- `ApplyLocalization()` — pushes localized strings into the x:Bind properties (navigate-up, selection labels, header) and refreshes tile text; subscribed once from `HomePage_Loaded` via `LocalizationService.LanguageChanged` (`_languageChangedSubscribed` guard lives in the main file).
- `RefreshTileVisualState()` — re-resolves computed brushes/visibility on every tile (theme change / selection toggle) since x:Bind `OneWay` bindings don't re-evaluate on their own.

## Important Notes
- Keep this file free of XAML element assumptions beyond the named selection-bar buttons — the visual contract lives in `HomePage.xaml`.
- `IsChoosingMoveTarget` must reset on `Done`, folder navigation, and refresh — a stuck "choose target" state turns every folder click into a move.
