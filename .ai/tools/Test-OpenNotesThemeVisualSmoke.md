# Test-OpenNotesThemeVisualSmoke.ps1

> Last updated: 2026-08-22（主题视觉回归工具设计） | Protection: STANDARD

## Purpose

Runs a strict desktop visual smoke against the built OpenNotes executable. It verifies the Light and Dark chrome palettes, a real More context menu, the Settings theme ComboBox popup and its selected-state fills, and Dark-theme persistence after a real Save/process restart.

## Scope and evidence rules

- The script changes no product source and writes only isolated test sidecars plus screenshot/report artifacts outside the repository by default.
- UI Automation is used only to locate and operate real controls. It is never used as proof that a visual state exists.
- Every visual assertion reads PNG pixels captured from the visible desktop with `Graphics.CopyFromScreen` after checking that the OpenNotes process owns the foreground window.
- Palette evidence requires observed pixels near the `ThemeService` Light/Dark surface, foreground, border and accent tokens. Popup captures additionally require observed selection and selection-foreground pixels.
- Contrast is calculated from observed screenshot pixels, not from UIA properties or only from source constants.
- Dark screenshots captured before and after restart are compared by pixel differences; Light and Dark screenshots must show a substantial pixel change. A JSON report records paths, dimensions, palette counts, contrast ratios and comparison metrics.
- If `LockApp` or another process owns the foreground, the script emits `THEME_VISUAL_SMOKE_RESULT=BLOCKED` with the foreground HWND/PID/name and exits non-zero. It must not use a `WM_MOUSE*`/message fallback or claim a pass.

## Parameters

| Parameter | Description |
|---|---|
| `-ExecutablePath` | Optional built `OpenNotes.exe`; defaults to `bin/Debug/net8.0-windows/win-x64/OpenNotes.exe` |
| `-StartupTimeoutSeconds` | Maximum startup/UIA wait, default 20 seconds |
| `-OutputDirectory` | Optional evidence directory; otherwise a unique temp directory is retained |
| `-KeepIsolatedEnvironment` | Keep the exact temporary LOCALAPPDATA/APPDATA/data-root for post-failure inspection |

## Expected evidence

- `light-main.png`, `light-more-menu.png`, `light-theme-popup.png`
- `dark-main-before-restart.png`, `dark-more-menu-before-restart.png`, `dark-theme-popup-before-restart.png`
- `dark-main-after-restart.png`, `dark-more-menu-after-restart.png`, `dark-theme-popup-after-restart.png`
- `theme-visual-report.json`

## Open Threads / Resume Context

- **Status:** in_progress
- **Intent:** finish Task 44.4's real-window theme contrast, popup, selected-state and restart visual regression without modifying product source.
- **Next steps:** parse the new runner, execute it on the real desktop, and retain a clear `PASS`, `FAIL`, or foreground-owner `BLOCKED` result with artifacts.
- **Blockers / notes:** a foreground owner such as `LockApp` is an environment block, not a reason to weaken the pixel gate.

## Agent Decisions / Thoughts

- **2026-08-22:** Screenshot capture uses the actual visible window region and any visible OpenNotes popup windows. UIA bounds may locate the capture region, but only PNG pixels can satisfy assertions.
- **2026-08-22:** The isolated run first saves Light, then captures Light evidence, previews/saves Dark, captures Dark evidence, and compares the same Dark popup/menu before and after process restart. This keeps the persistence comparison deterministic while leaving user data untouched.

## Important Notes / NEVER Change

- Do not alter `ThemeService`, XAML, application settings behavior, or any non-`tools/` source as part of this runner.
- Do not remove the retained screenshot output directory in `finally`; it is the visual evidence handoff.
- Remove only the exact generated isolated environment when `-KeepIsolatedEnvironment` is absent.
- Do not turn UIA-selected values, `IsEnabled`, `IsSelected`, or automation names into visual pass criteria.

## Change History

| Date | Change | Author |
|---|---|---|
| 2026-08-22 | Added design for strict real-window Light/Dark, popup, selected-state, pixel-contrast and restart comparison smoke | Codex |
