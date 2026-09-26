# OpenNotes.WinUI/Pages/EditorPage.xaml(.cs)
> Last updated: 2026-09-26 (T12-B — Fluent editor chrome revamp) | Protection: STANDARD

## Purpose
`Caelum.Pages.EditorPage : Page` — the WinUI editor shell port of the WPF
`Pages/EditorPage.xaml(.cs)` layout: `PdfScrollViewer` + centered
`PagesContainer` stack of `PdfPageControl`s, floating toolbar (ported
AutomationIds + `LucideIcon`), `DocumentSidebar` overlay (Pages/Outline/
Bookmarks tabs + collapse rail, **5.2.15 228-DIP content-offset rule**),
page-jump navigator, `PdfSearchPanel`, page context `MenuFlyout`, loading
overlay. Phase-A ink (pen/highlighter/eraser + pressure + undo) is live;
Phase-B ink tools (select/transforms incl. cross-page moves, shapes,
hidden ink, laser, ruler) are live too — text/sticky/image overlays and
the Task 9 Phase A save/autosave + close/dirty protocol is live (T9-B defers settings window, version-history UI, Save-As picker).

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
- **Thumbnail drag-reorder (G5, WPF parity):** `ThumbnailListBox` is a MANUAL
  drag surface — `CanDragItems`/`CanReorderItems` stay `False` (built-in
  reorder would mutate `SidebarPageItems` before the lease pipeline can
  validate). `PointerPressed`/`PointerMoved` (attached `handledEventsToo` —
  ListViewItem marks presses handled) arm a `ThumbnailDragPayload`
  (SourceIndex + session + normalized path + item ref) and lift
  `StartDragAsync` past a 4-DIP threshold; `DragStarting` stamps it into the
  package under `Caelum.ThumbnailDragPayload`. `DragOver`/`DragEnter`
  re-validate (`IsCurrentThumbnailDragPayload`) and resolve the slot via
  `TryResolveThumbnailDropSlot` — a container-walk half-item split (replaces
  the WPF `e.OriginalSource` ancestor probe; inter-item gaps now clamp to
  the nearest slot instead of "past end") — then raise
  `ThumbnailDropIndicator`. `Drop` → `MovePageAsync` runs the shared
  structural-op pipeline (payload lease → edit admission → structural latch
  → dirty flush → `ReorderPagesAsync` → fresh-session reload → focus moved
  page → `ApplyPageMove` → `DocumentSnapshotAction` undo) with
  bytes+sidecar rollback on post-write failure. Escape cancels the OS drag
  natively; `CloseTransientUi`/`LoadPdfAsync`/`SetHostActive` sweep armed
  state + indicator. Failure toast `Editor.PageReorderFailed` + `\uE783`.
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
  **Print (live: `PrintPdfAsync` → `Win32Print` PrintDlgEx + GDI spool,
  Ctrl+P too)** + page ops entries (insert/delete/duplicate/reorder) wired to
  Core. Blank-area flyout carries Copy/Paste/SelectAll/**RefreshPage**
  (save→`ReloadDocumentForOperationAsync`)/Delete.
- **Immersive fullscreen (G6, WPF Task 16 parity):** `ToggleImmersiveMode`
  hides `ToolbarBorder`/`DocumentSidebar`/`PdfSearchPanel` via Opacity=0 +
  IsHitTestVisible=false (overlay chrome — zero reflow), resets
  `PagesContainer.Margin` to `PagesContainerDefaultMargin`, and swaps the
  window presenter via `MainWindow.SetImmersiveFullscreen`
  (`AppWindowPresenterKind.FullScreen` covers the taskbar — the piece WPF's
  borderless window got free; Default restores placement, `AppWindow_Changed`
  re-applies custom chrome/min-size). `EditorPage_PreviewKeyDown` keeps WPF
  order: F11 gated on `!textInputFocused`, immersive-Escape ahead of
  resize/tool-reset Escape; `SetHostActive(false)` also exits (the presenter
  is window-global — WPF's immersive chrome was page-local).
- **Print (G1):** `PrintPdfAsync` ports the WPF lease/validation/
  `Editor.PreparingPrint` overlay/`PrintSent` toast/`PrintFailed` dialog/OCE
  flow. `BuildPrintablePagesAsync` atomically copies the PDF into
  `%TEMP%\Caelum\Print\{guid}.pdf`, bakes annotations via
  `SaveAnnotationsToPdfAsync`, and `RenderPrintablePages` rasterizes the
  selected pages at the printer's DPI through `PdfiumRasterizerFactory` —
  bounded by Core `PrintPageGeometry.ResolvePrintRenderDpi` (96–600 DPI,
  250 MP job budget). `Services/Win32Print.cs` owns the `PrintDlgEx` sheet +
  `CreateDC`/`StartDoc`/`StretchDIBits` spool (managed copies/collate —
  DEVMODE fields lifted then reset so drivers can't double-replicate);
  WinRT `PrintManager` intentionally not used (needs packaged CoreWindow).
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
- **Ink engine (Task 7 Phase A):** each `PdfPageControl` hosts an
  `InkSurface` (exposed as `page.Ink`); the page owns tool state
  (`_currentTool`/`_previousTool` `ToolType`, `_penColor`, `_penSize`,
  `_highlighterColor`, `_highlighterSize`, `_eraserSize`,
  `FreehandHighlighterOpacity`=140) and broadcasts tool + `AppSettings`
  (`PenOnlyMode`, `EnablePressure`, `WholeStrokeEraser`, `InkSimulation`,
  `ShapeRecognition`, `StrokeSmoothing`) via `ApplyToolToAllPages()`.
  `StrokeCollected` → `InkStrokeAddedAction` onto `_undoStack`;
  `StrokeRecognized` → `InkStrokeReplacedAction` (undo restores the raw
  scribble — deliberate spec change from WPF's StrokeAdded-on-fresh-stroke);
  `StrokesErased` → `InkStrokesErasedAction`; `InkMutated` → guarded
  `MarkDirty()` (suppressed under `_isLoadingAnnotations` because WinUI
  quiet mutators raise it too — WPF routes those through
  `QuietStrokeMutation`) + unconditional `InvalidateThumbnail(pageIndex)`
  (cache eviction — ink is not composited into thumbs yet).
  `PushUndoAction` records
  `_documentSaveCoordinator.RecordChange(action.LeavesDocumentDirty)` on
  BOTH the admitted path (via `ApplyDirtyStateForAction`) and the blocked
  path (the event may already have mutated the model — the generation must
  survive so the close-save loop can't release stale data). Undo/Redo toolbar buttons carry the accelerators —
  Ctrl+Z undo, Ctrl+Y and Ctrl+Shift+Z redo (WPF `EditorPage_KeyDown`
  parity) — and consume `_undoStack`/`_redoStack` of
  `Caelum.Ink.IUndoAction` (`UndoAsync`/`RedoAsync` awaited — both
  `CancelInteraction()` on every page first so a live selection-drag
  snapshot restore or erase gesture can't re-apply/duplicate what the
  action is undoing; `LastOperationSucceeded=false` on erased/cross-page
  actions skips the stack pop;
  `UpdateUndoRedoButtons` refreshes `IsEnabled` after every mutation).
  `CancelInteraction` runs on every page before viewport re-renders/tab
  teardown so a stroke in progress can't strand pointer capture. Pen
  service: `_penService` is a REFERENCE to the single window-scoped
  `Caelum.Services.PenService` owned by `MainWindow` —
  `InitializePenService` (on `Loaded`) only pulls
  `GetMainWindow()?.GetOrCreatePenService()` and
  `PushPenServiceToPages` feeds each surface's `SetPenService`; the
  surfaces' `ProbePointer` calls accumulate `Capabilities` →
  `PenDeviceDetected`. The window routes `ToolToggleRequested` to the
  ACTIVE editor's `internal HandlePenToolToggle()` → `ToggleEraserMode`
  (eraser ↔ `_previousTool`) and the first-pen event to
  `HandlePenDeviceDetected(info)` — both gated on
  `!_isHostActive || _resourcesReleased` pre-enqueue AND inside the
  callback (WPF `IsActiveEditorPage()` parity). `ReleaseResources`
  clears the reference only — it never disposes the shared instance.
- **Localization:** `ApplyLocalization()` refreshes chrome/tooltips/context
  menu labels AND re-stamps page-chrome tooltips via `RefreshPageDeleteButtons()`
  (per-page delete + insert-gap buttons); validation message strings `Editor.PageJump*`. The page
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

## Phase B additions (2026-09-23)

- `ActivateTool` now maps all Phase-B tools (`Select`, `Shape`, `HiddenInk`,
  `Laser`) into the exclusive set; `RulerToolButton` is NOT in the set —
  `RulerToolButton_Click` → `SetRulerVisible`, matching WPF's overlay-toggle
  semantics (ruler stays on beside Pen/Highlighter). Leaving Select clears
  `_activeSelectionPage`'s selection (WPF order — `_currentTool` still holds
  the outgoing tool at that point).
- `ApplyToolToAllPages` pushes the full Phase-B state per page:
  `SetSelectionMode`/`SetSelectionShape`/`SetSelectionFilter`,
  `CustomInkInputProcessingMode.Inking` (Pen/Highlighter, `ink.Tool`
  pre-seeded), `.Erasing`, `.HiddenInk` (mask colour 199,205,212 + size 28 +
  `DefaultRevealDurationMs`), `.Shape` (`CurrentShape`/`ShapeIsDashed`/
  `ShapeColor`/`ShapeStrokeSize`), `.Laser`, `.None` (Select/Text/etc.);
  then `CancelInteraction`.
- `AddPdfPage` wires every Phase-B event: `ShapeCommittedUndoable`,
  `HiddenInkCreated`/`HiddenInksRemoved`,
  `SelectionChanged` (tracks `_activeSelectionPage`),
  `SelectionMoveCompleted` (cross-page detection via
  `FindPageAtContainerPoint` → `InkSelectionCrossPageMoveAction` with
  page-origin adjust, else `InkSelectionMoveAction`),
  `SelectionResizeCompleted`/`SelectionRotateCompleted`,
  `BlankContextRequested` → `ShowBlankContextMenu`; plus per-page
  `GetRulerGeometryInPageCoords` (viewport→page `TransformToVisual` on
  every query — scroll/zoom/drag can never serve stale edges); and the
  host-active gate pair `SetHostActive`/`SetDocumentInputEnabled` is
  propagated with `_isHostActive` BEFORE the control joins
  `_pageControls` — a doc loading while its tab is hidden must not come
  up input-enabled.
- **Ruler overlay** (`RulerOverlayCanvas`, outside the ScrollViewer so it is
  viewport-anchored): `EnsureRulerVisual` builds a `CursorGrid` 360×56
  (semi-transparent themed body, tick canvas every 10 DIP with 50-DIP
  majors, centre dot, transparent end-cap rotate zones, edge length
  handles) — WPF `Cursor=` parity needs cursor-capable element subclasses
  (`CursorGrid`): `UIElement.ProtectedCursor` is protected so a page cannot
  assign a cursor to an arbitrary child (the shape types are also sealed —
  `Grid` is the only viable wrapper base). Left-drag body = move
  (clamped to viewport), left-drag end caps OR right-drag anywhere =
  rotate with 15° snapping, handles resize 80..∞; session-only state,
  first show centres in the viewport.
- **Flyouts**: `ShowPenFlyout` (WPF `_penPopup` port — 0.5–8/0.25 size
  slider `Editor.Pen.Size`, `Editor.PopupPreview` live stroke line,
  «Recent» row + shared 12×8 HSV palette, Pressure/Ink
  Simulation/Shape Recognition persisted toggles `Editor.Pen.*`,
  `Editor.Pen.Smoothing.{0..3}` → `AppSettings.StrokeSmoothing`),
  `ShowEraserFlyout` (`Editor.Eraser.Pixel`/`Editor.Eraser.WholeStroke`
  mode row → `WholeStrokeEraser`, 4–80 `Editor.Eraser.Size` slider +
  `EraserSizePreviewEllipse` ~1.2 s flash via `_eraserPreviewCts`),
  `ShowHighlighterFlyout`, `ShowShapeFlyout` (9 shape kinds in a 3×3 `Grid` —
  `UniformGrid` does not exist in WinUI — Solid/Dashed style, 1–20 size
  slider at 0.5 steps, shared HSV palette; WPF AutomationIds preserved:
  `Editor.Shape.<Kind>`, `Editor.Shape.Style.*`, `Editor.Shape.Size`),
  `ShowSelectionFlyout`, selection action-bar flyout (style apply →
  `ApplySelectedDrawingStyle` → `InkStrokesStyleChangedAction`),
  `ShowBlankContextMenu`. All five tool flyouts are built fresh per
  `ShowAt` and tracked as `_toolFlyout`/`_toolFlyoutTool` (WPF
  `CloseToolPopups` mutual exclusion — highlighter modes share one
  bucket) as well as `_transientFlyout`; `WrapToolFlyoutContent` ports
  `EnableToolPopupScrolling` (XamlRoot height for `WorkArea`) except the
  selection flyout which WPF left unwrapped.
- **Recent colors (G4)**: `BuildRecentColorsSection` + `RefreshRecentColorsRow`
  render the «最近 Recent» swatch row (`Editor.Color.Recent.{i}`,
  22×22 swatch in a 32×32 button) above the palette in pen/highlighter/
  text surfaces; `BuildColorPalette(initial, changed, recentRow, out
  markSelected)` exposes the shared marker so picks flag recents +
  palette ring alike. List math (`MaxRecentColors=8`, dedupe
  newest-first, `#RRGGBB`/`#AARRGGBB` parse) lives in Core
  `Caelum.Services.RecentColors`; persistence via `SaveSetting` (load →
  mutate → `AppSettingsService.Save` → `_applicationSettings`). The
  cached `_textColorFlyout` refreshes the row on `FlyoutBase.Opening`.
  Shape colours stay session-only — WPF records no shape recents.
- `EditorPage_PreviewKeyDown`: Ctrl+A → arm Select + `SelectAllAnnotations`
  on the current page; Delete/Back → `DeleteSelection` — the page is
  captured FIRST (`ClearSelection` fires `SelectionChanged(false)`
  synchronously and the handler nulls `_activeSelectionPage`), then
  placements → `InkStrokesRemovedAction`; Escape → `CloseTransientUi("escape")`
  + `ActivateTool(None)` ahead of the ctrl/`textInputFocused` gates (WPF Esc
  parity — the resize-restore Escape keeps precedence; the tool switch
  commits the live edit session).
- Hidden-ink load: `LoadAnnotationsIntoPages` feeds
  `pageAnnotation.HiddenInks` quietly under `_isLoadingAnnotations` (no
  undo entries, WPF loader parity); save-side `HiddenInks` writers live in
  Core `PdfService` — T9-A wires `CollectAnnotations` →
  `SaveAnnotationsToPdfAsync` so hidden masks persist through saves.
- WPF `SetResourceReference` has no WinUI equivalent — transient overlay
  brushes resolve once via `TryFindBrush` (rebuilt on next selection).

- `SetHostActive(bool)` (public) — `MainWindow.ActivateTab` calls it so
  hidden-tab editors gate page input + stop the ants timer via
  `PdfPageControl.SetHostActive`→`ApplyInputGate` (WPF parity, minimal:
  render/scroll state stays warm). `EditorPage_SizeChanged` also
  re-clamps a visible ruler's centre via `ClampRulerCenter` so a
  shrinking viewport can't strand it off-canvas.
- `CloseTransientUi(reason)` (spec fix 2026-09-23) — the WPF transient
  sweep: restore-bounds cancels for text drag/resize +
  `page.CancelInteraction()` (covers the sticky drag) + search cancel/
  close + `CancelStickyNoteEdit` + `_textColorFlyout`/`_transientFlyout`/
  `_pageContextMenu` hides + sticky-marker `ContextFlyout.Hide()` sweep +
  inline-toolbar hide. Idempotent; the text-edit session is intentionally
  excluded (commits later via LostFocus/tool-switch — WPF parity).
  `_transientFlyout` tracks the last-shown tool/options/context flyout at
  each `ShowAt` site. `SetHostActive(false)` sweeps BEFORE the no-op
  early return (WPF ordering); reactivate re-shows the toolbar over a
  still-selected box. `ReleaseResources` shares the helper
  (`CloseTransientUi("release")`). Text-resize handles carry a
  transparent `Background` — panels only hit-test through a non-null
  Background.

- **Quality pass (2026-09-23 — focus, capture ownership, gesture
  guards):** `IsInteractiveEditorChrome` walks visual ancestors in
  `PreviewKeyDown` — focus inside `_inlineTextBoxToolbar`, any
  `ButtonBase`/`ComboBox`/`SelectorItem`/`Slider`/`MenuFlyoutItem`, or a
  non-selected `TextBox` owns its keys (arrows navigate combos, Delete
  edits characters); only the selected annotation `TextBox` or plain page
  elements fall through to nudge/Delete. Resize capture is owned by the
  `TextResizeHandleElement` (`_resizingTextHandleElement` +
  `_textResizePointerId`) — `CancelTextResize` releases THAT element, not
  the container, across tool-switch/Escape/undo/teardown/capture-loss.
  `_dragPointerId` mirrors it for border-band drags (set on press,
  equality-checked in Moved/Released/Canceled/CaptureLost, cleared on
  complete/cancel). The per-box `page.SizeChanged` auto-width hook is
  lifecycle-owned: `container.Loaded`/`Unloaded` subscribe/unsubscribe a
  named handler so EVERY detach (delete, undo/redo, cross-page transfer,
  clipboard, teardown) releases it — and reparenting re-binds to the NEW
  page. `PerformUndoAsync`/`PerformRedoAsync` cancel live drag/resize
  with `restoreBounds: true` so uncommitted geometry never survives as
  phantom layout; `ActivateTool` cancels `_draggedContainer` when leaving
  Text, not just `_resizingTextContainer`. Both `ExecuteInitialTransfer`
  call sites are try/catch-guarded and the undo/redo loops also honor
  `LastOperationSucceeded` on `AnnotationItemsAdded/RemovedAction`.
  Cross-page text drops fold a `TextAnnotationGeometry.ClampToPage`
  target-page clamp into the action delta (initial transfer AND redo land
  identically); `PasteSelection` clamps text boxes the same way.
  `ApplyTextContainerBounds` drops the per-packet synchronous layout —
  `InvalidateMeasure` alone reschedules handles (they're container
  children). The text edit session carries `_textEditSessionPage` +
  `_textEditSessionId` anchors so a stale `LostFocus` can't push an undo
  for a removed/reparented/reloaded container.

## Task 8 Phase B additions (2026-09-24 — images/highlights/markups/area/PDF text selection)

- **Six-mode highlighter flyout** (`ShowHighlighterFlyout`, WPF
  `_highlighterPopup` port): `HighlighterApplyMode`
  Freehand/TextHighlight/Underline/StrikeOut/Squiggly/AreaHighlight 3×2
  grid with mode-aware preview glyphs (`BuildHighlighterModePreview` —
  squiggly's smooth-cubic `S` segments spelled out as `C` because the
  shared mini path parser has no `S`), size slider (2–48, 0.5) + shared
  palette. Selecting a mode switches the live tool immediately via
  `ActivateHighlighterModeTool` → `GetActiveHighlighterToolType`
  (Freehand→`Highlighter`, AreaHighlight→`AreaHighlight`, the rest→
  `TextHighlight`); `IsHighlighterTool` keeps the toolbar button checked
  for all three.
- **`ApplyToolToAllPages`** gained `ToolType.AreaHighlight` →
  `CustomInkInputProcessingMode.AreaHighlight` (+ `AreaHighlightColor`/
  `AreaHighlightOpacity` from the highlighter colour) and arms
  `SetPdfTextSelectionEnabled` for `None`+`TextHighlight`.
- **Real PDF text selection**: page `PdfTextSelectionPointer*` events →
  press resolves the anchor offset via
  `PdfTextSelectionGeometry.FindNearestTextOffset` (24-DIP cap on press,
  ∞ while dragging), `_pdfTextSelectionInfo` comes from
  `TryGetCachedPageTextInfo`/`GetPageTextInfoAsync`, drag threshold 4 DIP,
  `UpdatePdfTextSelectionVisuals` repaints merged quads + refreshes
  `_selectedPdfText`; the press handler captures `requestId =
  Interlocked.Increment(_pdfTextSelectionRequestId)` AFTER
  `ClearPdfTextSelection()` — the clear increments the field itself, so a
  pre-clear capture is always stale and the drag never armed (the WPF
  original has the identical dead-arm bug — fixed here); Ctrl+C copies it
  when no annotation selection is
  live (`TryCopySelectedPdfTextToClipboard` — a successful copy toasts
  `Editor.TextCopied` + `\uE8C8`/1500 ms via `GetMainWindow().ShowToast`,
  WPF parity); Escape/tool-switch/lease
  loss → `ClearPdfTextSelection` (`_pdfTextSelectionRequestId` races
  in-flight loads). Release under `TextHighlight` commits a persistent
  `HighlightAnnotation` (`AddHighlightAnnotation` + `HighlightAddedAction`)
  or a `TextMarkupAnnotation` (`BuildTextMarkupAnnotation` → `AddTextMarkup`
  + `AnnotationItemsAddedAction`). Search-jump results paint the same
  quads without persisting.
- **Image annotations**: `PasteClipboardImageAsync` (Ctrl+V image-first —
  `ClipboardImageDecoder` PNG/Bitmap/EMF legs → `AddImageAsync` → one
  `AnnotationItemsAddedAction` + auto-select), `EditorPage_DragOver`/
  `EditorPage_Drop` wired on `EditorRootGrid.AllowDrop` (Explorer
  png/jpg/jpeg files land under the cursor, stair-stepped 20 DIP, one undo
  for all), image legs in `CopySelection` (base64 JSON),
  `PasteSelection` (verbatim dims + rotation), `DuplicateSelection`
  (re-decode raw payload at +20,+20), `HasPasteableClipboard`.
- **Collectors**: `CollectAnnotations` now emits `Strokes`, `HiddenInks`,
  `Texts`, `StickyNotes`, `Highlights` (via `page.GetHighlights()`),
  `Images` (base64 + live geometry + `RotationDegrees`), `TextMarkups`
  (relative rects rescaled by container-vs-original bounds),
  `AreaHighlights` (live container rect + RGBA). Load path is
  `LoadAnnotationsIntoPagesAsync` — image decode is awaited
  (`AddImageAsync` + `ApplyAnnotationRotation`); markups/areas/highlights
  restore through the quiet adders; `_isLoadingAnnotations` guards dirty.
- **Undo**: committed area-highlight drags push one
  `AnnotationItemsAddedAction` via `PageControl_AreaHighlightCreated`;
  `PageControl_ImagesChanged` marks dirty outside loads.

- **T9 save/autosave + close/dirty protocol (Phase A, 2026-09-24):**
  `DocumentSaveCoordinator` + `DocumentEditAdmission` + `DocumentReleaseState`
  (Core, WPF-identical) now drive the editor lifecycle. `_autoSaveTimer`
  (`DispatcherQueueTimer`, repeating, `Math.Max(15, AutoSaveIntervalSeconds)`)
  arms in ctor/`EditorPage_Loaded`/`EnsureAutoSaveTimer`/`ApplySettings()`;
  `AutoSaveTimer_Tick` re-entry-guards via `Interlocked.Exchange` and gates on
  `_isHostActive`/`_resourcesReleased`/`CanResumeInteraction` + lease —
  **WinUI keeps hidden-tab sessions alive** (WPF cancelled them on
  deactivate) so the host-active check lives on the tick; close/guard paths
  call `AutoSaveAsync` directly. `SaveCurrentDocumentWithLeaseAsync` shares
  ONE `_autoSaveInFlight` task under `_saveGate` (manual save joins an
  in-flight autosave); `SaveCurrentDocumentCoreAsync` collects
  `CollectAnnotations()` on the UI dispatcher only (thread-pool
  continuations `TryEnqueue` back), awaits `SaveAnnotationsToPdfAsync`
  (atomic replace via `PdfSaveCoordinator`+`PdfAtomicFile`), THEN writes the
  version sidecar (no ghost versions on failure), revalidating the
  `DocumentOperationLease` at every boundary. Manual save:
  `SavePdf_Click`/`Ctrl+S` → `SaveAnnotationsToPdfAsync` (NoDocumentLoaded /
  SavedSuccessfully toasts, SaveFailed `ContentDialog` via
  `WinUiDialogService` — XamlRoot falls back to the window content root).
  Close protocol: `PrepareForNavigationAsync`/`PrepareForCloseAsync`
  (shared in-flight tasks under `_lifecycleGate`) → commit text/sticky
  sessions → `BeginDocumentInteractionBlockAsync` (`_editAdmission.BeginClose`
  → `IsEnabled=false` subtree block → `WaitForQuiescenceAsync` →
  `DispatcherQueueBarrierAsync` Normal-priority drain) →
  `SaveUntilCleanAsync(finalClose:true)` → `CompleteClose`.
  `ReleaseResourcesAsync` joins `_releaseResourcesInFlight`, runs
  `MarkCleanupStarted`/`MarkSucceeded`/`MarkFailed`/`ResetAfterPreReleaseFailure`,
  cancels the operation session, unsubscribes the timer, awaits
  `_pdfService.DisposeAsync`. `CancelClosePreparation`/`ResumeDocumentInteraction`
  reopen admission+coordinator (`CancelCloseRequest`+`CancelClose`) only when
  `CanResumeInteraction`. `ReleaseResources` (Unloaded/`OnNavigatedFrom`/
  `ShutdownEditor`) now ALWAYS funnels through `DeferredTeardownAsync`:
  it joins any pending protocol task, then runs the same awaited
  `ReleaseResourcesAsync` teardown a managed close uses (save barrier +
  awaited `PdfService.DisposeAsync`), so a mid-teardown throw can no longer
  escape the Unloaded handler or leave a false "released" marker. A refused
  or faulted deferred release calls `_releaseState.MarkFailed()` — a
  detached editor has no retry path and must never silently resume.
  `LoadPdfAsync` drains in-flight saves via `DrainInFlightDocumentSaveAsync`
  (`_autoSaveInFlight` + coordinator `InFlightSave`, the FULL task including
  the version sidecar) before `_documentSaveCoordinator.Reset()`, and
  refreshes `RecentFilesService.UpdateMetadata` after a validated load.
  Every async-void handler (insert clicks, debounce ticks, autosave tick)
  now has a last-resort `catch (Exception)` — there is no
  App.UnhandledException backstop. The close-prep failure path fire-and-
  forgets its error dialog (observed via OnlyOnFaulted continuation) so a
  stuck DialogGate cannot wedge `_closePreparationInFlight`; OCE surfaces
  `Editor.SaveTimedOut`.
  `TryBeginDocumentEdit` leases guard `PushUndoAction`, undo/redo,
  `InsertExternalDocumentAsync`, `RotateCurrentPage_Click`; both doc-ops
  flush a dirty doc via `AutoSaveAsync` before rewriting the binary PDF.
  Both are undoable through the private `DocumentSnapshotAction` (WPF
  parity): they snapshot the before/after PDF bytes + persisted bookmark
  list, `ReloadDocumentForOperationAsync` re-loads under the fresh session
  and hands back a new lease, and a mid-operation failure rolls the file
  bytes + bookmark sidecar back before the failure toast. Undo/redo pass
  their operation lease to the action via `SetOperationLease` and validate
  `snapshot.CompletedOperationLease` (the lease of the reloaded session);
  `ApplyDocumentSnapshotAsync` writes bytes through
  `PdfSaveCoordinator.RunExclusiveAsync` + `PdfAtomicFile` (temp → flush →
  `Replace`), reloads, restores focus, refreshes bookmarks and calls
  `RecentFilesService.UpdateMetadata`. `LeavesDocumentDirty => false` — the
  snapshot write already persisted the state.
  `_documentSaveCoordinator.Reset()` runs on each document load;
  `EditorPage_PreviewKeyDown` swallows all shortcuts while
  `_documentInteractionBlocked`.
- **Page chrome + structural ops (T9-B, WPF `CreatePageHost`/
  `CreatePageInsertGap` parity):** `AddPdfPage` wraps each page in a host
  Grid carrying a hover-only delete button (`Editor.PageDelete` AutomationId
  family) and inserts a `CreatePageInsertGap` zone BEFORE every page after
  the first plus one trailing gap (`Editor.PageInsertButton.{index}`) — the
  hover "+" opens `PageTemplatePickerDialog` (insert mode, dialog-gate
  serialized). `InsertPageAtAsync`/`DeletePageAtAsync` run the same
  lease-guarded sequence as insert-external/rotate: edit admission → dirty
  `AutoSaveAsync` flush → before/after byte snapshot → Core `InsertPageAsync`/
  `DeletePageAsync` → `ReloadDocumentForOperationAsync` (fresh-session lease)
  → `JumpToPage` + `RecentFilesService.UpdateMetadata` +
  `PageBookmarkService.ApplyPageInsert/Delete` + `RefreshBookmarks` →
  `DocumentSnapshotAction` undo → toast. Single-page documents refuse delete
  up front (`Editor.PageDeleteBlocked`); a cancelled picker disposes the
  lease without touching the document.
- **Version history (T9-B, WPF `VersionHistory_Click` parity):**
  `VersionHistoryButton` (enabled in XAML) opens a `MenuFlyout` listing
  `VersionControlService.GetVersions` entries (creation-time stamped, newest
  first, `MaxHeight=300` presenter + vertical scrollbar, `MaxVersions=50`
  sidecars). The handler captures `menuSessionId`/`menuPath` at open so a
  mid-menu document swap can't act on the replacement document. Each item's
  async click captures a `DocumentOperationLease` bound to that snapshot,
  then: `LoadVersionAsync` → `SaveVersionAsync(current annotations)` FIRST
  (restore stays reversible) → `DeselectTextBox`/`CancelStickyNoteEdit`/
  `ClearAllAnnotations()` (per-page sweep: stores clear quietly, overlay
  canvases + payload maps wiped — sticky/text chrome is closed first because
  the sweep cannot reach a root-level Popup like the WPF canvas clear did) →
  `ClearUndoRedoHistory` (old undo entries must not reinsert pre-restore
  annotations) → `_pdfService.ExtractedAnnotations = data` →
  `LoadAnnotationsIntoPagesAsync` → `Editor.RestoredVersion` toast →
  `MarkDirty`. `OperationCanceledException` and stale leases exit silently.
  The flyout registers as `_transientFlyout` so `CloseTransientUi` retires it.
- **Settings (T9-B):** `ApplySettings()` delegates to the new
  `ApplySettings(AppSettings)` overload (staged snapshot — the settings
  dialog's live preview calls this on every control change). On a
  performance-mode change it resets `_lastRenderedDpiScale`, clears
  `_pagesInitiallyRendered`/`_pagesRenderedAtScale`, trims the working set
  and `KickViewportRender()`s a re-raster under the new `PdfRenderPolicy`
  profile (WPF `RenderVisibleWorkingSetAsync` parity).

## T12-B Fluent editor chrome (2026-09-26)

- **Toolbar styles are animated Fluent templates.** `ToolbarButtonStyle`,
  `ToolbarToggleButtonStyle` and `DocumentSidebarNavButtonStyle` share the
  T12-A MainWindow convention: a `StateLayer`/`NavStateLayer` Border whose
  Opacity is interpolated by generated `VisualTransition`s (hover/press
  swap a brush on the layer because brush objects cannot animate),
  `Pressed` adds a 0.96/0.97 scale squish on the root `ScaleTransform`,
  `Disabled` = root Opacity 0.55. Transitions: normal/pointer-over 120 ms,
  pressed 60 ms, checked 150 ms.
- **Checked toggles** fade a `CheckedLayer` (`ThemeSelectionBrush`) and
  grow the named `ActiveBar` accent underline from 60 % width via a
  `ScaleTransform` — `ActiveBar` is a UIA/test probe, never rename it.
  **WinUI toggle VSM trap (quality-review fix):**
  `ToggleButton.ChangeVisualState` issues ONE `GoToState` with COMBINED
  names (`Checked`, `CheckedPointerOver`, `CheckedPressed`,
  `CheckedDisabled`, `Indeterminate*`) in the single CommonStates group —
  the WPF two-dimension model does not apply. All 12 stock state names
  live in `CommonStates` and every `Checked*` state carries the full
  checked visual inline (CheckedLayer + ActiveBar + the interaction
  overlay/scale for that state); a separate `CheckStates` group leaves
  Pressed visuals stuck for the whole checked lifetime.
- **Toolbar shell** stays the floating pill: `ToolbarBorder` =
  `ThemeToolbarBrush` (opaque — no acrylic), `ThemeRadiusPill`,
  `BorderThickness=1` `ThemeBorderBrush`, `ThemeShadow` +
  `Translation="0,0,16"`, `ToolbarEntranceTransform` Y drives the
  one-time `PlayToolbarEntrance` fade+slide (220 ms, gated on
  `ShouldAnimate`, `_toolbarEntrancePlayed` once per page instance;
  `CompleteToolbarEntrance` pins the end state on re-attach).
- **Zoom cluster** is a segmented pill (`ZoomSegmentPill`:
  `ThemeSurfaceAltBrush` + hairline + `ThemeRadiusCard`) containing
  `Editor.ZoomOutButton`/`Editor.ZoomLabel`/`Editor.ZoomInput`/
  `Editor.ZoomInButton` separated by 1-DIP `ThemeMenuSeparatorBrush`
  hairlines. The label still taps open `ZoomTextBox` inline editing.
- **Page navigator** (`CenteredPageJumpHost` overlaying
  `PageJumpReservedSpace` at the toolbar midpoint) is a 5-column
  segmented group inside a rounded `ThemeSurfaceAltBrush` host:
  chevron buttons (32×32 `ToolbarButtonStyle`), hairline separators at
  0.75 opacity, borderless semibold `Editor.PageJump` TextBox +
  subdued `/ N` `PageCountText`. `Editor.PageJumpGroup` HelpText keeps
  the DEBUG `current-page=N` probe. `PageJumpReservedSpace` is
  **coupled to the pill's auto-sized width** (~148 DIP at ≤3-digit
  page counts) — it reserves 152 DIP so neighbours never slide under
  the overlay; keep it ≥ the pill's real width if the navigator is
  ever widened.
- **DocumentSidebar** renders as one coherent Fluent card instead of a
  flat block: `Margin="12,70,0,12"`, outer Border =
  `ThemeSurfaceBrush` + `ThemeBorderBrush` hairline +
  `ThemeRadiusPill` + `ThemeShadow`/`Translation="0,0,8"`. Header row =
  `SidebarTitleLabel` (13 px semibold, names the active tab) +
  `Editor.Sidebar.Collapse` (32×32 `ToolbarButtonStyle`,
  `SidebarCollapseIcon` Kind flips PanelLeftClose/PanelLeftOpen).
- **Icon-led segmented nav:** `SidebarNavBar` is a bordered
  `ThemeSurfaceAltBrush` group of three equal columns
  (`Editor.Sidebar.Pages/Outline/Bookmarks`). The tab TextBlock labels
  are `Visibility="Collapsed"` in XAML — localization still writes them
  for the metadata contract, but the names surface via
  `SidebarTitleLabel`, localized `ToolTipService` tooltips and
  `AutomationProperties.Name`; `SetSidebarCollapsed` must NOT flip them
  visible again. Selection = `ApplySidebarButtonState(button, selected,
  label, cue)`: `ThemeSelectionBrush` background + `ThemeAccentBrush`
  1-DIP border + semibold + accent `Foreground` (each `LucideIcon`
  Stroke binds `Foreground` — that is how the active glyph tints) +
  per-button `*NavSelectionCue` accent bar in the content.
- **Thumbnail cards:** `SidebarPageItemTemplate` wraps each
  `ThumbnailImage_Loaded` image (fixed 132×170 Uniform letterbox) in a
  `ThemeRadiusCard` Border with `ThemeSurfaceAltBrush` fill and
  `{x:Bind CardBorderBrush}` hairline — `SidebarPageItem.IsSelected`
  flips it to `ThemeAccentBrush` (live `ResolveBrush`, same mechanism
  as `LabelForeground`) so the current page reads as an accent-ringed
  card. Items keep `Margin="0,2"` spacing.
- **Collapse/expand motion:** `UpdateSidebarChromeGeometry(bool
  animateTransition)` eases `DocumentSidebar.Width` 38↔184 with
  CubicEase EaseOut (`EnableDependentAnimation` — Width is a layout
  property). The requested 200 ms is a HINT —
  `WinUiThemeService.GetAnimationDuration` returns the app-wide
  `ThemeAnimationDuration` token (~160 ms) whenever nonzero, so the
  effective duration is the shared token (same for the 220 ms toolbar
  entrance request). The `PagesContainer.Margin` snaps to the 32/228
  contract IMMEDIATELY so the `pages-margin-left` DEBUG HelpText probe
  always reads settled geometry; `PagesShiftTransform.X` compensates
  and eases back to 0 so the stack appears to slide. **Compensation
  uses the arrange model** (`PagesArrangeXForMargin`) — `PagesContainer`
  is `HorizontalAlignment="Center"`, so while content fits the
  viewport its arrange X is `mL + max(0, viewportW − mL − mR − cw)/2`,
  not `mL`; the raw margin delta would overcompensate ~2× in the
  dominant fits-viewport layout. Both legs of the storyboard run
  `EnableDependentAnimation = true` — a mid-flight direction flip
  re-reads `PagesShiftTransform.X`/`DocumentSidebar.Width`, and
  independent (compositor) animations never update the DP (the read
  would see the stale start value and snap). Rapid toggles capture
  live values before `Storyboard.Stop()` and resume — no wedge.
  Instant paths: `!ShouldAnimate`, unloaded page, `_resourcesReleased`,
  AND the ≤375 DIP narrow auto-collapse
  (`AutoCollapseSidebarForNarrowLayout` and the
  `Editor.DebugSidebarNarrow` seam pass `animateTransition:false` —
  layout response, not a user toggle).
- **Immersive guard:** `ToggleImmersiveMode` calls
  `CancelSidebarGeometryAnimation` + `CompleteToolbarEntrance` before
  snapshotting pre-immersive chrome state (no half-run values in the
  snapshot); `UpdatePagesContainerMarginForSidebar` returns the
  default margin while `_isImmersiveMode` so a collapse/expand behind
  the hidden rail can't re-offset the stack (exit re-applies the
  contract margin). The immersive ENTRY path also routes through that
  helper (never a direct `PagesContainer.Margin` write) so the DEBUG
  `pages-margin-left` HelpText probe refreshes on entry too — the
  source contract pins the absence of the direct write.
  `EditorPage_Unloaded` and `ReleaseResourcesAsync` call
  `StopChromeAnimations` — no chrome motion outlives teardown.
- **Theme-swap re-stamp (review fix):** `WinUiThemeService.Apply`
  REPLACES brush objects in `App.Resources` — XAML `{ThemeResource}`
  lookups re-resolve, but brushes captured imperatively
  (`ApplySidebarButtonState`'s nav-cell Background/BorderBrush/
  Foreground, `SidebarPageItem.LabelForeground`/`CardBorderBrush`)
  keep the stale palette. `EditorPage` mirrors the
  `_languageChangedSubscribed` pattern: `_themeAppliedSubscribed`
  guards a `WinUiThemeService.ThemeApplied` subscription in
  `EditorPage_Loaded`, the handler (`EditorPage_ThemeApplied`, skipped
  when `_resourcesReleased`) calls `SetSidebarTab(_sidebarTab)` to
  re-stamp the nav cells (the icon `Stroke` ElementName binding picks
  up the new Foreground automatically) and loops
  `SidebarPageItems` calling `item.RefreshThemeBrushes()` (re-raises
  `PropertyChanged` for the two captured-brush properties so the
  x:Bind OneWay consumers re-read). The handler also re-stamps the
  persistent bookmark toggle (`ApplyLocalizedBookmarkLabel`),
  `RulerIcon.Stroke`, and **rebuilds a visible ruler overlay**
  (`_rulerVisual`/`_rulerTickCanvas` are nulled, `EnsureRulerVisual`
  re-creates with fresh brushes, and `_rulerCenter`/`_rulerAngle`/
  `_rulerLength` are restored — drag/rotate/resize state is dropped
  with the old visual). Unsubscribe lives in `ReleaseResourcesAsync`
  next to the language unsubscribe. Accepted residual: per-page
  delete/insert chrome and flyout color swatches also capture
  brushes imperatively but are rebuilt on each render/open, so they
  self-heal on next interaction.
- `DocumentSidebarListBoxItemStyle` stays `ListViewItemPresenter`-based
  (native hover/selection visuals) with `ThemeRadiusControl` corners;
  `ModernTextBox`/`ModernListBox` unchanged. Every chrome color is a
  `{ThemeResource}` — a repo-side check confirmed zero unresolved keys.

## Open Threads / Resume Context
- **Status:** GREEN — `tools/winui-editor-smoke.ps1` 60/60; Tasks 7A/7B/
  8A/8B + Task 9 Phase A (save/autosave + close/dirty protocol) + Task 9
  Phase B (page insert/delete chrome, version-history restore flyout,
  `ApplySettings(AppSettings)` perf-mode re-render) + Phase B quality pass
  (thumbnail context menu, structural-op latch + rollback, picker-key crash
  fix) live; WinUI build 0 err/0 warn, headless suite green — 695 tests
  across fixture batches (known `HwndSubclass` test-host teardown flake is
  environmental) incl. `WinUiDialogsServicesSourceTests` +
  `WinUiLocalizationCoverageTests`.
- **Deferred (by design):** dormant `promptSaveAsAfterLoad` draft
  flow (no WPF caller).

## Change History
- 2026-09-26 T12-B quality-review fixes (over `d5baeb9`): (1)
  `ToolbarToggleButtonStyle` rebuilt to the stock single-group VSM
  model — WinUI `ToggleButton` emits combined `Checked*`/
  `Indeterminate*` names in `CommonStates`, so every combined state
  now carries its full visual inline (fixed: Pressed layer + 0.96
  squish stuck for the whole checked lifetime, no hover/press/disabled
  feedback on checked toggles — all 11 toolbar toggles +
  `BookmarkToggleButton`). (2) `PagesShiftTransform` compensation now
  uses `PagesArrangeXForMargin` — the centered `PagesContainer`'s
  arrange X is `mL + max(0, viewportW − mL − mR − cw)/2`, so the old
  raw-margin delta overcompensated ~2× whenever content fit the
  viewport (teleport + drift rubber-band). (3) Shift animation gained
  `EnableDependentAnimation = true` so mid-flight flips read the live
  DP instead of a stale start value (Width leg already had it).
  (4) Nits: `PageLabel` automation-name bind `OneTime`→`OneWay`
  (re-localizes), unused `xmlns:primitives` removed, duplicate
  `DocumentSidebar.Width` write dropped, `PageJumpReservedSpace`
  144→152 with a coupling comment, and `EditorPage_ThemeApplied` now
  rebuilds a visible ruler overlay so its imperative brushes re-resolve
  (centre/angle/length preserved). | Devin
- 2026-09-26 T12-B spec-review fixes (over `f2d5349`): (1) theme-swap
  staleness — `EditorPage` now subscribes `WinUiThemeService.ThemeApplied`
  in `Loaded` (`_themeAppliedSubscribed` guard, unsubscribed in
  `ReleaseResourcesAsync` beside the language unsubscribe); the handler
  re-stamps nav-cell brushes via `SetSidebarTab(_sidebarTab)` and loops
  `SidebarPageItems` → new `SidebarPageItem.RefreshThemeBrushes()`
  re-raises `PropertyChanged` for `LabelForeground`/`CardBorderBrush`
  (the two imperatively captured brushes — bound consumers were keeping
  the pre-swap palette after Light→Dark/HC). (2) Doc comments corrected:
  the 200 ms / 220 ms animation requests resolve through
  `GetAnimationDuration` to the shared `ThemeAnimationDuration` token
  (~160 ms). (3) Immersive entry now calls
  `UpdatePagesContainerMarginForSidebar()` instead of a direct
  `PagesContainer.Margin` write so the DEBUG `pages-margin-left` probe
  refreshes on entry. Source-contract test updated to pin the helper
  route (`Does.Not.Contain` the direct write). | Devin
- 2026-09-26 T12-B Fluent editor chrome: toolbar/toggle/sidebar-nav styles
  rebuilt as StateLayer + generated-`VisualTransition` templates (120 ms
  hover / 60 ms press / 150 ms checked, 0.96 press squish, 0.55 disabled),
  `ActiveBar` kept and animated; zoom cluster + page navigator re-cut as
  segmented `ThemeRadiusCard` pills with hairline separators; sidebar is
  now one `ThemeSurfaceBrush`+shadow card with an icon-led segmented nav
  (tab labels Collapsed in XAML — names live on `SidebarTitleLabel`,
  tooltips and `AutomationProperties.Name`), accent-ringed thumbnail cards
  via `SidebarPageItem.CardBorderBrush`, and `UpdateSidebarChromeGeometry`
  animates width while the 32/228 margin contract snaps instantly
  (`animateTransition:false` for the ≤375 DIP auto-collapse +
  `Editor.DebugSidebarNarrow`); immersive toggles settle chrome
  storyboards first and `UpdatePagesContainerMarginForSidebar` yields the
  default margin while `_isImmersiveMode`. Build 0 err/0 warn; targeted
  source-test fixtures green. | Devin
- 2026-09-26 G5/G6 port: thumbnail drag-reorder live — manual
  `StartDragAsync` payload drag (NOT built-in `CanReorderItems`, which would
  mutate `SidebarPageItems` ahead of the lease pipeline): `PointerPressed`/
  `PointerMoved` arm a `ThumbnailDragPayload` record (source index + session
  + normalized path + item ref), `DragStarting` stamps it under
  `Caelum.ThumbnailDragPayload`, `DragOver` re-validates + resolves the slot
  (`TryResolveThumbnailDropSlot` — container-walk half-item split; gaps
  clamp to the nearest slot, not "past end" like WPF's OriginalSource probe)
  and raises `ThumbnailDropIndicator`. `Drop` → `MovePageAsync`: the WPF
  commit sequence verbatim (payload lease → autosave-dirty flush → byte +
  bookmark snapshot → `ReorderPagesAsync` → `ReloadDocumentForOperationAsync`
  → `JumpToPage(moved)` → `ApplyPageMove` → `DocumentSnapshotAction` undo)
  PLUS the WinUI structural latch + `TryRollbackStructuralOperationAsync`
  bytes+sidecar restore shared with insert/delete/duplicate. Escape cancels
  natively; `CloseTransientUi`/`LoadPdfAsync`/`SetHostActive` clear armed
  state + indicator. F11 immersive live: `ToggleImmersiveMode` ports the WPF
  Opacity/hit-test chrome hide (toolbar/sidebar/search + default pages
  margin) and adds the window presenter swap (`SetImmersiveFullscreen` →
  `AppWindowPresenterKind.FullScreen`/`Default`); Escape exits first,
  `SetHostActive(false)` exits on tab switch. Source pins:
  `WinUiThumbnailReorderAndImmersiveSourceTests`. | Devin
- 2026-09-26 G1/G8 port: print pipeline live via `Services/Win32Print.cs`
  (Win32 `PrintDlgEx` sheet on the MainWindow HWND → patched-DEVMODE
  `CreateDC` → per-page `StretchDIBits` of pdfium BGRA at printer DPI; the
  WinRT PrintManager pipeline was rejected — packaged CoreWindow plumbing is
  fragile unpackaged). `PrintMenuItem` enabled + `Ctrl+P` parity; blank
  context menu gained `Editor.Action.RefreshPage` →
  `RefreshCurrentDocumentPreservingEditsAsync` (autosave flush → fresh-session
  reload; unlike WPF a failed save aborts the reload so edits can't be
  dropped). Pure fit/DPI math in Core `PrintPageGeometry`; source pins in
  `WinUiPrintSourceTests`, behaviour in `PrintPageGeometryTests`. | Devin
- 2026-09-25 Structural-op residuals: `ApplyDocumentSnapshotAsync` now holds
  `_structuralOperationInFlight` (undo/redo shares the byte-write+reload
  pipeline; a held latch refuses via null → `LastOperationSucceeded=false`
  keeps the action on its stack — honest, retryable). Post-mutation reload
  failures no longer silent-return: `ReloadDocumentForOperationAsync`
  retires the incoming lease in its finally, so the new
  `TryRollbackStructuralOperationAsync` re-captures a fresh lease while the
  live session still owns the path (swap → silent bail), restores
  bytes+sidecar via `RollbackStructuralOperationAsync`, and surfaces the op
  failure (`Editor.DocumentReloadFailed` {0} inside each op's failure key).
  Rotate gained the same rollback coverage (bookmarks snapshotted so the
  restore is a no-op, not a wipe). Import's inline rollback was replaced by
  the shared helper — identical shape (bytes → Replace → reload → focus →
  bookmark repaint). | Devin
- 2026-09-25 Task 9 Phase B quality pass: thumbnail `ContextRequested` menu
  (WPF `BuildThumbnailContextMenu` parity — insert-blank-before / duplicate /
  delete, `Editor.Sidebar.Page.*` automation ids, session+path captured at
  menu-open via `RunThumbnailMenuOperationAsync`); `DuplicatePageAtAsync` +
  `InsertBlankPageBeforeAsync` added (blank-before inserts directly — WPF
  routed through the picker; picker stays on the "+" gaps); insert sequence
  shared via `InsertPageCoreAsync`; mid-op failures roll back bytes +
  bookmark sidecar + reload via `RollbackStructuralOperationAsync`
  (import-op parity); `_structuralOperationInFlight` Interlocked latch
  serializes all five structural ops + `RotateCurrentPage_Click`;
  hover insert/delete click lambdas gained last-resort `try/catch`;
  `operationLease = null` default paths removed (dead code — every caller
  captures a lease). | Devin
- 2026-09-25 Task 9 Phase B: page delete chrome + insert-gap zones wired (`InsertPageAtAsync`/`DeletePageAtAsync`, template picker under the dialog gate); `VersionHistory_Click` `MenuFlyout` restore flow (reversible snapshot-first semantics); `ApplySettings(AppSettings)` overload with performance-mode re-render; `VersionHistoryButton` enabled in XAML. | Devin
- 2026-09-24 Lifecycle hardening: unload funnels through `DeferredTeardownAsync` (sync `ReleaseCoreResources` removed); save drain before `LoadPdfAsync` reset; `RecentFilesService.UpdateMetadata` on load; async-void guards on all seven handlers; close-prep error dialog is fire-and-forget; deferred-teardown failures mark `_releaseState` failed; `Editor.SaveTimedOut` label for cancelled close/nav saves. | Devin
