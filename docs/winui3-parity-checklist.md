# OpenNotes V6 — WinUI 3 Parity Audit (Task 10)

> Audit date: 2026-09-25 · Branch: `v6/winui3` · Compares WPF `OpenNotes` (5.2.x channel)
> vs `OpenNotes.WinUI` (6.0.0 channel). Source of truth: WPF `Pages/EditorPage.xaml(.cs)`,
> `MainWindow.xaml(.cs)`, `Pages/HomePage*.cs`, `docs/checklist.md` vs the current WinUI tree.
> **G1/G8 closed 2026-09-26** (Win32 GDI print + RefreshPage item) — see rows.
> **G5/G6 closed 2026-09-26** (thumbnail drag-reorder + F11 immersive) — see rows.
>
> **Status legend:** ✅ ported · 🟡 partial (works, surface reduced) · ⏸ deferred (code path
> exists/stubbed, intentionally off) · ❌ dropped (with reason) · — not applicable to WinUI.
> `[manual]` = needs a real-device/desktop-session check; headless evidence only today.

## Headline gaps (open at audit time; G2/G3/G4 closed 2026-09-25, G5/G6 closed 2026-09-26 — see rows)

| # | Gap | Severity | Plan |
|---|---|---|---|
| G1 | **Print pipeline** — ✅ **ported** (`[manual]`): `PrintMenuItem` (`Editor.ContextMenu.Print`) enabled + Ctrl+P accelerates `PrintPdfAsync` — same lease/validation/`Editor.PreparingPrint` overlay/`Editor.PrintSent` `\uE749` toast/`Editor.PrintFailed` dialog/OCE-swallow flow as WPF. `BuildPrintablePagesAsync` keeps the atomic `%TEMP%\Caelum\Print\{guid}.pdf` copy + `SaveAnnotationsToPdfAsync` bake + temp cleanup; `RenderPrintablePages` rasterizes via `PdfiumRasterizerFactory.Shared` at the **printer's** DPI (Core `PrintPageGeometry.ResolvePrintRenderDpi`: min(X,Y) clamped to 96–600, then shrunk under a 250 MP total-job raster budget). Backend swap: the WinRT `PrintManager` task pipeline needs packaged CoreWindow plumbing, so `Services/Win32Print.cs` runs the classic Win32 `PrintDlgEx` sheet on the MainWindow HWND (printer choice + copies + collate + PD_PAGENUMS ranges) and spools BGRA pages through `CreateDC`/`StartDoc`/`StretchDIBits` — copies/collate are lifted out of the DEVMODE into a managed loop so no driver can double-replicate. `PrintablePageImage` now carries raw BGRA + page points (no BitmapSource) | closed | GDI spool is `[manual]`-verified only — no headless printer |
| G2 | **Pen tool flyout** — ✅ **ported** (`ShowPenFlyout`): size slider 0.5–8/0.25 (`Editor.Pen.Size`), `Editor.PopupPreview` live stroke, recents row + HSV palette, Pressure/Ink Simulation/Shape Recognition toggles (`Editor.Pen.Pressure`/`InkSimulation`/`ShapeRecognition`, persisted via `SaveSetting`), Off–High smoothing (`Editor.Pen.Smoothing.{i}`). Residual: flyout light-dismisses on first ink press (WPF's `StaysOpen`+gesture-arm kept the popup up mid-stroke); `XamlRoot.Size` stands in for `SystemParameters.WorkArea` in the scroll cap | closed | `ShowPenFlyout` + `BuildSettingToggleButton`/`WrapToolFlyoutContent` helpers |
| G3 | **Eraser mode flyout** — ✅ **ported** (`ShowEraserFlyout`): `Editor.Eraser.Pixel`/`Editor.Eraser.WholeStroke` mode row (persisted `WholeStrokeEraser`) above the 4–80 `Editor.Eraser.Size` slider; slider drags flash `EraserSizePreviewEllipse` for ~1.2 s (WPF `ShowEraserSizePreview`) | closed | eraser flyout opened from `EraserToolButton` like WPF `ToggleToolButton` |
| G4 | **Recent-colors row** — ✅ **ported**: `RefreshRecentColorsRow`/`BuildRecentColorsSection` render the «最近 Recent» swatch row (`Editor.Color.Recent.{i}`) above the palette in the pen + highlighter + text flyouts; recording/dedupe/cap live in new Core `RecentColors` (`MaxRecentColors=8`, `Record`, `TryParse`); the cached text flyout refreshes on `FlyoutBase.Opening` (WPF `popup.Opened`). Shape flyout intentionally has none — WPF keeps shape colours session-only | closed | pen/highlighter rebuilt per show ⇒ per-show refresh; text flyout `Opening` hook |
| G5 | **Sidebar thumbnail drag-reorder** — ✅ **ported** (`[manual]`): the rail runs the WPF manual-payload path, NOT built-in `CanReorderItems` (which stays `False` — it would mutate `SidebarPageItems` before the lease pipeline validates the move). `PointerPressed`/`PointerMoved` (attached `handledEventsToo` — ListViewItem marks the press handled for selection) arm an immutable `ThumbnailDragPayload` (SourceIndex + session + normalized path + item ref) and lift `StartDragAsync` past the 4-DIP threshold; `DragStarting` stamps it under `Caelum.ThumbnailDragPayload`. `DragEnter`/`DragOver` re-validate (`IsCurrentThumbnailDragPayload`: host-active + session + path + ref-identity), resolve the slot via `TryResolveThumbnailDropSlot` (container-walk half-item split — replaces the WPF `e.OriginalSource` ancestor probe so inter-item gaps clamp to the nearest slot instead of "past end"), and raise `ThumbnailDropIndicator`; `Drop` → `MovePageAsync` reuses the structural-op pipeline (payload-bound lease → edit admission → structural latch → dirty flush → `ReorderPagesAsync` → fresh-session reload → focus moved page → `ApplyPageMove` bookmark remap → `DocumentSnapshotAction` undo) with bytes+sidecar rollback on post-write failure; `Editor.PageReorderFailed` toast on error. Escape cancels the OS drag natively; `CloseTransientUi`/`LoadPdfAsync`/`SetHostActive` sweep armed state + indicator | closed | drop UX/indicator are `[manual]`-verified only; non-thumbnail drags bubble to the window file-import path |
| G6 | **F11 immersive fullscreen** — ✅ **ported** (`[manual]`): `ToggleImmersiveMode` hides `ToolbarBorder`/`DocumentSidebar`/`PdfSearchPanel` via Opacity=0 + IsHitTestVisible=false (overlay chrome — zero reflow; writing/scroll/page-jump/Ctrl+Z keep working), resets `PagesContainer.Margin` to the default, and swaps the window to `AppWindowPresenterKind.FullScreen` via `MainWindow.SetImmersiveFullscreen` (the piece WPF's borderless window got free); exit restores the recorded chrome + `Default` presenter — `AppWindow_Changed` re-applies custom chrome + min-size on each swap. `EditorPage_PreviewKeyDown` keeps WPF order: F11 gated on `!textInputFocused`, immersive-Escape ahead of resize/tool-reset Escape. `SetHostActive(false)` also exits — the presenter is window-global, unlike WPF's page-local chrome | closed | presenter swap + chrome restore are `[manual]`-verified only |
| G7 | **Scrollbar track click-to-jump** — WPF hooks `ScrollBar` template parts (`ScrollBarTrackJump_MouseLeftButtonDown`) so a track click centers the thumb; WinUI `ScrollViewer` does not expose that template surface | UX regression | Requires custom `ScrollBar` template or input hook — deferred by design |
| G8 | **`Editor.Action.RefreshPage`** — ✅ **ported**: `ShowBlankContextMenu` adds the item between SelectAll and Delete (WPF order) → `RefreshCurrentDocumentPreservingEditsAsync` = `AutoSaveAsync` flush → `ReloadDocumentForOperationAsync` fresh-session reload. Deviation: a failed save **aborts** the reload — WPF reloaded unconditionally, which would discard the in-memory edits the command exists to preserve | closed | save→reload under fresh-session lease |
| G9 | **Handwriting-to-text** — WPF ships no handwriting-to-text UI either (the `Editor.InkAnalysisUnavailable` degradation entry point was removed in Wave 3; no `InkAnalyzer` exists in either shell). WinUI *could* implement it via the WinRT API that WASDK exposes, but no code exists | parity-with-WPF-degraded | Optional: `Windows.UI.Input.Inking.Analysis` is reachable from WinUI 3 — a V6-only upgrade |
| G10 | **`releases/latest` update channel** — `UpdateCheckService` reads `/releases/latest`; after a v6.0.0 publish, a 5.2.x WPF install will be offered V6 (intended cutover path — same installer AppId upgrades in place). If a 5.x release ships *after* v6, `latest` regresses and 6.x installs see "no update" | release-channel caveat | Per-channel feed or tag-prefix filtering is a follow-up decision |

Already-handled contract items (for the record):
- Pen preset slots — intentionally **absent in both** (WPF removed visible slots; `PenPresets` JSON retained for compat, `EditorPage.xaml.cs:9661`).
- "Fit width / fit page" checklist entry — predates the Wave-3 toolbar redesign; no such buttons exist in current WPF. Not a V6 gap.
- Popup cross-app topmost fix (`PopupZOrderHelper`, Task-10-of-V5) — WPF HWND workaround; WinUI `MenuFlyout`/`ContentDialog` popups are XamlRoot-scoped and cannot leak across apps. N/A, documented in `WinUiDialogService`.

## Feature checklist by surface

### App shell / window chrome
| Feature | Status | Notes |
|---|---|---|
| Single window + tab strip (new/close/middle-click/drag-reorder) | ✅ | `ObservableCollection<AppTab>` + `ListView CanReorderItems`; `MoveTab` keeps WPF index math |
| Custom title bar / caption buttons | ✅ `[manual]` | `ExtendsContentIntoTitleBar` + `OverlappedPresenter` (WPF `WindowStyle=None` parity) |
| Nav Back/Forward/Home | ✅ | `NavigationCloseCoordinator` in Core; prepare-barrier replaces WPF journal (WinUI destroys pages — documented deviation) |
| Keyboard: Ctrl+T / Ctrl+W / Ctrl+Tab / Ctrl+Shift+Tab | ✅ | `RootGrid_PreviewKeyDown` |
| Window close intercept → per-tab release, 30 s timeout | ✅ | `AppWindow_Closing` + `_allowWindowClose` latch + `_tabCloseWorkflows` |
| Toasts (autosave, stylus detect, errors) | ✅ `[manual]` | `MainWindow.ShowToast` |
| Drag file onto window → import/open | ✅ `[manual]` | `MainWindow.Window_Drop` (PDF passthrough + Word→PDF) |
| F11 immersive fullscreen | ✅ | `VirtualKey.F11`/`VirtualKey.Escape` gates in `EditorPage_PreviewKeyDown` → `ToggleImmersiveMode` → `SetImmersiveFullscreen` (FullScreen presenter); tab deactivate exits |
| Popup cross-app z-order | — | WinUI popups can't leak apps (XamlRoot-scoped) |

### Home library
| Feature | Status | Notes |
|---|---|---|
| Tile grid (files + folders), folder open, breadcrumb/up | ✅ | `ItemsRepeater`+`UniformGridLayout` replaces WrapPanel |
| Search box, sort (name/date) | ✅ | `SearchBox`, `SortByNameMenuItem`/`SortByDateMenuItem` |
| Selection mode bar (select all / move / delete / done) | ✅ | `SelectButton` + selection action bar |
| Folder colors (8 swatches) | ✅ | `RecentFilesService.SetFolderColor` |
| Context menus (open/rename/color/move/delete/export/copy-path/remove) | ✅ | code-built `MenuFlyout`s via `ContextRequested` |
| Drag-out to Explorer | ✅ `[manual]` | Copy-only `DataPackageOperation.Copy` (WPF invariant) |
| Drag-into-folder + move-to-root | ✅ `[manual]` | `FolderTile_Drop`/`FileTile_DropCompleted` |
| Recycle-bin delete | ✅ `[manual]` | `RecycleBinService.TrySendToRecycleBin` |
| Word import (.doc/.docx → PDF convert + toast) | ✅ `[manual]` | `WordToPdfConverter` (LibreOffice/COM, Core) |
| New notebook / open file / create folder | ✅ | `Home.Menu.*` |

### Editor shell
| Feature | Status | Notes |
|---|---|---|
| PDF render (pdfium BGRA → `SoftwareBitmapSource`) | ✅ | Core `PdfiumRasterizer`, DPI-aware baseline |
| Scroll + anchored zoom (0.25–8, ±0.1, ctrl+wheel, pinch) | ✅ `[manual]` | `ZoomAroundPoint`→`ChangeView`; `ZoomMode=Enabled` pinch |
| Zoom label + editable % input (Enter/LostFocus, `%` strip, range check) | ✅ | `ApplyZoomFromTextBox` |
| Debounced re-render + working-set trim | ✅ | `DispatcherQueueTimer` ×2, `PdfRenderPolicy`, `PageSource=null` reclaim |
| Page jump field + prev/next + PgUp/PgDn/Home/End | ✅ | `Editor.PageJump*` ids preserved |
| Sidebar: Pages/Outline/Bookmarks tabs, 184/38 DIP rail, 228/32 content offset, ≤375 auto-collapse | ✅ | `SetSidebarTab`/`SetSidebarCollapsed`/`AutoCollapseSidebarForNarrowLayout` |
| Sidebar thumbnails (lazy), current-page highlight+scroll sync | ✅ | `ThumbnailListBox` ListView |
| Sidebar thumbnail drag-reorder | ✅ | manual `StartDragAsync` payload drag (WPF parity) → `MovePageAsync` under the structural latch; `ThumbnailDropIndicator` renders at the resolved slot; context menu keeps insert/duplicate/delete |
| Outline tree jump | ✅ | `TreeView` binds `TreeViewNode.Content` |
| Bookmarks: Ctrl+M toggle, list jump/remove, persisted | ✅ | `PageBookmarkService` (Core) |
| Ctrl+F search + results + F3/Shift+F3 cycle + Esc | ✅ | `PdfSearchPanel`, `MovePdfSearchSelection` |
| Loading overlay | ✅ | `LoadingOverlay` |
| Blank-area context menu (copy/paste/select-all/refresh/delete) | ✅ | `Editor.Action.RefreshPage` ported (G8) |
| Page context menu (print/rotate/export-PNG/insert-PDF/insert-image) | ✅ `[manual]` | print live via Win32 GDI (G1); rotate via `RotatePageAsync` + reload |
| Scrollbar track click-to-jump | ❌ G7 | WPF template-part hook has no WinUI equivalent |

### Ink engine (custom, replaces InkCanvas)
| Feature | Status | Notes |
|---|---|---|
| Pen/highlighter strokes, pressure capture, `[x,y,p]` persistence | ✅ | `InkSurface` + `StrokeRenderer`; legacy `[x,y]`→p=0.5 read |
| Point eraser (square-stamp parity) + whole-stroke eraser | ✅ | Core `StrokeGeometry`; `WpfCoreEraserParityTests` (23-case cross-validation vs `Stroke.GetEraseResult`) |
| Barrel button / pen-inversion erase; Win+F19/20 hotkeys | ✅ `[manual]` | `PenService` HWND subclass |
| Pen-only mode (blocks finger/mouse ink, panning allowed) | ✅ `[manual]` | `PenOnlyButton` + `PenOnlyInputTests` |
| Shape tools: 9 kinds, dashed, Shift 45°/square/circle snap, `ShapeGroupId` grouping | ✅ | `ShowShapeFlyout` ports all WPF options |
| Scribble → shape recognition (toggle from Settings + pen flyout) | ✅ | engine works (`StrokeGeometry.TryRecognizeShape`); `Editor.Pen.ShapeRecognition` toggle in `ShowPenFlyout` persists via `SaveSetting` |
| Ink simulation + smoothing levels | ✅ | `Editor.Pen.InkSimulation` toggle + `Editor.Pen.Smoothing.{0..3}` segmented row in `ShowPenFlyout`, persisted via `SaveSetting` |
| Hidden ink masks (draw/tap-reveal 3 s/erase/undo, `wna_hidden_` persist) | ✅ | `HiddenInkStore`, separate ledger |
| Laser pointer (0.15 s hold + 0.9 s fade, never persisted) | ✅ `[manual]` | `LaserInkCanvas` |
| Ruler (drag/rotate 15°-snap/resize, constrains strokes) | ✅ `[manual]` | `RulerOverlayCanvas`, `ConstrainPointsToRuler` |
| Eraser mode toggle UI | ✅ | `ShowEraserFlyout` pixel/whole-stroke row → `AppSettings.WholeStrokeEraser` |
| Pen flyout (size/color/preview/pressure/sim/recognition/smoothing/recents) | ✅ | `ShowPenFlyout` — full WPF `_penPopup` port |
| Recent colors row | ✅ | pen/highlighter/text flyouts; Core `RecentColors` (`MaxRecentColors=8`, dedupe newest-first) |

### Selection & clipboard
| Feature | Status | Notes |
|---|---|---|
| Marquee + lasso select (60%/70% rules), marching-ants outlines | ✅ `[manual]` | `IsStrokeInsidePolygon`/`IsStrokeInsideRect` |
| Ctrl+click multi-select / toggle | ✅ | `HandleCtrlClickToggle` |
| Move/rotate/scale (8 handles + rotate stem), opposite-anchor scale | ✅ `[manual]` | Core `TranslateSpinePoints`/`ScaleSpinePoints`/`RotateSpinePoints` |
| Cross-page move | ✅ | `InkSelectionCrossPageMoveAction` |
| Copy/paste (strokes+text+sticky+image), Ctrl+D duplicate, Delete | ✅ | `Ctrl+D` at `EditorPage.xaml.cs:9891` |
| Paste at last-clicked point (incl. cross-page) | ✅ | `_lastClickedPage` |
| Selection style popups (shape/filter) | ✅ | `ShowSelectionFlyout` |

### Annotations
| Feature | Status | Notes |
|---|---|---|
| Text boxes (create/edit session/8-pt resize/drag/nudge/min-size) | ✅ | `TextOverlayCanvas` widgets |
| Inline text toolbar (bold/italic/font/size/align/color/delete) | ✅ | `EnsureInlineTextBoxToolbar`, `Editor.TextToolbar.*` ids |
| Sticky notes (marker, Save/Cancel/Delete bubble, drag) | ✅ | `CommitStickyNoteEdit`/`CancelStickyNoteEdit`/`StickyNoteDeletedAction` |
| Image annotations (clipboard PNG/bitmap/EMF incl. Excel charts, drag-in) | ✅ `[manual]` | `ClipboardImageDecoder` + Core `EnhMetafileRasterizer` |
| Persistent highlights (120-alpha) + area highlights | ✅ | `AddHighlightAnnotation`, `InkSurfaceTool.AreaHighlight` |
| Text markups (underline/strikeout/squiggly) | ✅ `[manual]` | `PdfTextSelectionGeometry` relative rects |
| Full undo ledger (`IUndoAction`, `DocumentSnapshotAction`) | ✅ | Core `AnnotationUndoActions.cs`/`InkUndoActions.cs` |
| CJK text export correctness | ✅ | `PdfServiceAnnotationSavingTests` (unchanged Core path) |

### PDF text selection
| Feature | Status | Notes |
|---|---|---|
| Drag-select text, rects via `PdfTextSelectionGeometry` | ✅ `[manual]` | real implementation (WPF stub superseded — see plan T8-B) |
| Copy + markups (highlight/underline/strikeout/squiggly) | ✅ | `BuildPdfTextSelectionRects` + markup actions |

### Save / lifecycle
| Feature | Status | Notes |
|---|---|---|
| Manual save (button + Ctrl+S), autosave timer, dirty tracking | ✅ | `DocumentSaveCoordinator` shared task, `Math.Max(15, interval)` |
| Atomic PDF write + version sidecar ordering | ✅ | `PdfAtomicFile` temp→flush→move; sidecar after save |
| Dirty-close/tab-close prompt protocol | ✅ | `PrepareForCloseAsync`/`SaveUntilCleanAsync`/`DocumentEditAdmission` |
| Structural-op snapshot undo (insert/delete/duplicate/rotate) | ✅ | private `DocumentSnapshotAction`, `LeavesDocumentDirty=false` |
| Version history (cap 50, pre-restore auto-save) | ✅ `[manual]` | `VersionControlService` + `VersionHistoryButton` flyout |
| Notebook draft Save-As flow | — | dormant in WPF (no caller); documented T9 deviation |
| Print | ✅ `[manual]` (G1 closed) | `PrintDlgEx` + GDI `StretchDIBits` spool; annotations baked via temp-copy save |

### Dialogs & services
| Feature | Status | Notes |
|---|---|---|
| Settings (language/autosave/pressure/smoothing/pen-only/perf/theme/backdrop) | ✅ `[manual]` | `SettingsDialog` — stage/preview/revert contract |
| Page template picker (all 9 templates: blank/notebook/lined/quadrille/dotted/music/Cornell/checklist/two-column) | ✅ | `PageTemplatePickerDialog` |
| Update check (menu entry, GitHub latest-release, open release page) | ✅ `[manual]` | assembly-version aware (6.0.0); UA now reports caller version |
| About | ✅ | `WinUiDialogService` |
| Themes: Light/Dark/System/HighContrast + workspace backdrop + animation/opacity tokens | ✅ `[manual]` | `WinUiThemeService` (per-window `RequestedTheme` + `UISettings`/`AccessibilitySettings`/HKCU) |
| i18n (EN/zh/FR catalog, live language switch) | ✅ | shared `LocalizationService`; `WinUiLocalizationCoverageTests` pins coverage |
| Recycle bin | ✅ | `SHFileOperation` unchanged (Core) |
| Word→PDF converter | ✅ | unchanged (Core) |
| Poppler/Edge third-party viewer validation | — | output-format level: WPF smoke remains the harness |

## Test-port audit (WPF `OpenNotes.Tests` → V6)

There is **no separate `OpenNotes.Core.Tests` project** — all tests live in `OpenNotes.Tests`
(which references `OpenNotes.csproj`; `OpenNotes.Core` types are reachable transitively).
The V6 suite is a strict superset of the WPF suite plus new fixtures.

### A. Portable logic — same fixtures, now also covering Core (`OpenNotes.Core`)
`AnnotationTransformTests`, `AppSettingsCompatibilityTests`, `DocumentOperationSessionTests`,
`DocumentSaveCoordinatorTests`, `LocalizationServiceTests`, `PageBookmarkServiceTests`,
`PdfRenderPolicyTests`, `PdfSaveCoordinatorTests`, `PdfServiceAnnotationParsingTests`,
`PdfServiceAnnotationSavingTests`, `PdfServicePageEditingTests`, `ProductInfoTests`,
`RecentFilesServiceFolderColorTests`, `RecentFilesServiceLibrarySurvivalTests`,
`RecycleBinServiceTests`, `ThumbnailDropPlacementTests`, `UpdateCheckServiceTests`,
`WindowsEnvironmentTests`, `WordDocumentImportTests`, `WordToPdfConverterTests`,
`StrokeReplacementProductionTests`, `ShapeStrokeMetadataTests`, `HiddenInkTests`(Core-model half),
`EditorPopupDismissalTests`(logic half), `UnitTest1`, `ApplicationIconTests`.

### B. Core ink/annotation engine — new V6 fixtures (the WinUI engine itself)
`CoreStrokeGeometryTests`, `CoreInkEngineTests`, `CoreInkPhaseBTests`, `CoreAnnotationUndoTests`,
`WpfCoreEraserParityTests` (WPF `Stroke.GetEraseResult` ↔ Core cross-validation),
`WpfStrokeAdapterTests`, `PdfiumRasterizerParityTests` (byte-identical BGRA vs PdfiumViewer),
`Task8PhaseBTests`, `EditorTextStickySourceTests`.

### C. WinUI shell — new V6 source-contract fixtures
`WinUiSavePipelineSourceTests`, `WinUiDialogsServicesSourceTests`,
`WinUiLocalizationCoverageTests`, **`WinUiParitySourceTests`** (added this task: sidebar/zoom/jump,
tab-shell close protocol, theme, update wiring, home library, render lifecycle, packaging),
**`WinUiPrintSourceTests`** (G1/G8, 2026-09-26: enabled Print item + Ctrl+P →
lease-guarded `PrintPdfAsync` → `Win32Print` PrintDlgEx/GDI spool, temp-copy bake,
blank-menu `Editor.Action.RefreshPage` ordering + save→reload path) +
**`PrintPageGeometryTests`** (Core behavioural half: fit math + raster-DPI budget).

### D. WPF-UI-coupled fixtures — stay WPF-only by design
These instantiate WPF controls/STA or pin WPF source text; the WinUI equivalents are covered
by §B/§C source contracts + `tools/winui-*.ps1` smoke scripts at runtime:
`EditorNavigationSourceTests`→§C + winui-editor-smoke · `EditorPopupAutomationTests`,
`EditorPopupDismissalTests`(UI half), `TransientUiSourceTests`→N/A (WinUI popup model) ·
`EditorSelectionChromeSourceTests`→§B `CoreInkPhaseBTests` · `EditorToolbarVisualSourceTests`→
§C `ToolbarKeepsTheWpfAutomationIdSet` · `FluentChromeSourceTests`, `ThemeSurfaceSourceTests`,
`ThemeReviewContractTests`→§C `WinUiThemeServiceKeepsTheWpfThemeContract` ·
`HomePageLibrarySourceTests`, `HomePageDragDropHelperTests`→§C + winui-home-smoke ·
`MainWindowTabChromeSourceTests`, `TabDragCoordinatorTests`→§C + winui-uia-smoke ·
`MainWindowUpdateCheckSourceTests`→§C `UpdateCheckPrefersTheRunningAssemblyVersion` ·
`PerformanceLifecycleSourceTests`→§C `EditorPageKeepsTheRenderLifecycleContract` ·
`WordImportSourceTests`→§C `HomePageKeepsTheLibraryAndImportContract` ·
`DialogServiceTests`, `PageTemplatePickerLayoutTests`, `ThemeServiceTests`(WPF runtime),
`EditorTextSessionTests`, `TextAnnotationTests`(STA UI), `StickyNoteInteractionTests`,
`AreaHighlightTests`(WPF control math), `RulerInteractionTests`, `ShapeSelectionTests`,
`ShapeToolTests`, `ShapeRecognitionUndoTests`(WPF source), `StrokeEraserGeometryTests`
(WPF `Stroke` baseline — the parity corpus), `SidebarScrollbarAndThumbnailSyncTests`,
`ThumbnailCompositorTests`(BitmapSource), `PenOnlyInputTests`(WPF control path),
`ClipboardImageDecoderTests`(WPF leg), `EditorPopupAutomationTests`.

> Rationale: `OpenNotes.Tests` targets `net8.0-windows` and cannot reference
> `net8.0-windows10.0.19041.0` (WinUI); WinUI controls additionally need the
> running XAML runtime, so behavioral coverage must be source contracts +
> runtime smoke scripts — mirroring the WPF suite's own `*SourceTests` pattern.

## Release / packaging notes

- `installer.iss` is parameterized (`MyAppSourceDir`, `MyAppExeName`, `MyAppWinUIPayload`);
  defaults unchanged → **WPF 5.x releases keep working verbatim**.
- V6 keeps AppId `{{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}`, AppName `OpenNotes`,
  `%LOCALAPPDATA%\Caelum`, `WindowsNotesApp` identity — in-place upgrade over 5.x;
  `[InstallDelete]` sweeps stale `OpenNotes.*` WPF payload files when the WinUI leg runs.
- `release.yml` `release` job: skipped for `v6.*` tags; new `release-winui` job builds
  `dotnet publish OpenNotes.WinUI -c Release -r win-x64 --self-contained` →
  `publish-winui\` → ISCC + `OpenNotes-Portable-win-x64-{v}.zip`.
- Version surfaces: `OpenNotes.WinUI.csproj` = 6.0.0/6.0.0.0; `OpenNotes.csproj` stays
  5.2.15; `ProductInfo.Version` stays "5.2.15" (WPF channel fallback; both shells prefer
  their entry-assembly version). `UpdateCheckService` UA now reports the caller's version.
- `Package.appxmanifest` is the WPF packaging manifest only — WinUI is unpackaged
  (`WindowsPackageType=None`) so no V6 appxmanifest exists; version comes from the csproj.

## What is needed for the actual `v6.0.0` release (explicit user consent required)

1. Close or consciously accept gap list G1–G10 (G1/G8 + G5/G6 closed
   2026-09-26; G7 deferred by design, G9/G10 are follow-up decisions).
2. Manual run of the four `tools/winui-*.ps1` smokes on a real desktop session.
3. `git tag v6.0.0` + push → `release-winui` job builds setup + portable zip.
   ⚠ Tag push and release publish are **not** performed by this task.
