# tools/winui-uia-pointer-smoke.ps1
> Last updated: 2026-09-23 (V6 Task 4 — real-pointer smoke driver) | Protection: STANDARD

## Purpose
Real-mouse-input smoke for a running `OpenNotes.WinUI.exe` window. Reads UIA `BoundingRectangle`s, `SetCursorPos` + `mouse_event` sends real left/middle clicks at element centers. Verifies: new-tab real click, first-tab click selects it (`SelectionPattern`), middle-click closes a tab, `MinimizeButton`/`MaximizeButton` real clicks drive `WindowVisualState` Minimized→Maximized→Normal (proves the title-bar caption-rect passthrough works for real input, not just `InvokePattern`), `NavHomeButton` click inside the `SetTitleBar` element, and a final real click on `CloseButton` closes the window.

## Important Notes
- Inlines a `MouseInput` C# type (`SetCursorPos`/`mouse_event` P/Invoke) — moves the physical mouse cursor; do not run while the user's hands are on the input devices.
- Depends on the same AutomationIds as `winui-uia-smoke.ps1` plus `TabStrip` `SelectionPattern`.
- Reorder is verified separately via the app's `--tabsmoke` DEBUG hook (`%TEMP%\opennotes_winui_tabsmoke.log`), which exercises `MoveTab` semantics; ListView `CanReorderItems` supplies the drag UX.
