# PopupZOrderHelper

## Wave6 popup lifecycle (2026-08-24)

- **Status:** green for focused automated scope. Popup, ContextMenu and ComboBox hooks
  remain ConditionalWeakTable-backed, idempotent and exact-Unfix detachable. Each Opened
  callback reasserts the actual MainWindow owner HWND before `HWND_NOTOPMOST`/
  `WS_EX_NOACTIVATE`, so an editor popup stays above its owner but cannot float above
  another application after Alt-Tab. Reopening after a close/localization rebuild is safe.

## Wave6 dual-review follow-up (2026-08-24) — plan before code

- Audit each `Fix*` call owned by EditorPage transient surfaces and pair it with exact
  `Unfix*` cleanup on CloseTransientUi/Unload/close. Preserve ConditionalWeakTable safety,
  owner HWND behavior and idempotent reopen; add hook-count/source tests for TextColor,
  formatting ComboBoxes and PdfViewer context menus.

## Purpose

Centralizes the Win32 popup/context-menu z-order workaround used by editor, main-window and settings popups.

## V5 Constraints

- Keep `WS_EX_NOACTIVATE` and the `HWND_NOTOPMOST` transition so popups remain owned by Caelum without floating above other applications after Alt-Tab.
- ComboBox, ContextMenu and tool-popup callers should use the shared helper instead of duplicating user32 interop.

## Completion Status

- V5.1.1 hotfix: deferred popup callbacks resolve their owner through a null-safe helper. Programmatically opened menus and targets detached before Render priority leave the native owner unchanged instead of passing null to `Window.GetWindow` and terminating the app; callers that own a target explicitly assign it before opening.
- The helper is used by editor tool/color/version popups, PDF context menus, MainWindow menus, all four SettingsWindow ComboBoxes, and the three dynamically created HomePage context menus.
- `FixPopupTopmost`, `FixContextMenuTopmost`, and `FixComboBoxPopupTopmost` all remove WPF's unintended topmost band and apply `WS_EX_NOACTIVATE`; the latter two wait for the popup HWND to exist at render priority.
- Wave 3 dual-review follow-up: popup registrations are idempotent and `UnfixPopupTopmost` removes the exact `Opened` delegate before an EditorPage localization rebuild replaces a tool popup. ContextMenu and ComboBox registrations now use the same weak-table guards (with matching `Unfix*` APIs), so repeated localization/template setup cannot accumulate anonymous delegates.
- Code/static verification passes: focused Wave 3 P2 source/runtime contracts 20/20, full suite 186/186, solution build has 0 errors, and PowerShell smoke-script parsing passes. Explicit-fixture Editor UIA smoke passed; Alt-Tab and focus behavior still need a real desktop run because Win32 HWND behavior is not covered by headless tests.
