# OpenNotes.WinUI/Models/HomeTile.cs
> Last updated: 2026-09-23 (V6 Task 5 — WinUI library tile model) | Protection: STANDARD

## Purpose
`Caelum.Models.HomeTile : INotifyPropertyChanged` — the per-tile view-model for the WinUI `HomePage` library grid. One instance represents the **add tile**, a **virtual folder**, or a **library file**. The WinUI stand-in for the WPF `RecentFileEntry` + `DataTrigger` combination: WPF expressed tile state visually through template triggers; WinUI `x:Bind` templates have no triggers, so this model exposes computed properties instead.

## What It Does
- **Factories:** `CreateAddTile()`, `CreateFileTile(RecentFileEntry)`, `CreateFolderTile(RecentFileEntry, childCount)` — set `IsAddTile`/`IsFolder`/`IsFile`, `Id`, `Path`, `FileName`, `Color` (persisted folder-color hex), `PageCount`, `LastModified`.
- **State flags (notifying):** `IsSelected`, `IsDropTarget`, `IsHovered`, `IsSelectionModeUi` — drive the selection check badge, folder drop/move-target highlight, hover scale, and the selection-mode badge toggle.
- **Computed text:** `FileName` (display name or file-name fallback), `InfoText` (folder child count / page count + last-modified line).
- **Computed chrome for x:Bind:** `TileBackground`, `TileBorderBrush`, `TileBorderThickness`, `CheckBadgeBackground`, `CheckBadgeBorderBrush`, `CheckBadgeVisibility`, `CheckGlyphVisibility`, `FolderTabBrush`, `FolderBodyBrush`, `FolderLineBrush`, `FolderLineAltBrush`, `AutomationId` (`HomeTile_<name>`, `HomeTile_Add` for the add tile).
- Folder color parses the persisted `Color` hex via `Windows.UI.Color.FromArgb` — the type name must be fully qualified here because the instance `Color` property shadows `Windows.UI.Color` (a CS0120 trap when editing).

## Important Notes / NEVER Change
- `AutomationId` feeds the `AutomationProperties.AutomationId` bindings in `HomePage.xaml` and is load-bearing for `tools/winui-home-smoke.ps1` (`HomeTile_Add`, `HomeTile_<filename>`).
- Keep all user-visible strings out of this model — `InfoText` uses `LocalizationService.Get/Format` keys (`Home.Info.Items`, `Home.Info.Pages`, `Home.Info.Notebook`, etc.).
- WPF `DataTrigger` parity lives ONLY in these computed properties + the code-behind hover/selection refresh (`RefreshTileVisualState`) — do not reintroduce `DataContext`-dependent triggers.
