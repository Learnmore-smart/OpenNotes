# PopupZOrderHelper

## Purpose

Centralizes the Win32 popup/context-menu z-order workaround used by editor, main-window and settings popups.

## V5 Constraints

- Keep `WS_EX_NOACTIVATE` and the `HWND_NOTOPMOST` transition so popups remain owned by Caelum without floating above other applications after Alt-Tab.
- ComboBox, ContextMenu and tool-popup callers should use the shared helper instead of duplicating user32 interop.

## Completion Status

- The helper is used by editor tool/color/version popups, PDF context menus, MainWindow menus, all four SettingsWindow ComboBoxes, and the three dynamically created HomePage context menus.
- `FixPopupTopmost`, `FixContextMenuTopmost`, and `FixComboBoxPopupTopmost` all remove WPF's unintended topmost band and apply `WS_EX_NOACTIVATE`; the latter two wait for the popup HWND to exist at render priority.
- Code/static verification passes. Alt-Tab and focus behavior still need a real desktop run because Win32 HWND behavior is not covered by headless tests.
