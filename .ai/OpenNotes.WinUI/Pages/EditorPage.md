# OpenNotes.WinUI/Pages/EditorPage.xaml(.cs)
> Last updated: 2026-09-24 (V6 Task 9 Phase A — save/autosave pipeline + close/dirty protocol) | Protection: STANDARD

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
  `StrokesErased` → `InkStrokesErasedAction`; `InkMutated` →
  `InvalidateThumbnail(pageIndex)` (cache eviction — ink is not composited
  into thumbs yet). Undo/Redo toolbar buttons carry the accelerators —
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
  service: `_penService` is one `Caelum.Services.PenService` PER editor
  page — `InitializePenService` (on `Loaded`) subclasses the window HWND
  for Win+F19/F20, `ToolToggleRequested` → `ToggleEraserMode` (eraser ↔
  `_previousTool`), and `PushPenServiceToPages` feeds each surface's
  `SetPenService`; the surfaces' `ProbePointer` calls accumulate
  `Capabilities` → `PenDeviceDetected` toast.
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
- **Flyouts**: `ShowShapeFlyout` (9 shape kinds in a 3×3 `Grid` —
  `UniformGrid` does not exist in WinUI — Solid/Dashed style, 1–20 size
  slider at 0.5 steps, shared HSV palette; WPF AutomationIds preserved:
  `Editor.Shape.<Kind>`, `Editor.Shape.Style.*`, `Editor.Shape.Size`),
  selection action-bar flyout (style apply → `ApplySelectedDrawingStyle`
  → `InkStrokesStyleChangedAction`), `ShowBlankContextMenu`.
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
  `ShutdownEditor`) defers to `DeferredTeardownAsync` while any protocol task
  is in flight, else runs `ReleaseCoreResources` synchronously.
  `TryBeginDocumentEdit` leases guard `PushUndoAction`, undo/redo,
  `InsertExternalDocumentAsync`, `RotateCurrentPage_Click`; both doc-ops
  flush a dirty doc via `AutoSaveAsync` before rewriting the binary PDF.
  `_documentSaveCoordinator.Reset()` runs on each document load;
  `EditorPage_PreviewKeyDown` swallows all shortcuts while
  `_documentInteractionBlocked`.

## Open Threads / Resume Context
- **Status:** GREEN — `tools/winui-editor-smoke.ps1` 60/60; Tasks 7A/7B/
  8A/8B + Task 9 Phase A (save/autosave + close/dirty protocol) live;
  WinUI build 0 err/0 warn, headless suite 685/685 incl.
  `WinUiSavePipelineSourceTests` 6/6 + `DocumentSaveCoordinatorTests`
  (incl. `NavigationCloseCoordinator` behavioral) 17/17.
- **Deferred (by design):** `SettingsWindow`/page-template dialogs,
  `VersionControlService` UI, dormant `promptSaveAsAfterLoad` draft
  flow (no WPF caller), update-check UI (T9-B+).
