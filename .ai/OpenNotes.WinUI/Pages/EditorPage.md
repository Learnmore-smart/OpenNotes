# OpenNotes.WinUI/Pages/EditorPage.xaml(.cs)
> Last updated: 2026-09-23 (V6 Task 6 hardening — rename/DPI/bookmark-cache/CTS edges) | Protection: STANDARD

## Purpose
`Caelum.Pages.EditorPage : Page` — the WinUI editor shell port of the WPF
`Pages/EditorPage.xaml(.cs)` layout: `PdfScrollViewer` + centered
`PagesContainer` stack of `PdfPageControl`s, floating toolbar (ported
AutomationIds + `LucideIcon`), `DocumentSidebar` overlay (Pages/Outline/
Bookmarks tabs + collapse rail, **5.2.15 228-DIP content-offset rule**),
page-jump navigator, `PdfSearchPanel`, page context `MenuFlyout`, loading
overlay. Annotation interaction (ink/text/sticky/shapes) is explicitly
deferred — toolbar annotation buttons are visual/inert until T7–T9.

## What It Does
- **Load/render:** `PdfService.LoadPdfAsync(filePath, CancellationToken)` on
  `OnNavigatedTo` (path param) / `UpdateCurrentPdfPath`; `GetPageSizeInDips`
  stacks `PdfPageControl`s; `RenderPageBgraAsync` → `SoftwareBitmapSource`
  assigned into `PageSource`. Debounced re-render on zoom/scroll via
  `DispatcherQueueTimer`s + `PdfRenderPolicy` scale/retention; working-set
  trim assigns `PageSource = null` off-screen. The baseline render
  (`RenderPageInitialAsync`) multiplies `_zoomLevel` by
  `XamlRoot.RasterizationScale` (fallback 1.0) so a >100% DPI monitor gets a
  sharp first paint instead of a soft 1.0 raster until first zoom.
- **Rename/rebase (`UpdateCurrentPdfPath`):** `DocumentOperationSession.Begin`
  re-leases the renamed path but cancels every prior session lease — so
  `RestartPendingDocumentPipelines()` immediately re-kicks thumbnail loads
  (`_thumbnailPagesLoading.Clear()` + `TryLoadThumbnail` for null thumbs),
  `KickViewportRender`, `InvalidateBookmarkCache` + `RefreshBookmarks`,
  `RefreshOutlineCoreAsync` (fresh lease), and re-runs the visible
  `PdfSearchTextBox` query. When the rename lands mid-load (`_pageControls`
  empty but a load pending, `_completedLoadSessionId != _loadSessionId`) it
  re-kicks `LoadPdfAsync(_currentPdfPath)` instead — otherwise the dead
  lease would exit the load early and leave `LoadingOverlay` up forever.
  No-op when released/inactive or nothing was ever loaded.
- **Zoom:** `_zoomLevel` clamped `[0.25, 8]`, ±`0.1` step;
  `ZoomAroundPoint` keeps the viewport anchor stationary via one
  `ScrollViewer.ChangeView(offsets, zoomFactor)` (offsets are in scaled
  content coordinates — the WinUI equivalent of WPF's UpdateLayout+ScrollTo
  pair). `ZoomLabel` text AND accessible name = `NN%` (UIA parity — name is
  asserted by the smoke); `ZoomTextBox` inline edit on label tap; Ctrl+wheel.
  ZoomTextBox commit parity (WPF `ApplyZoomFromTextBox`): **both Enter AND
  LostFocus commit** — `Trim().TrimEnd('%')` → `int.TryParse` → range-check
  `[ZoomMin*100, ZoomMax*100]` → `ZoomAroundPoint(pct/100, viewportCenter)`
  (NOT bare `SetZoom` — keeps the anchor); Escape discards. `ZoomLabel`
  collapses while the textbox shows and is restored by `HideZoomTextBox`
  (WPF visibility parity — they share one grid cell).
- **Sidebar geometry (NEVER-change contract):** expanded margin-left
  **228 DIP** (184 rail + 44 collapsed-rail gutter math preserved), collapsed
  **32 DIP**, auto-collapse at window width **≤375 DIP**
  (`SidebarNarrowAutoCollapseWidth`). Live margin exposed DEBUG-only via
  `PagesContainer` `HelpText` = `pages-margin-left=N`.
- **Sidebar tabs:** `Pages` (thumbnail ListView + lazy rasterized thumbs +
  selection↔scroll sync, `_isSynchronizingThumbnailSelection` guard),
  `Outline` (`TreeView` — binds `TreeViewNode.Content`, NOT the item itself;
  ItemInvoked/invoke-button jump guarded by `IsSidebarOutlineItemCurrent`),
  `Bookmarks` (`PageBookmarkService` toggle/list, BookmarkToggle in rail).
  `UpdateBookmarkButton` runs on every `ViewChanged` — it reads the memoized
  `_bookmarkPageIndexes` HashSet (`GetBookmarkPageIndexes`, keyed by
  `_bookmarksCachePath`), never `PageBookmarkService.Load` per scroll tick;
  `RefreshBookmarks` re-warms the cache, `InvalidateBookmarkCache` clears it
  on load/rename.
- **Navigation:** prev/next buttons, editable one-based `Editor.PageJump`
  TextBox (`ApplyPageJumpFromTextBox` — parse/clamp/validation message/
  `JumpToPage`/`EndPageJumpEdit` — renamed from the misleading
  `HidePageNumberTextBox`; nothing hides, the box stays visible and re-syncs
  to the live page), PageUp/Down/Home/End/arrows,
  thumbnail/outline/bookmark/search-result jumps all funnel to `JumpToPage`.
- **Search:** Ctrl+F (page `PreviewKeyDown` + `MainWindow` window-level
  forward via `OpenSearchPanel`) opens `PdfSearchPanel`; `TextChanged`
  debounce → `GetPageTextInfoAsync` per page → `PdfSearchResult`s; select a
  result → `JumpToPage` + `SetPdfTextSelectionRects` highlight; status text.
  Enter/F3 steps via `MovePdfSearchSelection` (sync — `SelectedIndex=`
  synchronously raises `SelectionChanged`, which performs the jump; the old
  `MovePdfSearchSelectionAsync` jumped twice).
- **Context menu:** code-built `MenuFlyout` on `PdfScrollViewer.ContextFlyout`
  — Rotate current page (`RotatePageAsync` + reload + re-jump), Export current
  page PNG incl. 1× (`RenderPageBgraAsync` → PNG encode → save picker),
  print + page ops entries (insert/delete/duplicate/reorder) wired to Core.
- **Lifecycle:** `DocumentOperationSession`/`lease` validation guards every
  async continuation (`ValidateDocumentOperationLease`,
  `IsSidebarLoadCurrent`, `_loadSessionId`); `ShutdownEditor()` →
  `ReleaseResources` — cancels CTSs, stops timers, releases thumbs/pages,
  disposes `PdfService`. `MainWindow.CloseTab` calls it synchronously because
  a collapsed Frame's page may never raise `Unloaded`. CTS rotation is
  **cancel-only** — a cancelled-but-unreferenced CTS is GC-collectible,
  while `Cancel()`+immediate `Dispose()` races continuations that still
  `token.Register` (ObjectDisposedException). `PdfService.DisposeAsync()` is
  fire-and-forget but observed via `ContinueWith(OnlyOnFaulted)` so a fault
  can't go unobserved to the GC. `RefreshLocalizedDocumentSidebar` and
  `ApplyLocalizedSidebarLabels` share `ApplyLocalizedSidebarLabelText` (the
  common 7-assignment block).
- **UIA seams (DEBUG only):** `Editor.DebugSidebarNarrow` (forced narrow
  layout), `Editor.DebugCommitJump` (runs `ApplyPageJumpFromTextBox`),
  `Editor.DebugOpenSearch` (`OpenPdfSearch`), `Editor.DebugOpenContextMenu`
  (`_pageContextMenu.ShowAt` at viewport center) — 2×2 invisible buttons;
  the smoke session cannot deliver OS input, so these invoke the same
  handlers real input would. `Editor.PageJumpGroup` `HelpText` =
  `current-page=N` (the WinUI TextBox UIA Value can stay pinned after a
  SetValue+programmatic rewrite).
- **Localization:** `ApplyLocalization()` refreshes chrome/tooltips/context
  menu labels; validation message strings `Editor.PageJump*`. The page
  self-subscribes `LocalizationService.LanguageChanged` in `Loaded`
  (guarded by `_languageChangedSubscribed`) and unsubscribes inside
  `ReleaseResources` (covers Unloaded + `ShutdownEditor` + `OnNavigatedFrom`
  — a collapsed Frame's page may never raise `Unloaded`); handler →
  `ApplyLocalization()` on the UI thread. Without this, open editor tabs
  keep stale strings when the language changes — MainWindow's handler only
  reaches itself and HomePage.

## Important Notes / NEVER Change
- **228/32 DIP margin contract + ≤375 auto-collapse** is a pinned WPF
  (5.2.15) contract — do not "simplify" the constants.
- `ListView` (not `ListBox`) for sidebar lists — `ListViewItemPresenter`
  inside a `ListBoxItem` template crashes WinUI (`stowed 0xc000027b`).
- `TreeView` `ItemTemplate` binds against `TreeViewNode` — use
  `Content`/cast, never the data item directly (`WinRT.IInspectable` cast
  crash).
- Plain `Grid`/`Border`/`StackPanel` have no AutomationPeer — container
  AutomationIds must live on `Uia*` controls (`Controls/UiaPanels.cs`).
- `ShutdownEditor` must stay synchronous and idempotent — it is the only
  guaranteed teardown path on tab close.
- Keep `Editor.*` AutomationIds stable — the entire smoke contract keys off
  them.

## Open Threads / Resume Context
- **Status:** GREEN — `tools/winui-editor-smoke.ps1` 60/60.
- **Deferred (stubbed, by design):** ink surface + eraser/shape/laser/ruler/
  select/text tools (T7); text/sticky/image overlays + persistent PDF text
  selection visuals (T8); save/autosave/dirty-close + version history +
  settings + remaining flyout actions (T9). Undo/Redo buttons are inert.
