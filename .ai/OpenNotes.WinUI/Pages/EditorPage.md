# OpenNotes.WinUI/Pages/EditorPage.xaml(.cs)
> Last updated: 2026-09-23 (V6 Task 6 — editor shell port) | Protection: STANDARD

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
  trim assigns `PageSource = null` off-screen.
- **Zoom:** `_zoomLevel` clamped `[0.25, 8]`, ±`0.1` step;
  `ZoomAroundPoint` keeps the viewport anchor stationary via one
  `ScrollViewer.ChangeView(offsets, zoomFactor)` (offsets are in scaled
  content coordinates — the WinUI equivalent of WPF's UpdateLayout+ScrollTo
  pair). `ZoomLabel` text AND accessible name = `NN%` (UIA parity — name is
  asserted by the smoke); `ZoomTextBox` inline edit on label tap; Ctrl+wheel.
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
- **Navigation:** prev/next buttons, editable one-based `Editor.PageJump`
  TextBox (`ApplyPageJumpFromTextBox` — parse/clamp/validation message/
  `JumpToPage`/`HidePageNumberTextBox`), PageUp/Down/Home/End/arrows,
  thumbnail/outline/bookmark/search-result jumps all funnel to `JumpToPage`.
- **Search:** Ctrl+F (page `PreviewKeyDown` + `MainWindow` window-level
  forward via `OpenSearchPanel`) opens `PdfSearchPanel`; `TextChanged`
  debounce → `GetPageTextInfoAsync` per page → `PdfSearchResult`s; select a
  result → `JumpToPage` + `SetPdfTextSelectionRects` highlight; status text.
- **Context menu:** code-built `MenuFlyout` on `PdfScrollViewer.ContextFlyout`
  — Rotate current page (`RotatePageAsync` + reload + re-jump), Export current
  page PNG incl. 1× (`RenderPageBgraAsync` → PNG encode → save picker),
  print + page ops entries (insert/delete/duplicate/reorder) wired to Core.
- **Lifecycle:** `DocumentOperationSession`/`lease` validation guards every
  async continuation (`ValidateDocumentOperationLease`,
  `IsSidebarLoadCurrent`, `_loadSessionId`); `ShutdownEditor()` →
  `ReleaseResources` — cancels CTSs, stops timers, releases thumbs/pages,
  disposes `PdfService`. `MainWindow.CloseTab` calls it synchronously because
  a collapsed Frame's page may never raise `Unloaded`.
- **UIA seams (DEBUG only):** `Editor.DebugSidebarNarrow` (forced narrow
  layout), `Editor.DebugCommitJump` (runs `ApplyPageJumpFromTextBox`),
  `Editor.DebugOpenSearch` (`OpenPdfSearch`), `Editor.DebugOpenContextMenu`
  (`_pageContextMenu.ShowAt` at viewport center) — 2×2 invisible buttons;
  the smoke session cannot deliver OS input, so these invoke the same
  handlers real input would. `Editor.PageJumpGroup` `HelpText` =
  `current-page=N` (the WinUI TextBox UIA Value can stay pinned after a
  SetValue+programmatic rewrite).
- **Localization:** `ApplyLocalization()` refreshes chrome/tooltips/context
  menu labels; validation message strings `Editor.PageJump*`.

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
