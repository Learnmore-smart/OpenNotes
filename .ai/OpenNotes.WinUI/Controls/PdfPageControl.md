# OpenNotes.WinUI/Controls/PdfPageControl.xaml(.cs)
> Last updated: 2026-09-24 (V6 Task 8 Phase B — images/highlights/markups/area highlights/PDF text selection) | Protection: STANDARD

## Purpose
`Caelum.Controls.PdfPageControl : UserControl` — the per-page frame stacked in
`EditorPage.PagesContainer`: fixed DIP size, `PdfImage` raster slot, the
`InkSurface` ink layer and the named overlay canvases the annotation tasks
(T7B/T8/T9) attach to.

## What It Does
- `PageIndex` (zero-based), `PageSource`/`SetPageImage(SoftwareBitmapSource)` —
  assigning replaces `PdfImage.Source` so the working-set trim can reclaim the
  old bitmap via `null`.
- XAML layer order mirrors WPF (`PageGrid`, with a `ThemeShadow` caster `Border`
  replacing the WPF DropShadowEffect): `PdfImage` → `PdfImageOverlay` →
  `ImageOverlayCanvas` → **`InkSurface` (ink strokes)** → `ShapePreviewCanvas` →
  `TextOverlayCanvas` → `HighlightsCanvas` → `PdfTextSelectionCanvas` →
  `SelectionOverlayCanvas` → `HiddenInkCanvas` → `EraserCanvas` (hosts the
  `EraserIndicator` `Ellipse`) → `LaserInkCanvas`.
- `Ink` exposes the `InkSurface` so `EditorPage.ApplyToolToAllPages` pushes
  tool/colour/size/`AppSettings` fields directly; the ctor assigns
  `InkSurface.EraserIndicator = EraserIndicator` and forwards
  `StrokeCollected`/`StrokeRecognized`/`StrokesErased`/`InkMutated`.
- `AddStroke(StrokeAnnotation)` — quiet sidecar load (no undo entry, mirroring
  the WPF loader). `CancelInteraction()` dismisses an in-flight stroke/erase
  gesture (scroll-pawn + tool-switch + teardown safety).
- `SetHostActive`/`SetDocumentInputEnabled` combine into `ApplyInputGate` →
  `InkSurface.InputEnabled` (+ `CancelInteraction` and
  `StopSelectionDashTimer` when the gate closes — hidden tabs keep no
  ticking ants; re-opening with a live selection rebuilds the chrome once).
- `SetPdfTextSelectionEnabled`/`SetPdfTextSelectionRects`/`ClearPdfTextSelection`
  — the real PDF text-selection layer (pointer-forwarding + painted rects,
  see Phase B below); `RefreshStickyNoteContextMenuLocalization` re-localizes
  marker menus.
- `AutomationProperties.AutomationId` = `PdfPageControl.<index>` (set by
  EditorPage) — load-bearing for `tools/winui-editor-smoke.ps1`.

## Phase B additions (2026-09-23)

- `CustomInkInputProcessingMode` = `None`/`Inking`/`Erasing`/`Shape`/`Laser`/
  `AreaHighlight`/`HiddenInk` — `SetInputMode` is the tool gate the page's own
  pointer handlers and `InkSurface` consult (EditorPage pushes it from
  `ApplyToolToAllPages`).
- **Selection** (`SelectionOverlayCanvas`): `SetSelectionMode`/`SetSelectionShape`
  (rect marquee vs free-form lasso)/`SetSelectionFilter`; `SelectAllAnnotations`,
  `SelectItems`, `ClearSelection`, `HasSelection`, `GetSelectionBounds`.
  Containment = Core `IsStrokeInsidePolygon` (60% point rule) /
  `IsStrokeInsideRect` (70%) / `HitTestClosedOrBounds`; all `ShapeGroupId`
  parts of a logical shape move together. Visuals: dashed bounds rect +
  marching-ants per-item outlines (`DispatcherQueueTimer` ~33 ms dash offset,
  gated on `WinUiThemeService.ShouldAnimate`), four 8×8 corner handles,
  rotate stem+knob 22 DIP above top-centre (all Core-computed). Gestures
  snapshot the pre-drag spine so `PointerCanceled`/capture loss restores it;
  completed drags raise `SelectionMoveCompleted`/`SelectionResizeCompleted`
  (total scale + opposite-corner anchor)/`SelectionRotateCompleted`
  (total degrees + centre) for the editor's undo push; `SelectionChanged`
  carries `HasSelection`+bounds for the flyout/keyboard paths. Outside-click
  + tool-switch deselect; Escape handled by the page-level key handler.
- **Shapes** (`ShapePreviewCanvas`): `CurrentShape`/`ShapeIsDashed`/`ShapeColor`/
  `ShapeStrokeSize`; live preview = `ShapeStrokeFactory.BuildPreviewPolylines`
  rendered via `LucideIcon.ParseIconGeometry` markup (WinUI has no
  `Geometry.Parse`); Shift applies `StrokeGeometry.ConstrainShapeEndpoints`
  (45° snap on Line/Arrow, square/circle on Rectangle/Ellipse); open kinds
  route through the ruler constraint at commit; release →
  `ShapeStrokeFactory.BuildShapeStrokes` quiet-adds every part then raises
  `ShapeCommittedUndoable` (editor pushes ONE `InkStrokesAddedAction` and
  arms Select — WPF order).
- **Hidden ink** (`HiddenInkCanvas` + `HiddenInkStore`): the canvas clips to
  its size via `SizeChanged` → `Clip = RectangleGeometry` (WPF
  `ClipToBounds="True"` parity — WinUI `Canvas` has no `ClipToBounds`
  member); freehand gesture →
  `HiddenInkAnnotation` (default `HiddenInkMaskColor` opaque 199,205,212,
  `HiddenInkSize` 28) → `HiddenInkCreated` event. Opaque `Polyline` visuals (keyed by mask Id in `_hiddenInkVisuals`) +
  tap-to-reveal (`DispatcherQueueTimer`, auto-recover after
  `HiddenInkRevealDurationMs`/`RevealDurationMs`, `HiddenInkRevealState`
  deadline math). Eraser path hits whole masks via
  `HiddenInkIntersectsEraser` → `HiddenInksRemoved` (batch
  `HiddenInksRemovedEventArgs` with captured indices — WPF's single
  press-remove branch is dead here because the visual is
  non-hit-testable while erasing; tap-remove rides the same swept
  gesture path); cancel rolls back quietly. `AddHiddenInk` = quiet
  loader, `GetHiddenInkData` = save-path collector (T9 `CollectAnnotations`).
  Store `Changed` args drive incremental sync: `Added` →
  `AddHiddenInkVisual(item, index)` (index-clamped `Children.Insert`
  preserves undo-restore z-order), `Removed` → `StopHiddenInkRevealTimer(id)`
  + drop just that visual (WPF `RemoveHiddenInkQuiet` parity — a removed
  mask's reveal timer must not keep ticking), `Cleared` → stop all +
  `RebuildHiddenInkVisuals`.
- **Laser** (`LaserInkCanvas`): surface events → red (`FF3B30`) 3-DIP
  `Polyline`s; completed strokes timestamp and fade on a ~30 ms
  `DispatcherQueueTimer` via `LaserInkFade.GetOpacity`/`IsExpired`
  (0.15 s hold + 0.9 s fade; `ShouldAnimate=false` expires instantly),
  `MaxLivePolylines` cap drops the oldest. Never stored/persisted/undoable.
- **Ruler bridge**: `GetRulerGeometryInPageCoords` delegate (EditorPage
  assigns per page — viewport→page `TransformToVisual` per query) feeds
  `InkSurface.RulerGeometryProvider`; `ProtectedCursor` (own-element only —
  WinUI keeps it protected) flips to the resize shape while the pointer is
  inside a corner handle.
- **Selection-chrome coalescing**: `NotifyGeometryChanged` raises one
  `Mutated` per stroke, so `InkMutated` → `QueueSelectionVisualsUpdate`
  dirty-flag + `DispatcherQueue.TryEnqueue` — the bounds/handles/ants
  overlay rebuilds at most once per frame, not N× per pointer packet
  (synchronous `UpdateSelectionVisuals` callers satisfy the queued
  request by clearing the dirty flag).
- Timers store on `Microsoft.UI.Dispatching.DispatcherQueueTimer` via an
  alias — `Windows.System` is also imported and its type wins unqualified
  (was a build break).

## Task 8 Phase A additions (2026-09-23 — text/sticky overlays)

- **`SetMode(bool isTextMode)`** — text-tool gate pushed by
  `ApplyToolToAllPages`: the page raises `TextOverlayPointerPressed`
  (empty-overlay press → editor's `CreateTextBox`) and
  `BackgroundPointerPressed` (sticky-tool placement + deselect + the
  `_lastClickedPage/_lastClickedPoint` paste anchor) instead of inking.
- **Overlay data registry** (`_overlayData: Dictionary<Grid,object>`) —
  the annotation payload behind a container (`StickyNoteAnnotation`
  today); `GetOverlayData`/`SetOverlayData`/`GetOverlayContainers` back the
  Core `IAnnotationContainerHost` implementation the undo actions replay.
- **Sticky notes** — `AddStickyNote(StickyNoteAnnotation)` builds the
  36-DIP marker `Grid` on `ImageOverlayCanvas` (below ink), clamps it into
  page bounds (`GetStickyPageSize`/`ClampStickyNotePosition`), syncs
  `note.X/Y` on quiet moves, and wires marker pointer events:
  left-drag → `StickyNoteMoved` (old/new position for the editor's
  `StickyNoteMovedAction`), activation (tap/double-tap) →
  `StickyNoteActivated` (editor opens the editor popup), context menu
  (`BuildStickyNoteContextMenu`, localized via
  `RefreshStickyNoteContextMenuLocalization`) →
  `StickyNoteDeleteRequested`. `SetStickyNotePositionQuiet`/
  `SetStickyNoteTextQuiet` are the undo replay setters.
- **Text containers** — text boxes live on `TextOverlayCanvas` (editor-
  built `Grid`s: chrome + TextBox + 8 `TextResizeHandle` squares). The page
  supplies `GetContainerRect`, `SetTextAutoSize`/`IsTextAnnotationAutoWidth`/
  `IsTextAnnotationAutoHeight` (persist-as-auto flags ride attached DPs
  `TextAnnotationAutoWidth/HeightProperty` ON THE ELEMENT — WPF attached-
  property parity, so cross-page moves keep them), `GetTextData()`/
  `TryGetTextAnnotation(container)` (live `TextAnnotation` collectors —
  WPF `CollectAnnotations`/clipboard parity) and
  `GetStickyNoteData()`.
- **Quiet mutators** — `RemoveTextContainerQuiet`/`AddTextContainerQuiet`
  work on both overlay layers (container is re-parented to whichever
  canvas hosts it: sticky → `ImageOverlayCanvas`, text → `TextOverlayCanvas`);
  `SetTextContainerPositionQuiet`/`SetTextContainerBoundsQuiet`/
  `SetTextContentQuiet`/`SetTextStyleQuiet`/`SetTextFormatQuiet` replay
  undo state without firing user events. `IAnnotationContainerHost`
  explicit impls adapt `object` containers → `Grid`.
- **Selection participation** — `SelectedTextContainers`; marquee/lasso
  hit-testing, Ctrl-click toggling, per-item outline chrome and the
  move/scale/rotate transforms all treat text+sticky containers as
  first-class items (`MoveItemsDirectly`/`ScaleItemsDirectly`/
  `RotateItemsDirectly` take both legs; `ReadAnnotationRotation`/
  `ApplyAnnotationRotation` persist `RotationDegrees`).
- `AnnotationSelectionChangedEventArgs` /
  `SelectionMoveCompletedEventArgs` / `SelectionResizeCompletedEventArgs` /
  `SelectionRotateCompletedEventArgs` carry the selected containers so the
  editor can push ONE mixed `Annotation*Action` per gesture.
- **`CancelInteraction()` covers the sticky drag** (spec fix 2026-09-23) —
  `CancelStickyDrag()` folds into the shared cancel so the editor's
  `CloseTransientUi` gesture sweep ends a captured marker drag too (WPF
  `InteractionCancellation.CancelAll` parity). Marker pointer capture
  targets the inner `Button` (owner of the Moved/Released handlers) —
  capturing the parent container would route the stream past the button
  whenever `ButtonBase` skips its internal capture on a handled press.
  `ApplyTextContainerBoundsQuiet` restores auto-size legs as `NaN` like
  the live path — a fixed restore would freeze an auto annotation.
- **Quality pass (2026-09-23):** `CancelSelectionInteraction` releases
  `SelectionOverlayCanvas` pointer captures while clearing state — a
  programmatic cancel (undo, teardown, tool switch) no longer leaves the
  pointer routed to a dead gesture. Sticky `PointerCanceled` checks the
  tracked `_stickyDragPointerId` like Moved/Released. `MoveItemsDirectly`
  coalesces live-drag chrome through `QueueSelectionVisualsUpdate()`
  (dispatcher queue); `CompleteSelectionGesture` runs a synchronous
  `UpdateSelectionVisuals()` after each completion event. The sticky
  marker keeps exactly ONE flyout path — explicit `ShowAt` on RightTapped
  (`e.Handled` suppresses the container's retrieval-anchor flyout); the
  button's own `ContextFlyout` is NOT set so the framework can't
  auto-open a second copy. `IAnnotationContainerHost` gained
  `ContainsTextContainer` (parent-probe on both overlay canvases) and
  every quiet mutator raises `InkMutated` so thumbnail/dirty observers
  refresh through undo/redo and quiet loads.

## Task 8 Phase B additions (2026-09-24 — images/highlights/markups/area/PDF text selection)

- **Image annotations** (`ImageOverlayCanvas`): `AddImageAsync(imageBytes,
  position, explicitW?, explicitH?)` decodes via `InMemoryRandomAccessStream`
  → `BitmapImage.SetSourceAsync` (eager, WPF `OnLoad` parity), sizes to 40%
  of the page when no explicit dims (Core
  `PdfTextSelectionGeometry.ComputeImagePlacementSize` rule), clamps into
  page bounds and registers the raw bytes in `_imageDataById` (the
  persistence payload — `GetImageData`/`SetImageData`, also the
  `IAnnotationContainerHost` cross-page transfer legs). `_imageContainers`
  is the ordered list `ImageContainers` exposes to the collector; the
  container is `IsHitTestVisible=false` (the selection overlay owns image
  interaction). `ImagesChanged` fires on every overlay-set mutation —
  images, markups AND area highlights (WPF `ImagesChanged` parity — the
  editor marks dirty through it).
- **Overlay annotations**: `AddTextMarkup(TextMarkupAnnotation)` draws the
  underline/strikeout/squiggly once into an inner `Canvas` inside a
  `Viewbox(Stretch=Fill)` — corner-handle rescaling scales the drawing for
  free; the model rides `_overlayData`. `AddAreaHighlight(AreaHighlightAnnotation)`
  is a `Grid` whose `Background` is the semi-transparent colour so stretch
  is automatic too. Both are non-hit-testable overlay containers tagged
  `MarkupContainerTag`/`AreaHighlightContainerTag` (`IsOverlayContainer`
  covers image/markup/area/sticky tags).
- **Persistent text-quad highlights** (`HighlightsCanvas`): `_highlights`
  list + `AddHighlightAnnotation(rects, color)` (commit path — fixed 120
  alpha), `AddHighlight`/`RemoveHighlight` (load + undo replay — the
  `IAnnotationContainerHost` legs `HighlightAddedAction`/
  `HighlightRemovedAction` call), `GetHighlights()` (collector),
  `RefreshHighlightsVisuals()` repaint.
- **Area-highlight drag**: rides the `InkSurface` shape-drag event pipeline
  (`Ink_ShapeDragStarted/Updated/Ended/Cancelled` route by
  `_currentMode == AreaHighlight`); `Begin/Update/EndAreaHighlightDrag`
  normalize anchor/current via `NormalizeAreaHighlightRect`, paint a
  dashed-edge (220 alpha) + translucent-fill (`AreaHighlightOpacity`, 76)
  preview on `ShapePreviewCanvas`, ignore sub-4-DIP gestures
  (`AreaHighlightDragThreshold`) and commit a container + raise
  `AreaHighlightCreated` (editor pushes one `AnnotationItemsAddedAction`).
- **PDF text selection** (`PdfTextSelectionCanvas`): armed by
  `SetPdfTextSelectionEnabled` (editor pushes `true` for `ToolType.None` +
  `TextHighlight`); pointer handlers capture the press pointer
  (`_pdfTextSelectionPointerId`, `CapturePointer`/`ReleasePointerCaptures`,
  `PointerCanceled`/`PointerCaptureLost` release) and forward
  page-DIP positions through `PdfTextSelectionPointerPressed/Moved/Released`
  (editor owns anchor/active offsets + the commit). `SetPdfTextSelectionRects`
  paints the merged quads; `ClearPdfTextSelection` drops them. Disarming
  releases capture + clears.
- **Quiet paths cover every overlay kind** — `RemoveTextContainerQuiet`
  detaches from whichever canvas parents the container (keeping
  `_overlayData`/`_imageDataById` entries so re-add restores as-is);
  `AddTextContainerQuiet` re-parents by `IsOverlayContainer` →
  `ImageOverlayCanvas` (re-registering `_imageContainers` for image tags).
- **`CancelInteraction`/`ReleaseResources` sweep the new state** — in-flight
  area-highlight drag, the text-selection pointer capture, `_imageDataById`,
  `_highlights`, both selection canvases.
- **`SelectAllAnnotations` (Ctrl+A) selects image containers too** — the
  concat is `TextOverlayCanvas` grids + `ImageOverlayCanvas` grids filtered
  by `IsOverlayContainer` (WPF parity); `GetOverlayContainers()` must NOT
  be used there because it deliberately excludes `IsImageContainer`
  (marquee/lasso/Ctrl+click already used the unfiltered tag check).

## Important Notes / NEVER Change
- The ink layer sits UNDER `ShapePreviewCanvas`/`TextOverlayCanvas` — strokes must
  not swallow overlay input.
- `SetBitmapScalingMode` intentionally absent (WPF toggled it during scroll/zoom;
  WinUI images sample full quality).
- Hidden-ink visuals and `HiddenInkStore` stay OFF the stroke ledger — ordinary
  lasso/eraser/undo must never touch masks; only `HiddenInkIntersectsEraser`
  decides mask removal (whole mask, never fragments).
- Deferred: overlay-swap animation (`PdfImageOverlay` staged swap), text/sticky
  hit overlays (T8).

## Open Threads / Resume Context
- **Status:** GREEN — Phase A ink (pen/highlighter/eraser, pressure, scribble
  shape recognition, undo seams) + Phase B (select/lasso/transforms, shapes,
  hidden ink, laser, ruler bridge) + Task 8 Phase A (text/sticky overlay
  surface, quiet mutators, `IAnnotationContainerHost`) + Task 8 Phase B
  (image annotations, persistent highlights, text markups, area highlights,
  real PDF text selection) live; renders BGRA pages.
