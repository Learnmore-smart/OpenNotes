# tools/winui-uia-smoke.ps1
> Last updated: 2026-09-23 (V6 Task 4 — UIA smoke driver) | Protection: STANDARD

## Purpose
UI Automation driver for a running `OpenNotes.WinUI.exe` window. Finds the `OpenNotes` window, counts `AppTab` elements, `InvokePattern`s `NewTabButton` ×2, a `TabCloseButton`, `MinimizeButton`, `MaximizeButton` (checks `WindowVisualState` transitions), `MaximizeButton` again (restore), and `NavHomeButton`.

## Important Notes
- `InvokePattern.Invoke()` exercises the button handlers but does NOT prove real pointer hit-testing — use `winui-uia-pointer-smoke.ps1` for that.
- Depends on the chrome AutomationIds in `MainWindow.xaml` (`AppTab`, `NewTabButton`, `TabCloseButton`, `MinimizeButton`, `MaximizeButton`, `NavHomeButton`) — keep them stable.
