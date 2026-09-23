# tools/winui-home-smoke.ps1
> Last updated: 2026-09-23 (V6 Task 6 — DEBUG context-menu seam preferred) | Protection: STANDARD

## Purpose
End-to-end smoke for the WinUI `HomePage` port. Seeds a throwaway library under `OPENNOTES_DATA_ROOT` (folder + two PDFs + `recent_files.json`), launches the packaged `OpenNotes.WinUI.exe`, then drives UI Automation — plus ONE real right-click for the context-menu check.

## Checks (27)
window-found → add/folder/file tiles render → `home-toolbar-visible-on-home` → **context-menu-opened** (prefers the `Home.DebugOpenContextMenu` DEBUG seam — same `ShowFileContextMenu` path as `ContextRequested` — with real right-click on `HomeTile_smoke-a.pdf` as fallback; expects a `ControlType.Menu` with ≥5 items) → folder invoke → breadcrumb/navigate-up/nested tile → navigate-up returns to root → `SearchBox` filter keeps/hides the right tiles → `SelectButton` shows the selection bar → `Done` hides it → file-tile invoke → `EditorPageTitle`/`EditorPagePath` show the PDF → NavHome returns → `MoreButton` flyout opens with items.

## Important Notes / NEVER Change
- **OS input delivery:** physical right-clicks do not reach the window in this automation session (they land on the desktop site bridge, and stray clicks can navigate/corrupt the run). The DEBUG seam is the preferred path; keep the real-click loop as fallback for normal desktops.
- **Real-input z-order trap:** `mouse_event` right-click lands on whatever top-level window owns the point — `SetWindowPos(HWND_TOP)` cannot outrank a TOPMOST window (e.g. an overlay or the driving console). `Clear-ClickPoint` compares `GetAncestor(WindowFromPoint(pt), GA_ROOT)` with the app hwnd and `SW_MINIMIZE`s covering top-level windows first — note `WindowFromPoint` may return a CHILD of the app, so the ancestor compare is required (comparing raw hwnds minimizes the app itself — regression verified). Minimized foreign HWNDs are tracked in `$script:minimizedHwnds` and `SW_RESTORE`d in `finally`.
- **Window matching:** `Get-Window` requires `NativeWindowHandle == $proc.MainWindowHandle` (with `$proc.Refresh()` per poll) — ANY window titled "OpenNotes" (e.g. the WPF app or a second instance) would otherwise be driven. `AppTitleBar` exposes `AutomationProperties.AutomationId="AppTitleBar"` for the focus helper.
- The context menu is opened by the app's `ContextRequested` handler (MenuFlyout appears as `ControlType.Menu` on the desktop, items as `MenuItem`) — NOT `RightTapped` (ButtonBase swallows it; see `.ai/OpenNotes.WinUI/Pages/HomePage.md`).
- Seed JSON must serialize as an ARRAY: a single-entry `ConvertTo-Json` emits an object and the app loads an empty library — always seed ≥2 entries or force array form.
- `InvokePattern` is used for all other interactions (real clicks are unnecessary + focus-eaten); `Find-OpenMenu` scans the whole desktop for `ControlType.Menu` (flyouts are NOT descendants of the app window).
- Depends on AutomationIds in `HomePage.xaml` (`HomeTile_*`, `HomeScrollViewer`, `FolderBreadcrumb`, `NavigateUpButton`, `SelectionActionBar`, `SearchBox`, `SelectButton`, `MoreButton`, `EditorPageTitle`/`EditorPagePath`) — keep them stable.
- Kills the launched process, restores minimized foreign windows, and deletes the seeded `$dataRoot` in `finally`; requires a Debug build under `OpenNotes.WinUI\bin\x64\Debug\...\win-x64`.
