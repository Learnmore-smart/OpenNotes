# OpenNotes V6 — WinUI 3 Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild OpenNotes as a WinUI 3 (Windows App SDK) desktop app — `OpenNotes.WinUI` — with full feature parity with the current WPF 5.2.x app, sharing all platform-neutral logic through a new `OpenNotes.Core` library. The WPF app keeps shipping until WinUI parity is proven.

**Architecture:** Three projects in one solution:

| Project | TFM | Contents |
|---|---|---|
| `OpenNotes.Core` | `net8.0` | UI-free code: `Models/`, PDF services, save coordinators, settings, localization, bookmarks, version control, word→pdf, undo framework. No `System.Windows.*`, no `System.Windows.Ink.Stroke`, no `BitmapSource` — strokes become `StrokeAnnotation`-style POCOs, renders return `IPdfRasterizer` results (BGRA buffers/PNG bytes). |
| `OpenNotes` (WPF) | `net8.0-windows` | Existing app; progressively references `OpenNotes.Core` instead of duplicated files. Ships 5.2.x until V6 parity. |
| `OpenNotes.WinUI` | `net8.0-windows10.0.19041.0` | WinUI 3 app — unpackaged (`WindowsPackageType=None`), self-contained (`WindowsAppSDKSelfContained=true`), x64. Owns all UI: App, MainWindow tab shell, HomePage, EditorPage, PdfPageControl successor, settings, template picker, icons, theme. |

**Tech Stack:** .NET 8, Windows App SDK 1.6.x (`Microsoft.WindowsAppSDK`), `Microsoft.Graphics.Win2D` for ink/annotation rendering, `PdfSharpCore` (kept), `PdfiumViewer` native `pdfium.dll` P/Invoke for rasterization (WinForms host dropped — it was already dead code), NUnit for `OpenNotes.Core` tests.

**NEVER-change contracts to preserve** (from `.ai/PROJECT_CONTEXT.md`):
1. Annotation coordinates: DIP 96dpi in-memory; save `scale=72/96` + Y-flip; load `scale=96/72`.
2. Atomic PDF write: same-dir temp + `File.Move(temp, path, overwrite:true)`.
3. Strip-and-redraw annotation loading (`/Ink`, `/FreeText`, `/Highlight` pulled from `/Annots` before rasterization).
4. `IUndoAction` command-stack semantics (`DocumentSnapshotAction`, `LeavesDocumentDirty=false`).
5. Single-window tab shell with one `EditorPage` per tab; `Caelum` namespace/data dir, `WindowsNotesApp` package identity, installer AppId unchanged.

---

## Task 1: `OpenNotes.Core` project scaffold + UI-free services — ✅ DONE (`ab5fbb7` + `ca64e05`)

**As-implemented notes:** 19 services moved (not 20) — `NavigationCloseCoordinator` stayed in the WPF project: it is UI-free but semantically bound to WPF Frame/journal navigation; revisit during the navigation-abstraction task. `System.Drawing.Common` was NOT added — no moved file uses `System.Drawing` (Word→PDF uses COM + LibreOffice subprocess). `InternalsVisibleTo` added for `OpenNotes`, `OpenNotes.Tests`, and later `OpenNotes.WinUI` (`080296f`). `OpenNotes.slnx` turned out to be a `<Solution />` stub — only `OpenNotes.sln` was updated. `tools/verify-i18n.ps1` catalog path fixed in `ca64e05`. 466/466 tests green (batched).

**Files:**
- Create: `OpenNotes.Core/OpenNotes.Core.csproj`
- Move (git mv): the 19 UI-free services into `OpenNotes.Core/Services/` — `AppSettingsService`, `DocumentEditAdmission`, `DocumentOperationSession`, `DocumentReleaseState`, `DocumentSaveCoordinator`, `InteractionCancellation`, `LocalizationService`, `PageBookmarkService`, `PdfAtomicFile`, `PdfRenderPolicy`, `PdfSaveCoordinator`, `ProductInfo`, `RecentFilesService`, `RecycleBinService`, `UpdateCheckService`, `VersionControlService`, `WindowsEnvironment`, `WordDocumentImport`, `WordToPdfConverter`
- Move: `Models/` files that are already UI-free — `AnnotationModels.cs`, `AppLanguage.cs`, `AppSettings.cs`, `HiddenInkRevealState.cs`, `PageInsertTemplate.cs`, `TextAnnotationGeometry.cs`, `ThumbnailDropPlacement.cs`
- Modify: `OpenNotes.csproj` — `<ProjectReference Include="OpenNotes.Core\OpenNotes.Core.csproj" />`; remove moved files via `<Compile Remove="OpenNotes.Core\**"/>`-style patterns and delete local copies
- Modify: `OpenNotes.slnx`/`OpenNotes.sln` — add `OpenNotes.Core`

- [ ] **Step 1: Create `OpenNotes.Core.csproj`** — `net8.0`, `Nullable disable`, `ImplicitUsings disable`, `RootNamespace=Caelum` (namespace compatibility). Packages: `PdfSharpCore 1.3.67`, `System.Drawing.Common 8.0.8` (word converter + EMF decode only — NOT exposed on public APIs).
- [ ] **Step 2: `git mv` the listed files**, keeping `namespace Caelum.Services` / `Caelum.Models` names exactly (no namespace churn).
- [ ] **Step 3: Wire `ProjectReference`** in `OpenNotes.csproj`; delete moved originals from WPF project.
- [ ] **Step 4: `dotnet build OpenNotes.csproj -c Release`** — expect 0 errors; fix stray `using` gaps.
- [ ] **Step 5: `dotnet test OpenNotes.Tests`** — tests reference both projects transitively; all currently-passing tests stay green.
- [ ] **Step 6: Commit** `refactor: extract UI-free services and models into OpenNotes.Core`.

## Task 2: UI-free ink/geometry primitives in Core — ✅ DONE (`f4922d0`)

**As-implemented notes:** Core geometry namespace is `Caelum.InkGeometry`, not `Caelum.Geometry` — the latter collides with `System.Windows.Media.Geometry` inside `Caelum.*` code. Tests stayed in the existing `OpenNotes.Tests` project (`CoreStrokeGeometryTests`, 36 tests) rather than a new `OpenNotes.Core.Tests` project. `StrokeReplacementSnapshot.cs` moved verbatim to Core (it was already UI-free); `ShapeStrokeMetadata.cs` split into a Core identity/keys file plus a WPF `Stroke` extended-property facade. `PdfPageControl` delegates all pure math to `StrokeGeometry` via `WpfStrokeAdapter`; exact rendered-geometry ops (`Stroke.HitTest`, `GetEraseResult`, `GetBounds`, `RectangleStylusShape`) intentionally remain WPF-side — `StrokeGeometry.SplitStrokeAtEraser` is the capsule-model approximation for the UI-free host only. Reflection-pinned private method names kept as wrappers. Review follow-up (`refactor: delegate scribble recognition to Core stroke geometry`): `PdfPageControl.TryRecognizeShape` now delegates to `StrokeGeometry.TryRecognizeShape` and the leftover WPF-typed duplicate stack (threshold constants, `DirectionRun`, `LooksLike*` gates, `DirectionBucket`, `Dist`, `PerpendicularDistance`) was deleted. Required fixtures 93/93; all 57 fixtures green in per-fixture batches (~493 tests).

**Files:**
- Create: `OpenNotes.Core/Models/InkPointData.cs` — `{ double X, Y; float Pressure; }`
- Create: `OpenNotes.Core/Models/InkStrokeData.cs` — points list, RGBA, size, `IsHighlighter`, `FitToCurve`, shape metadata (mirror `StrokeAnnotation` semantics 1:1)
- Create: `OpenNotes.Core/Geometry/StrokeGeometry.cs` — port of the eraser/selection math currently in `System.Windows.Media` (hit-test, split-at-point, bounds, path build) expressed on `InkStrokeData`
- Modify: `Models/StrokeReplacementSnapshot.cs`, `ShapeStrokeMetadata.cs` — replace `System.Windows.*` types with Core equivalents; WPF side gets thin adapters

- [x] **Step 1:** Write Core primitives + port `StrokeEraserGeometryTests` expectations into `OpenNotes.Core.Tests` (or keep existing NUnit suite compiling against Core types via adapters).
- [x] **Step 2:** Move eraser/split/hit-test math into Core; WPF `Stroke` ↔ `InkStrokeData` adapters live in the WPF project (`WpfStrokeAdapter`).
- [x] **Step 3:** WPF build + tests green.
- [x] **Step 4: Commit.**

## Task 3: PDF rasterization abstraction

**Files:**
- Create: `OpenNotes.Core/Pdf/IPdfRasterizer.cs` — `PageCount`, `PageSize`, `RenderPng(pageIndex, dpiScale)` / `RenderBgra(pageIndex, w, h, dpi)`
- Create: `OpenNotes.Core/Pdf/PdfiumRasterizer.cs` — wraps `pdfium.dll` via PdfiumViewer's document API (no WinForms dependency on public surface) returning PNG bytes or pinned BGRA
- Modify: `Services/PdfService.cs` — split into `OpenNotes.Core/Services/PdfService.cs` (document ops, annotation strip/save, page edits) + render call sites taking `IPdfRasterizer`; WPF keeps `RenderPageBitmapSourceAsync` adapter converting BGRA → `BitmapSource`
- Modify: `Pages/ThumbnailCompositor.cs` — accept rasterizer output instead of BitmapSource directly

- [ ] **Step 1:** Failing/Core-first contract: rendering the known 3-page fixture returns identical PNG SHA-256 as current WPF path.
- [ ] **Step 2:** Implement `PdfiumRasterizer` (reuse existing render flag/DPI math from `PdfService.cs` ~line 1513).
- [ ] **Step 3:** WPF adapter + tests green (PdfRenderPolicyTests, PdfService*Tests).
- [ ] **Step 4: Commit.**

## Task 4: `OpenNotes.WinUI` project + app shell

**Files:**
- Create: `OpenNotes.WinUI/OpenNotes.WinUI.csproj` — pattern after `wip/winui-cutover` spike: `net8.0-windows10.0.19041.0`, `TargetPlatformMinVersion 10.0.17763.0`, `UseWinUI=true`, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `Platforms x64`, `EnableMsixTooling`, `DISABLE_XAML_GENERATED_MAIN` (keep custom entry point). Packages: `Microsoft.WindowsAppSDK 1.6.x`, `Microsoft.Graphics.Win2D`, `Microsoft.Windows.SDK.BuildTools`. `ProjectReference` → `OpenNotes.Core`.
- Create: `OpenNotes.WinUI/App.xaml(.cs)` — WinUI `Application`; merged `Fluent XAML ThemeResources` + ported theme tokens from `App.xaml`
- Create: `OpenNotes.WinUI/MainWindow.xaml(.cs)` — WinUI `Window` (not WPF `Window`): ExtendsContentIntoTitleBar or custom chrome, tab strip, `Frame` navigation identical to current `AppTab`/`_tabs` model
- Create: `OpenNotes.WinUI/app.manifest`, `Package.appxmanifest` (Version 6.0.0.0, same `WindowsNotesApp` name + publisher)

- [ ] **Step 1:** Create csproj + minimal `App`/`MainWindow` that launches an empty window. Build via `build.ps1` (CJK-path junction already handles WinUI XAML compiler limitation).
- [ ] **Step 2:** Launch smoke — window opens, title `OpenNotes`, closes cleanly.
- [ ] **Step 3:** Port theme resource keys (`Theme*Brush`) into `App.xaml` ThemeDictionaries; `ThemeService` port swaps `RequestedTheme` + resource overrides.
- [ ] **Step 4:** Tab strip: new/close/activate/drag-reorder against `_tabs` model (reuse `AppTab` semantics; `AppTab` moves to Core minus `Frame`).
- [ ] **Step 5: Commit** `feat(winui): V6 shell — app, themed chrome, tab strip`.

## Task 5: HomePage (library) on WinUI

**Files:**
- Create: `OpenNotes.WinUI/Pages/HomePage.xaml(.cs)` — library grid, search/sort, folder colors, tile drag/move, recycle-bin delete, export — consuming `RecentFilesService`/`RecycleBinService`/`PdfAtomicFile` from Core
- Port: `Pages/HomePage.DragDropHelper.cs`, `HomePage.Utilities.cs`, `HomeTileTemplateSelector.cs`

- [ ] **Step 1:** XAML port (WPF→WinUI control swaps: `ContextMenu`→`MenuFlyout`, `Style` triggers→`VisualStateManager` where needed, `DynamicResource`→`ThemeResource`/`StaticResource` or code-applied brushes).
- [ ] **Step 2:** Wire Core services; open PDF → navigates to EditorPage stub.
- [ ] **Step 3:** Smoke: seeded `%LOCALAPPDATA%\Caelum` library renders tiles; context commands work.
- [ ] **Step 4: Commit.**

## Task 6: EditorPage shell — scroll/zoom/pages/sidebar/toolbar

**Files:**
- Create: `OpenNotes.WinUI/Pages/EditorPage.xaml(.cs)` — port the layout: `PdfScrollViewer` + centered `PagesContainer`, floating toolbar, `DocumentSidebar` overlay (Pages/Outline/Bookmarks, collapse rail, **including the 5.2.15 content-offset fix**), page-jump navigator, `PdfSearchPanel`, loading overlay
- Create: `OpenNotes.WinUI/Controls/PdfPageControl.xaml(.cs)` — page frame: `PdfImage` (+overlay), `ImageOverlayCanvas`, `TextOverlayCanvas`, `HighlightsCanvas`, `PdfTextSelectionCanvas`, `SelectionOverlayCanvas`, `HiddenInkCanvas`, `EraserCanvas`, `LaserInkCanvas`, `ShapePreviewCanvas` — minus WPF `InkCanvas` (replaced by Task 7 ink surface)

- [ ] **Step 1:** Load PDF via Core `PdfService`+`IPdfRasterizer`; pages stack renders bitmaps at DIP scale; scroll/zoom transform parity.
- [ ] **Step 2:** Sidebar rail + content offset (228-DIP rule), narrow auto-collapse (≤375), thumbnails from rasterizer.
- [ ] **Step 3:** Toolbar buttons present with ported `LucideIcon` (WinUI `PathIcon`/geometry), AutomationIds preserved.
- [ ] **Step 4: Commit.**

## Task 7: Custom ink engine (replaces WPF InkCanvas)

**Files:**
- Create: `OpenNotes.WinUI/Controls/InkSurface.cs` — `Canvas`-hosted control: `PointerPressed/Moved/Released/CaptureLost` capture → builds `InkStrokeData` (pressure from `PointerPoint.Properties.Pressure`, eraser-side detection from `PointerPoint.Properties.IsEraser`)
- Create: `OpenNotes.WinUI/Rendering/StrokeRenderer.cs` — renders `InkStrokeData` to `Win2D CanvasGeometry`/`PathGeometry` polylines (or `CanvasControl` draw), honoring `FitToCurve`/smoothing like WPF `Stroke`
- Port: eraser modes (whole-stroke + point eraser with stroke splitting — Core `StrokeGeometry`), shape tools (line/rect/ellipse/arrow/dashed/grouping), selection lasso + move/rotate/scale, hidden ink masks, laser fade (timer-driven alpha decay)
- Port: `WindowsPenService`/`HuaweiPenService` pressure + hotkey glue onto WinUI pointer model + `SetWindowSubclass` WndProc for Win+F19/20

- [ ] **Step 1:** Pen draws pressure strokes rendered live; stroke persists to `StrokeAnnotation` + undo action.
- [ ] **Step 2:** Eraser parity: whole-stroke + geometric point erase (`StrokeEraserGeometryTests` equivalent green against Core math).
- [ ] **Step 3:** Selection/move/rotate/scale + shape tools + undo/redo stack parity.
- [ ] **Step 4:** Hidden ink, laser, ruler overlay.
- [ ] **Step 5: Commit.**

**Review note (2026-09-22, Task 2 quality follow-up):** before the WinUI eraser ships, tighten `StrokeGeometry.SplitStrokeAtEraser` — replace the circle-capsule interval model with a square-stamp model (axis-aligned slab per spine segment, closed-form) matching the WPF `RectangleStylusShape` footprint, plus pressure-scaled radius (`eraser/2 + size·pressure(t)/2`), cross-validated against `Stroke.GetEraseResult` on a random stroke/eraser-path corpus. Also: the `InkStrokeData`↔`StrokeAnnotation` converter must decide whether per-point pressure enters the sidecar schema — the format is `[x,y]` only today and loads pressure as 0.5.

## Task 8: Text/sticky/image annotations + PDF text selection

- [ ] Port `TextOverlayCanvas` widgets (text boxes, drag handles, 8-point resize, rotation), sticky notes with Save/Cancel/Delete lifecycle, image annotations (clipboard/drag-in via `Windows.ApplicationModel.DataTransfer` + Win32 EMF path for Excel charts), persistent highlights, PDF text selection surface (was disabled stub in WPF — decide: keep stub-off or implement via PdfiumViewer text page API).
- [ ] **Commit.**

## Task 9: Save/load pipeline + dialogs + misc services

- [ ] `Save`/`Save As`/`autosave`/`DocumentSaveCoordinator` parity, close/dirty guards, tab close protocol (`NavigationCloseCoordinator`, `DocumentReleaseState`, `DocumentEditAdmission`)
- [ ] `SettingsWindow`, `PageTemplatePickerWindow` as WinUI windows/content dialogs; `DialogService` port; `PopupZOrderHelper` → WinUI flyout layering
- [ ] `UpdateCheckService` UI, toasts, `VersionControlService` UI, `RecycleBinService` (unchanged — `SHFileOperation`)
- [ ] Localization parity — `verify-i18n.ps1` equivalent check for WinUI sources
- [ ] **Commit.**

## Task 10: Parity verification + installer + release cutover

- [ ] Port remaining NUnit tests to `OpenNotes.Core.Tests`; document which UI tests become WinUI smoke scripts (`tools/Test-OpenNotes*.ps1` equivalents driving the WinUI window via UIA)
- [ ] `installer.iss` → V6 (new AppId decision: keep `WindowsNotesApp` name + Caelum data dir for upgrade compatibility), portable zip, `release.yml` job for `v6.*` tags
- [ ] Full manual feature checklist vs WPF build (checklist.md parity audit)
- [ ] `git tag v6.0.0` release

---

## Session-1 scope (this run)

✅ **Task 1** done (`ab5fbb7`, `ca64e05`); ✅ **Task 4 Step 1** done (`1fa9859`, `080296f`) — `OpenNotes.WinUI.exe` builds and launches an empty themed window, WASDK self-contained; ✅ **Task 2** done (`f4922d0`) — UI-free ink/geometry primitives in `OpenNotes.Core` (`Caelum.InkGeometry.StrokeGeometry`, `InkPointData`, `InkStrokeData`, shape identity/keys, replacement snapshots) with WPF adapters. Execution continues into Task 3+. Progress is tracked via checkboxes here and `.ai/PROJECT_CONTEXT.md`.
