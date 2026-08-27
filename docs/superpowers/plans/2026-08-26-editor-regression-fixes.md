# Editor Regression Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix selection, popup draw-through, shape undo, live-ink thumbnails, and toolbar/page-navigation presentation, then publish v5.2.3.

**Architecture:** Keep `EditorPage` as the document/tool/history coordinator and `PdfPageControl` as the page-local input/selection/stroke owner. Add only narrow test seams and a thumbnail compositor; preserve the stripped-PDF rendering boundary and stable stroke-placement ledger.

**Tech Stack:** .NET 8, WPF, NUnit, PdfiumViewer/PdfSharpCore, Inno Setup, GitHub Actions/Releases.

---

### Task 1: Selection and Ctrl multi-selection

**Files:** `OpenNotes.Tests/ShapeSelectionTests.cs`, `Controls/PdfPageControl.xaml.cs`, `Pages/EditorPage.xaml.cs`, and their `.ai` mirrors.

- [x] Add failing STA tests proving an open Select popup does not lose the first selection, broad/open drawings are selectable, and click/Ctrl-click/Ctrl-toggle-empty preserve same-page selection semantics.
- [x] Run the focused filter and verify the new tests fail for the evidenced popup-consumption and hit-test causes.
- [x] Implement the minimal popup/select routing and bounded stroke-hit fallback needed by the tests; do not enable cross-page accumulation.
- [x] Run the focused filter and confirm GREEN.

### Task 2: Popup dismissal gesture and shape-history semantics

**Files:** `OpenNotes.Tests/TransientUiSourceTests.cs`, `OpenNotes.Tests/StrokeReplacementProductionTests.cs`, `Pages/EditorPage.xaml.cs`, `Controls/PdfPageControl.xaml.cs`, and mirrors.

- [x] Add failing production-path tests: stationary outside click closes a pen popup without a stroke/history entry; movement past the drag threshold creates one stroke; one Undo after smoothing/recognition removes the drawing and Redo restores the ideal stroke.
- [x] Run focused tests and verify RED for the current pass-through and replacement-only history paths.
- [x] Add a pending popup-dismissal gesture boundary and record recognized new strokes as one add/remove history entry while preserving token/placement safety.
- [x] Run focused tests and confirm GREEN.

### Task 3: Annotation-aware live thumbnails

**Files:** `OpenNotes.Tests/SidebarScrollbarAndThumbnailSyncTests.cs`, `Pages/EditorPage.xaml.cs`, optional focused helper file under `Pages/`, and mirrors.

- [x] Add failing tests proving ordinary live ink changes thumbnail pixels and invalidates only the affected page with stale async results rejected.
- [x] Run the focused tests and verify RED because current thumbnails use only the clean Pdfium bitmap.
- [x] Composite ordinary strokes over the clean base bitmap at 42 DPI, freeze the result, and add page-local revision/session invalidation on ink mutation.
- [x] Run focused tests and confirm GREEN without changing `PdfService` annotation stripping.

### Task 4: Semantic toolbar icons and compact page navigator

**Files:** `OpenNotes.Tests/EditorToolbarVisualSourceTests.cs`, `OpenNotes.Tests/EditorNavigationSourceTests.cs`, `Controls/LucideIcon.cs`, `Pages/EditorPage.xaml`, and mirrors.

- [x] Add failing source/STA contracts rejecting `WandSparkles` for Laser, requiring explicit Hidden Ink reveal semantics, consistent tool spacing, and a compact symmetric centered page group.
- [x] Run focused tests and verify RED against the current vectors/geometry.
- [x] Add the named vectors and adjust only toolbar/nav layout; preserve handlers, localization, one-based editing, and AutomationIds.
- [x] Run focused tests and confirm GREEN at normal and narrow widths.

### Task 5: Integrated verification and v5.2.3 release

**Files:** version surfaces, `.ai/PROJECT_CONTEXT.md`, release artifacts.

- [x] Run all focused filters, then the full suite in a fresh process; isolate any environment/order-sensitive WPF host crash rather than accepting a partial run.
- [x] Run Release build, i18n verification, and `git diff --check`.
- [x] Update all version surfaces to 5.2.3, build the self-contained installer, and launch the installed executable in an isolated data root.
- [x] Commit and push `main`, create/push tag `v5.2.3`, wait for the GitHub release, and verify downloadable executable/installer assets and hashes.
