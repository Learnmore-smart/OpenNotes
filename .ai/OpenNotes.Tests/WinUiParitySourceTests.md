# OpenNotes.Tests/WinUiParitySourceTests.cs
> Last updated: 2026-09-25 (V6 Task 10 — parity audit coverage) | Protection: STANDARD

## Purpose
Task 10 source-contract fixture: pins the WinUI ports of surfaces that
previously had only WPF `*SourceTests` coverage. Behavioural halves live in
the Core fixtures; runtime verification lives in `tools/winui-*.ps1`.

## What It Pins
- Sidebar geometry: 184/38 DIP rail widths, 228/32 content-offset margin math,
  375-DIP narrow auto-collapse, three-tab ids + collapse button.
- Page jump (`Editor.PageJump*` ids, `EndPageJumpEdit`) + anchored zoom
  (`ApplyZoomFromTextBox` `%`-strip/range, `ZoomAroundPoint`→`ChangeView`).
- MainWindow tab shell: `ObservableCollection<AppTab>`, `MoveTab`,
  `AppWindow.Closing` first-cancel + `_allowWindowClose` latch, 30 s
  `CloseWorkflowTimeout`, `_tabCloseWorkflows`, `NavigationCloseCoordinator`,
  Ctrl+T/W/F/Tab accelerators, custom title-bar chrome.
- `WinUiThemeService`: per-window `RequestedTheme`, `ThemeApplied`,
  `UISettings`/`AccessibilitySettings`, WPF `Theme*Brush` key names at
  App.xaml root (no `ThemeDictionaries` — root entries win lookup).
- Update check: WinUI prefers `typeof(App).Assembly.GetName().Version`
  (6.0.0), `ProductInfo.Version` fallback only; Core UA = caller version.
- HomePage library contract: `RecentFilesService` mutation calls, Copy-only
  drag-out, `RecycleBinService`, Word→PDF import.
- Render lifecycle: `RenderPageBgraAsync`→`SoftwareBitmapSource`,
  `PdfRenderPolicy`, debounce timers, `PageSource=null` trim,
  `ShutdownEditor`/`ReleaseResourcesAsync`/`DeferredTeardownAsync`.
- Toolbar AutomationId set + context-menu ids (print pinned but disabled).
- Packaging: csproj pins `WindowsPackageType=None`, self-contained WASDK,
  `win-x64`, `Version 6.0.0` / `AssemblyVersion 6.0.0.0`.

## Important Notes / NEVER Change
- Anchors must stay in sync with `OpenNotes.WinUI/` sources — this fixture is
  the regression net for Task-10 parity claims in
  `docs/winui3-parity-checklist.md`.
