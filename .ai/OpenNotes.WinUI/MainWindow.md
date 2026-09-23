# OpenNotes.WinUI/MainWindow.xaml.cs
> Last updated: 2026-09-23 (V6 Task 4 Steps 2–5 — themed chrome + tab strip) | Protection: STANDARD

## Purpose
WinUI 3 main-window code-behind (`Caelum.MainWindow : Microsoft.UI.Xaml.Window`, sealed partial). Ports the chrome + tab-model slice of the 2,094-line WPF `MainWindow.xaml.cs` — deliberately thin: editor tabs, save/close workflows, toasts, and toolbar clusters land in later tasks.

## What It Does
- **Window chrome:** `InitializeAppWindow` resolves `AppWindow` via `WinRT.Interop.WindowNative` + `Microsoft.UI.Win32Interop`, sizes/centers 1280×720 (`MoveAndResize`, WPF `WindowStartupLocation=CenterScreen` parity), sets `ExtendsContentIntoTitleBar=true` and `SetTitleBar(AppTitleBar)` so the 40px XAML title row is the real caption rect (drag, double-click maximize, system menu for free). Caption buttons call `OverlappedPresenter.Minimize()/Maximize()/Restore()` and `Window.Close()`; `AppWindow.Changed` swaps the maximize/restore glyph (WPF `MainWindow_StateChanged` parity).
- **Tabs:** `ObservableCollection<AppTab> _tabs` bound to the `TabStrip` ListView. `AddNewHomeTab` creates a `Frame` per tab (added to `TabContentArea`, only the active one `Visible`), navigates to `HomePlaceholderPage`, and activates on request. `ActivateTab` toggles `IsActive`/frame visibility and syncs `TabStrip.SelectedItem` under `_syncingTabSelection`. `CloseTab` removes the tab+frame; closing the last tab recreates a Home tab, closing the active tab activates `_tabs.Last()` (WPF parity). `MoveTab` keeps the WPF insert-before/after index math for programmatic moves.
- **Tab strip input:** ListView single-selection → `ActivateTab` (guarded by `_syncingTabSelection` during programmatic changes and removal); `CanReorderItems` gives built-in drag-reorder (mutates the bound collection in place); middle-click on a tab pill closes it; the close button consumes the left press (WPF `PreviewMouseLeftButtonDown` parity — no activate-then-close) and its `Click` covers keyboard activation.
- **Navigation:** `NavBack`/`NavForward`/`NavHome` operate on `ActiveFrame`; `Frame_Navigated` refreshes enabled state and resets home-tab metadata (`UpdateActiveTabInfo` — editor branch deferred).
- **Startup:** `AppSettingsService.Load()` → `WinUiThemeService.Apply(theme, workspaceBackdrop)` + `LocalizationService.ApplyLanguage`, `WinUiThemeService.RegisterWindow(this)` + `ThemeApplied` → `AppTab.RefreshVisualState()` so tab pills re-resolve palette brushes.
- **Keyboard (WPF parity):** Ctrl+T new tab, Ctrl+W close active, Ctrl(+Shift)+Tab cycle — on `RootGrid.PreviewKeyDown` via `InputKeyboardSource.GetKeyStateForCurrentThread`.
- **Debug seam (DEBUG only):** `--tabsmoke` runs `RunTabSmoke` ~800ms after activation — adds two tabs, activates, `MoveTab` first→end, closes one, appends a state log to `%TEMP%\opennotes_winui_tabsmoke.log` for automation-free environments. `TabCount`/`Tabs`/`ActiveTab` internals expose state.

## Important Notes / NEVER Change
- `CloseTab` here is the UI-level removal only; the WPF editor release/dirty-save protocol (`_tabCloseWorkflows`, `PrepareForCloseAsync`) intentionally arrives with the editor port (Task 9) — do not pre-port.
- AutomationIds on chrome (`NavBackButton`, `NavForwardButton`, `NavHomeButton`, `MinimizeButton`, `MaximizeButton`, `CloseButton`, `NewTabButton`, `TabStrip`, `AppTab`, `TabCloseButton`) are load-bearing for the UIA smoke scripts in `tools/` — keep them stable.
- `MainWindow` ctor order matters: `InitializeAppWindow` (SetTitleBar needs the element) → settings/theme apply → `RegisterWindow` → first home tab.

## Open Threads / Resume Context
- **Status:** GREEN — solution builds 0 errors; real-pointer UIA smoke verified new-tab/activate/middle-close/min/max/restore/close + code-path reorder (see `tools/winui-uia-smoke.ps1`, `winui-uia-pointer-smoke.ps1`).
- Cross-window tab drag/tear-out and `TabDragCoordinator` are NOT ported (later task).
- Search/select/sort/more toolbar clusters intentionally deferred to the HomePage port (Task 5) — only the `MoreButton` AutomationId existed in WPF and it is not yet carried.
