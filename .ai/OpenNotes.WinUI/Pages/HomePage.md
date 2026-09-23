# OpenNotes.WinUI/Pages/HomePage.xaml.cs
> Last updated: 2026-09-23 (V6 Task 5 — library home code-behind; `ContextRequested` fix) | Protection: STANDARD

## Purpose
`Caelum.Pages.HomePage : Page` (partial with `HomePage.Utilities.cs`) — the WinUI port of the WPF library home (~1,500 lines vs WPF's larger original). Drives tile loading, folder navigation, search/sort, add/create flows, context menus, drag/drop, file open → `EditorPage`, delete/rename/export, and toasts via `MainWindow`.

## What It Does
- **Loading:** `HomePage_Loaded` → `EnsureLibraryLoadedAsync` → `RecentFilesService.GetLibraryEntries(_currentFolderId)` → `HomeTiles` (add tile first, then folders, then files) → `RebuildVisibleTiles` applies `_searchQuery` + `HomeSortMode` (Date/Name) into `VisibleTiles` (the bound collection). `RefreshCurrentFolderAsync` is the single re-entry after every mutation.
- **Folder nav:** `_currentFolderId`/`_currentFolderName` + `FolderTile_Click` descend; `NavigateUpButton_Click` returns to root; `FolderBreadcrumb`/`BreadcrumbBarVisibility`/`IsInsideFolder` are x:Bind properties. `GetLibraryEntries("")` = library root via `NormalizeFolderId`.
- **Public surface for MainWindow:** `Filter(query)`, `SortByName()`, `SortByDate()`, `ToggleSelectionMode()` (Utilities), `IsSelectionMode`, `ApplyLocalization()`.
- **Tile activation:** `FileTile_Click` → selection toggle in selection mode, else `OpenFileTileAsync` → `MainWindow.NavigateActiveTabToFile(path)`. `FolderTile_Click` honors `IsChoosingMoveTarget` (selection-move drops into the clicked folder) before normal navigation.
- **Context menus:** `FileTile_ContextRequested`/`FolderTile_ContextRequested` build per-show `MenuFlyout`s (`CreateMenuItem` = glyph + themed foreground; danger items use `ThemeDangerBrush`). **Uses `ContextRequested`, not `RightTapped`** — ButtonBase marks `RightTapped` handled so the gesture dies on the tile surface; `ContextRequested` is the WinUI context-menu event (right-click, Menu key, press-and-hold) and is not swallowed. Position via `e.TryGetPosition` (falls back to element center for keyboard invocation).
- **Add tile:** `ShowAddTileMenu` → Open PDF (`PickAndOpenPdfAsync`), New Folder (`CreateFolderAsync`), New Notebook (`CreateEmptyNotebookAsync` → `PickNotebookTemplateAsync` template picker → `PdfService.CreateBlankPdfAsync`).
- **Rename:** `PromptForInputAsync` (ContentDialog with `TextBox`, localized confirm/cancel) → `RecentFilesService.RenameFolder` or physical file rename + `RecentFilesService.UpdatePath` + `MainWindow.HandleFilePathChanged` (retitles open tabs, updates the stub `EditorPage`).
- **Delete/Remove:** `DeleteFileTileAsync` → `RecycleBinService.TrySendToRecycleBin` + `RecentFilesService.Remove` (danger confirm first); `RemoveFileTileAsync` removes the library entry only.
- **Import/Export:** `TryImportAsLibraryPdfAsync` — PDFs copy in place; `WordDocumentImport.IsImportablePath` files go through `WordToPdfConverter` then import the sibling PDF. `ExportTileAsync` → `FileSavePicker` copy-out.
- **Drag/drop:** `FileTile_DragStarting` packs `HomePageDragDropHelper` formats (multi-path when the dragged tile is part of the selection) + `StorageItems` for Explorer drag-out — **Copy-only end to end** (`AllowedOperations = Copy`, `RequestedOperation = Copy`, matching the WPF `DragDropEffects.Copy` invariant pinned by `HomePageLibrarySourceTests`; in-app folder moves never reach OS negotiation — they ride the custom formats). `FileTile_DropCompleted` DEBUG-logs `DropResult` to the smoke log. Folder `DragEnter/Over` accepts via synchronous format/`StorageItems` probes + `IsDropTarget` visual; `FolderTile_Drop` moves library items (`MoveToFolder`) or imports externals; page-level `DragOver`/`Drop` imports to the current view.
- **Smoke log (DEBUG):** `HomeSmokeLog` appends `%TEMP%\opennotes_winui_home.log` — added while diagnosing the tile-activation/context-menu issues; keep it DEBUG-only.
- `OpenContainingFolder` (`Explorer /select,`), `SanitizeFileName`, `GetDefaultNotebookDirectory`/`BuildNotebookFilePath`, `GetMainWindow`, `ResolveThemeBrush`, `IsPdfFile` helpers at the tail.

## Important Notes / NEVER Change
- Tile handlers resolve the tile from `FrameworkElement.Tag` — NOT `DataContext` (x:Bind templates leave `DataContext` null; the first port used it and every tile action silently no-oped).
- NEVER switch the context-menu wiring back to `RightTapped` — see above; the UIA smoke `context-menu-opened` check guards this.
- NEVER advertise `DataPackageOperation.Move` in `FileTile_DragStarting` — a same-volume Explorer drop could relocate the library PDF and dangle the `RecentFilesService` entry (spec-review finding over `6c80948`).
- Async click/context handlers fire-and-forget by design (`_ = XAsync()`); faults route through `ContinueWith`→`HomeSmokeLog`/`ShowDialogAsync` where the WPF original surfaced errors — do not `await` inside event handlers that must stay synchronous.
- `RecentFilesService`/`RecycleBinService`/`PdfService`/`WordDocumentImport`/`WordToPdfConverter`/`LocalizationService` are static Core services — no DI.
- `HomePage_Unloaded`/`LanguageChanged` manage the `LocalizationService.LanguageChanged` subscription exactly once (`_languageChangedSubscribed` guard) — a page re-navigation must not double-subscribe.

## Open Threads / Resume Context
- **Status:** GREEN — `tools/winui-home-smoke.ps1` 27/27 (tiles render, context menu, folder nav + breadcrumb, search, selection bar, editor navigation, More flyout).
- Notebook creation is a template-picked blank PDF (same `PageInsertTemplate` catalog as WPF) — notebook *surface* support is an editor-task concern.
- WPF's `RefreshOpenContextMenus` has no port: menus are per-show `MenuFlyout`s, so a live language change can't leave stale strings.
