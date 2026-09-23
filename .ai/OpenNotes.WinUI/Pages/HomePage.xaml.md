# OpenNotes.WinUI/Pages/HomePage.xaml
> Last updated: 2026-09-23 (V6 Task 5 — library home markup; `ContextRequested` context-menu fix) | Protection: STANDARD

## Purpose
WinUI `Page` markup for the library home: header/breadcrumb, `ItemsRepeater` tile grid, selection action bar, page-level drop surface. Ports the WPF `Pages/HomePage.xaml` layout; every string is `x:Bind` to localized properties or `x:Uid`-free code-behind assignment (no literal UI text beyond the editor-independent add-tile glyphs).

## Layout / Structure
- `HomePage.Resources`: three `x:DataType="models:HomeTile"` templates (`AddTileTemplate`, `FolderTileTemplate`, `FileTileTemplate`) + `HomeTileTemplateSelector`.
- Root `Grid`: `AllowDrop` + `DragOver`/`Drop` (external file import to the current folder/root) → `HomeScrollViewer` → header stack (`HomeTitleTextBlock`, `HomeSubtitleTextBlock`, `FolderBreadcrumb`, `NavigateUpButton` bound to `BreadcrumbBarVisibility`) → `ItemsRepeater` (`TilesRepeater`, AutomationId `TilesGrid`, `ItemsSource={x:Bind VisibleTiles, Mode=OneWay}`, `ItemTemplate` = selector, `UniformGridLayout`) → `SelectionActionBar` (`SelectAllSelectionButton`, `ClearSelectionButton`, `MoveSelectionButton`, `RemoveSelectionButton`, `DeleteSelectionButton`, `DoneSelectionButton`).
- **Tiles:** invisible `Button` (style `InvisibleButtonStyle`) over the icon `Grid`, name + info `TextBlock`s, corner check badge on file tiles. Every interactive element carries `Tag="{x:Bind}"` — the code-behind resolves the `HomeTile` from `FrameworkElement.Tag` because x:Bind templates do NOT populate `DataContext`.
- **Context menus:** `ContextRequested="FileTile_ContextRequested"` / `FolderTile_ContextRequested` on the tile `Border`s — NOT `RightTapped` (ButtonBase marks `RightTapped` handled, so a handler on the tile surface never fires — this was the context-menu regression fixed in Task 5). Menus themselves are code-built `MenuFlyout`s (file: Open/Rename/Select/MoveToLibrary-in-folder/CopyPath/OpenFolder/Export/Delete/Remove; folder: Open/Rename/Color submenu/RemoveFolder).
- Hover scale animation stays code-behind (`TileButton_PointerEntered/Exited` → `ScaleTransform` on `IconGrid`/`FolderIconGrid`); WPF `TileScale` storyboard parity.

## Important Notes / NEVER Change
- `Tag="{x:Bind}"` on every tile `Button`/`Border`/`Grid` that a code-behind handler inspects is LOAD-BEARING — removing one silently kills that handler (`DataContext` is null inside x:Bind templates).
- Do NOT revert `ContextRequested` to `RightTapped` — see note above; also do not attach context handlers to the inner `Button` (same ButtonBase swallowing).
- AutomationIds bound from `HomeTile.AutomationId` (`HomeTile_Add`, `HomeTile_<filename>`) plus `HomeScrollViewer`, `HomeTitleTextBlock`, `HomeSubtitleTextBlock`, `FolderBreadcrumb`, `NavigateUpButton`, `TilesGrid`, `DragDropOverlay`/`DragDropOverlayText`, `SelectionActionBar`, `SelectionSummary`, `SelectionHint` are load-bearing for `tools/winui-home-smoke.ps1`.
- File-tile drag-out uses `CanDrag` + `DragStarting` + `DropCompleted` (DEBUG `DropResult` log) on the tile `Button` — packs `HomePageDragDropHelper` formats + `StorageItems` with **Copy-only** `AllowedOperations`; folder tiles accept via `AllowDrop` + `DragEnter/Over/Leave/Drop` on the inner `Grid`.
